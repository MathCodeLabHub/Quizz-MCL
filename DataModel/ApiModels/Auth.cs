namespace Quizz.DataModel.ApiModels;

/// <summary>
/// Login request model
/// </summary>
public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Login response model
/// </summary>
public class LoginResponse
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty; // JWT token
    public DateTime ExpiresAt { get; set; }
    public List<UserLevelInfo> EnrolledLevels { get; set; } = new();
}

/// <summary>
/// Signup/Create user request model (admin only)
/// </summary>
public class SignupRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string Role { get; set; } = "student"; // student, tutor, admin
    public List<string> LevelCodes { get; set; } = new(); // e.g., ["level0", "level1"]
}

/// <summary>
/// Signup response model
/// </summary>
public class SignupResponse
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> EnrolledLevels { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// User level information
/// </summary>
public class UserLevelInfo
{
    public Guid LevelId { get; set; }
    public string LevelCode { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
    public decimal ProgressPercentage { get; set; }
    public bool IsCompleted { get; set; }
}

/// <summary>
/// User profile model
/// </summary>
public class UserProfile
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<UserLevelInfo> EnrolledLevels { get; set; } = new();
}

/// <summary>
/// Level with quiz count for students
/// </summary>
public class StudentLevelInfo
{
    public Guid LevelId { get; set; }
    public string LevelCode { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal ProgressPercentage { get; set; }
    public int QuizCount { get; set; }
    public int CompletedQuizCount { get; set; }
}

/// <summary>
/// Level with statistics for tutors
/// </summary>
public class TutorLevelInfo
{
    public Guid LevelId { get; set; }
    public string LevelCode { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long StudentCount { get; set; }
    public long QuizCount { get; set; }
}

/// <summary>
/// Student response for tutor review
/// </summary>
public class StudentResponseForTutor
{
    public Guid ResponseId { get; set; }
    public string StudentUsername { get; set; } = string.Empty;
    public string? StudentFullName { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public decimal? PointsEarned { get; set; }
    public decimal? PointsPossible { get; set; }
    public bool? IsCorrect { get; set; }
    public string LevelCode { get; set; } = string.Empty;
    public object? AnswerPayload { get; set; }
    public object? GradingDetails { get; set; }
}
