using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Quizz.DataModel.DbModels;

/// <summary>
/// UserLevel database entity - represents a student's enrollment in a level (many-to-many)
/// </summary>
[Table("user_levels", Schema = "quiz")]
public class UserLevelDb
{
    [Key]
    [Column("user_level_id")]
    public Guid UserLevelId { get; set; } = Guid.NewGuid();

    [Required]
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Required]
    [Column("level_id")]
    public Guid LevelId { get; set; }

    [Column("enrolled_at")]
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("progress_percentage")]
    [Range(0, 100)]
    public decimal ProgressPercentage { get; set; } = 0;

    // Navigation properties
    [ForeignKey("UserId")]
    public virtual UserDb User { get; set; } = null!;

    [ForeignKey("LevelId")]
    public virtual LevelDb Level { get; set; } = null!;
}
