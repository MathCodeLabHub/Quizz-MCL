using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Quizz.DataModel.DbModels;

/// <summary>
/// User database entity - represents a user with authentication and role information
/// </summary>
[Table("users", Schema = "quiz")]
public class UserDb
{
    [Key]
    [Column("user_id")]
    public Guid UserId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(255)]
    [Column("email")]
    public string? Email { get; set; }

    [MaxLength(255)]
    [Column("full_name")]
    public string? FullName { get; set; }

    [Required]
    [MaxLength(20)]
    [Column("role")]
    public string Role { get; set; } = "student"; // student, tutor, admin

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }

    // Navigation properties
    public virtual ICollection<UserLevelDb> UserLevels { get; set; } = new List<UserLevelDb>();
    public virtual ICollection<TutorLevelAssignmentDb> TutorAssignments { get; set; } = new List<TutorLevelAssignmentDb>();
    public virtual ICollection<AttemptDb> Attempts { get; set; } = new List<AttemptDb>();
}
