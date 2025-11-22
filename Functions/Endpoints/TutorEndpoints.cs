using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Npgsql;
using Quizz.DataModel.ApiModels;
using Quizz.DataAccess;
using Quizz.Auth;

namespace Quizz.Functions.Endpoints;

/// <summary>
/// Tutor endpoints for managing quizzes and viewing student responses
/// </summary>
public class TutorEndpoints
{
    private readonly ILogger<TutorEndpoints> _logger;
    private readonly IDbService _dbService;
    private readonly AuthService _authService;

    public TutorEndpoints(ILogger<TutorEndpoints> logger, IDbService dbService, AuthService authService)
    {
        _logger = logger;
        _dbService = dbService;
        _authService = authService;
    }

    /// <summary>
    /// GET /api/tutor/levels
    /// Get all levels assigned to the tutor with statistics
    /// </summary>
    [Function("GetTutorLevels")]
    public async Task<IActionResult> GetTutorLevels(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tutor/levels")] HttpRequest req)
    {
        try
        {
            var tutorId = await AuthorizeTutor(req);
            if (tutorId == null)
            {
                return new UnauthorizedObjectResult(new { error = "Authentication required" });
            }

            await using var conn = await _dbService.GetConnectionAsync();

            var sql = @"
                SELECT 
                    l.level_id,
                    l.level_code,
                    l.level_name,
                    l.description,
                    COUNT(DISTINCT ul.user_id) as student_count,
                    COUNT(DISTINCT q.quiz_id) as quiz_count
                FROM quiz.levels l
                INNER JOIN quiz.tutor_level_assignments tla ON l.level_id = tla.level_id
                LEFT JOIN quiz.user_levels ul ON l.level_id = ul.level_id AND ul.completed_at IS NULL
                LEFT JOIN quiz.quizzes q ON l.level_id = q.level_id AND q.deleted_at IS NULL
                WHERE tla.tutor_id = @tutorId
                AND tla.is_active = true
                AND l.is_active = true
                GROUP BY l.level_id, l.level_code, l.level_name, l.description, l.display_order
                ORDER BY l.display_order";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("tutorId", tutorId.Value);

            var levels = new List<TutorLevelInfo>();
            await using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                levels.Add(new TutorLevelInfo
                {
                    LevelId = reader.GetGuid(0),
                    LevelCode = reader.GetString(1),
                    LevelName = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                    StudentCount = reader.GetInt64(4),
                    QuizCount = reader.GetInt64(5)
                });
            }

            return new OkObjectResult(levels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tutor levels");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// GET /api/tutor/responses?levelId={guid}
    /// Get student responses for quizzes in assigned levels
    /// </summary>
    [Function("GetStudentResponses")]
    public async Task<IActionResult> GetStudentResponses(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tutor/responses")] HttpRequest req)
    {
        try
        {
            var tutorId = await AuthorizeTutor(req);
            if (tutorId == null)
            {
                return new UnauthorizedObjectResult(new { error = "Authentication required" });
            }

            var levelIdParam = req.Query["levelId"].FirstOrDefault();
            Guid? levelId = null;
            if (!string.IsNullOrEmpty(levelIdParam) && Guid.TryParse(levelIdParam, out var parsedLevelId))
            {
                levelId = parsedLevelId;
            }

            await using var conn = await _dbService.GetConnectionAsync();

            var sql = @"
                SELECT 
                    r.response_id,
                    u.username as student_username,
                    u.full_name as student_full_name,
                    qz.title as quiz_title,
                    qs.question_text,
                    r.submitted_at,
                    r.points_earned,
                    r.points_possible,
                    r.is_correct,
                    l.level_code,
                    r.answer_payload,
                    r.grading_details
                FROM quiz.responses r
                INNER JOIN quiz.attempts a ON r.attempt_id = a.attempt_id
                INNER JOIN quiz.users u ON a.user_id = u.user_id
                INNER JOIN quiz.quizzes qz ON a.quiz_id = qz.quiz_id
                INNER JOIN quiz.questions qs ON r.question_id = qs.question_id
                INNER JOIN quiz.levels l ON qz.level_id = l.level_id
                INNER JOIN quiz.tutor_level_assignments tla ON l.level_id = tla.level_id
                WHERE tla.tutor_id = @tutorId
                AND tla.is_active = true
                AND (@levelId::uuid IS NULL OR l.level_id = @levelId)
                AND qz.deleted_at IS NULL
                AND qs.deleted_at IS NULL
                ORDER BY r.submitted_at DESC
                LIMIT 100";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("tutorId", tutorId.Value);
            cmd.Parameters.AddWithValue("levelId", (object?)levelId ?? DBNull.Value);

            var responses = new List<StudentResponseForTutor>();
            await using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                responses.Add(new StudentResponseForTutor
                {
                    ResponseId = reader.GetGuid(0),
                    StudentUsername = reader.GetString(1),
                    StudentFullName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    QuizTitle = reader.GetString(3),
                    QuestionText = reader.GetString(4),
                    SubmittedAt = reader.GetDateTime(5),
                    PointsEarned = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    PointsPossible = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                    IsCorrect = reader.IsDBNull(8) ? null : reader.GetBoolean(8),
                    LevelCode = reader.GetString(9)
                    // Note: answer_payload and grading_details are JSONB, would need JsonDocument parsing
                });
            }

            return new OkObjectResult(responses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student responses");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// GET /api/tutor/students?levelId={guid}
    /// Get all students in tutor's assigned levels
    /// </summary>
    [Function("GetLevelStudents")]
    public async Task<IActionResult> GetLevelStudents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tutor/students")] HttpRequest req)
    {
        try
        {
            var tutorId = await AuthorizeTutor(req);
            if (tutorId == null)
            {
                return new UnauthorizedObjectResult(new { error = "Authentication required" });
            }

            var levelIdParam = req.Query["levelId"].FirstOrDefault();
            if (string.IsNullOrEmpty(levelIdParam) || !Guid.TryParse(levelIdParam, out var levelId))
            {
                return new BadRequestObjectResult(new { error = "Valid levelId is required" });
            }

            await using var conn = await _dbService.GetConnectionAsync();

            // Verify tutor has access to this level
            var accessSql = @"
                SELECT COUNT(*) 
                FROM quiz.tutor_level_assignments 
                WHERE tutor_id = @tutorId AND level_id = @levelId AND is_active = true";
            
            await using var accessCmd = new NpgsqlCommand(accessSql, conn);
            accessCmd.Parameters.AddWithValue("tutorId", tutorId.Value);
            accessCmd.Parameters.AddWithValue("levelId", levelId);
            
            var hasAccess = (long)(await accessCmd.ExecuteScalarAsync() ?? 0) > 0;
            if (!hasAccess)
            {
                return new ForbidResult();
            }

            var sql = @"
                SELECT 
                    u.user_id,
                    u.username,
                    u.full_name,
                    u.email,
                    ul.enrolled_at,
                    ul.progress_percentage,
                    COUNT(DISTINCT a.attempt_id) as total_attempts,
                    COUNT(DISTINCT CASE WHEN a.status = 'completed' THEN a.attempt_id END) as completed_attempts,
                    AVG(CASE WHEN a.status = 'completed' THEN a.total_score END) as avg_score
                FROM quiz.users u
                INNER JOIN quiz.user_levels ul ON u.user_id = ul.user_id
                LEFT JOIN quiz.quizzes q ON ul.level_id = q.level_id AND q.deleted_at IS NULL
                LEFT JOIN quiz.attempts a ON q.quiz_id = a.quiz_id AND a.user_id = u.user_id
                WHERE ul.level_id = @levelId
                AND ul.completed_at IS NULL
                AND u.deleted_at IS NULL
                AND u.role = 'student'
                GROUP BY u.user_id, u.username, u.full_name, u.email, ul.enrolled_at, ul.progress_percentage
                ORDER BY u.username";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("levelId", levelId);

            var students = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                students.Add(new
                {
                    userId = reader.GetGuid(0),
                    username = reader.GetString(1),
                    fullName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    email = reader.IsDBNull(3) ? null : reader.GetString(3),
                    enrolledAt = reader.GetDateTime(4),
                    progressPercentage = reader.GetDecimal(5),
                    totalAttempts = Convert.ToInt32(reader.GetInt64(6)),
                    completedAttempts = Convert.ToInt32(reader.GetInt64(7)),
                    avgScore = reader.IsDBNull(8) ? (decimal?)null : Convert.ToDecimal(reader.GetValue(8))
                });
            }

            return new OkObjectResult(students);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting level students");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<Guid?> AuthorizeTutor(HttpRequest req)
    {
        var authHeader = req.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return null;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var userId = _authService.GetUserIdFromToken(token);
        var role = _authService.GetRoleFromToken(token);
        
        if (userId == null || (role != "tutor" && role != "admin"))
        {
            return null;
        }

        return userId;
    }
}
