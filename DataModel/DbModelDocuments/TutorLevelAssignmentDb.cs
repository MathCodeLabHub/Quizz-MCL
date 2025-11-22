using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Quizz.DataModel.DbModels;

/// <summary>
/// TutorLevelAssignment database entity - assigns tutors to levels they can manage
/// </summary>
[Table("tutor_level_assignments", Schema = "quiz")]
public class TutorLevelAssignmentDb
{
    [Key]
    [Column("assignment_id")]
    public Guid AssignmentId { get; set; } = Guid.NewGuid();

    [Required]
    [Column("tutor_id")]
    public Guid TutorId { get; set; }

    [Required]
    [Column("level_id")]
    public Guid LevelId { get; set; }

    [Column("assigned_at")]
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    // Navigation properties
    [ForeignKey("TutorId")]
    public virtual UserDb Tutor { get; set; } = null!;

    [ForeignKey("LevelId")]
    public virtual LevelDb Level { get; set; } = null!;
}
