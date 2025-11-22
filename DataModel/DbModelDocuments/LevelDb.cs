using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Quizz.DataModel.DbModels;

/// <summary>
/// Level database entity - represents an education level (e.g., level0 to level4)
/// </summary>
[Table("levels", Schema = "quiz")]
public class LevelDb
{
    [Key]
    [Column("level_id")]
    public Guid LevelId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    [Column("level_code")]
    public string LevelCode { get; set; } = string.Empty; // e.g., "level0", "level1"

    [Required]
    [MaxLength(255)]
    [Column("level_name")]
    public string LevelName { get; set; } = string.Empty; // e.g., "Beginner", "Intermediate"

    [Column("description")]
    public string? Description { get; set; }

    [Required]
    [Column("display_order")]
    public int DisplayOrder { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<UserLevelDb> UserLevels { get; set; } = new List<UserLevelDb>();
    public virtual ICollection<TutorLevelAssignmentDb> TutorAssignments { get; set; } = new List<TutorLevelAssignmentDb>();
    public virtual ICollection<QuizDb> Quizzes { get; set; } = new List<QuizDb>();
}
