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
/// Student endpoints for viewing quizzes based on enrolled levels
/// </summary>
public class StudentEndpoints
{
    private readonly ILogger<StudentEndpoints> _logger;
    private readonly IDbService _dbService;
    private readonly AuthService _authService;

    public StudentEndpoints(ILogger<StudentEndpoints> logger, IDbService dbService, AuthService authService)
    {
        _logger = logger;
        _dbService = dbService;
        _authService = authService;
    }

    /// <summary>
    /// GET /api/student/levels
    /// Get all levels the student is enrolled in with progress
    /// </summary>
    [Function("GetStudentLevels")]
    public async Task<IActionResult> GetStudentLevels(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "student/levels")] HttpRequest req)
    {
        try
        {
            var userId = await AuthorizeStudent(req);
            if (userId == null)
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
                    ul.progress_percentage,
                    COUNT(DISTINCT q.quiz_id) as quiz_count,
                    COUNT(DISTINCT CASE WHEN a.status = 'completed' THEN q.quiz_id END) as completed_quiz_count
                FROM quiz.user_levels ul
                INNER JOIN quiz.levels l ON ul.level_id = l.level_id
                LEFT JOIN quiz.quizzes q ON l.level_id = q.level_id AND q.deleted_at IS NULL
                LEFT JOIN quiz.attempts a ON q.quiz_id = a.quiz_id AND a.user_id = ul.user_id
                WHERE ul.user_id = @userId
                AND l.is_active = true
                AND ul.completed_at IS NULL
                GROUP BY l.level_id, l.level_code, l.level_name, l.description, l.display_order, ul.progress_percentage
                ORDER BY l.display_order";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("userId", userId.Value);

            var levels = new List<StudentLevelInfo>();
            await using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                levels.Add(new StudentLevelInfo
                {
                    LevelId = reader.GetGuid(0),
                    LevelCode = reader.GetString(1),
                    LevelName = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ProgressPercentage = reader.GetDecimal(4),
                    QuizCount = Convert.ToInt32(reader.GetInt64(5)),
                    CompletedQuizCount = Convert.ToInt32(reader.GetInt64(6))
                });
            }

            return new OkObjectResult(levels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student levels");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// GET /api/student/quizzes?levelId={guid}
    /// Get all quizzes for a specific level (or all enrolled levels if not specified)
    /// </summary>
    [Function("GetStudentQuizzes")]
    public async Task<IActionResult> GetStudentQuizzes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "student/quizzes")] HttpRequest req)
    {
        try
        {
            var userId = await AuthorizeStudent(req);
            if (userId == null)
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
                    q.quiz_id,
                    q.title,
                    q.description,
                    q.difficulty,
                    q.estimated_minutes,
                    q.subject,
                    l.level_code,
                    l.level_name,
                    COUNT(DISTINCT qq.question_id) as question_count,
                    MAX(a.started_at) as last_attempt_at,
                    COUNT(DISTINCT CASE WHEN a.status = 'completed' THEN a.attempt_id END) as completed_attempts
                FROM quiz.quizzes q
                INNER JOIN quiz.levels l ON q.level_id = l.level_id
                INNER JOIN quiz.user_levels ul ON l.level_id = ul.level_id
                LEFT JOIN quiz.quiz_questions qq ON q.quiz_id = qq.quiz_id
                LEFT JOIN quiz.attempts a ON q.quiz_id = a.quiz_id AND a.user_id = ul.user_id
                WHERE ul.user_id = @userId
                AND q.deleted_at IS NULL
                AND l.is_active = true
                AND ul.completed_at IS NULL
                AND (@levelId::uuid IS NULL OR l.level_id = @levelId)
                GROUP BY q.quiz_id, q.title, q.description, q.difficulty, q.estimated_minutes, q.subject, l.level_code, l.level_name, l.display_order
                ORDER BY l.display_order, q.created_at DESC";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("userId", userId.Value);
            cmd.Parameters.AddWithValue("levelId", (object?)levelId ?? DBNull.Value);

            var quizzes = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                quizzes.Add(new
                {
                    quizId = reader.GetGuid(0),
                    title = reader.GetString(1),
                    description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    difficulty = reader.IsDBNull(3) ? null : reader.GetString(3),
                    estimatedMinutes = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                    subject = reader.IsDBNull(5) ? null : reader.GetString(5),
                    levelCode = reader.GetString(6),
                    levelName = reader.GetString(7),
                    questionCount = Convert.ToInt32(reader.GetInt64(8)),
                    lastAttemptAt = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9),
                    completedAttempts = Convert.ToInt32(reader.GetInt64(10))
                });
            }

            return new OkObjectResult(quizzes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student quizzes");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<Guid?> AuthorizeStudent(HttpRequest req)
    {
        var authHeader = req.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return null;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var userId = _authService.GetUserIdFromToken(token);
        var role = _authService.GetRoleFromToken(token);
        
        if (userId == null || role != "student")
        {
            return null;
        }

        return userId;
    }
}
