using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bikiran.Engine.Database.Entities;

/// <summary>Defines an automated trigger for a flow definition.</summary>
[Table("FlowSchedule")]
public class FlowSchedule
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string ScheduleKey { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = "";

    [Required]
    [MaxLength(100)]
    public string DefinitionKey { get; set; } = "";

    /// <summary>Schedule type: cron / interval / once.</summary>
    [Required]
    [MaxLength(20)]
    public string ScheduleType { get; set; } = "";

    [MaxLength(100)]
    public string? CronExpression { get; set; }

    public int? IntervalMinutes { get; set; }
    public long? RunOnceAt { get; set; }
    public string DefaultParameters { get; set; } = "{}";
    public bool IsActive { get; set; } = true;

    [MaxLength(50)]
    public string TimeZone { get; set; } = "UTC";

    public int MaxConcurrent { get; set; } = 1;
    public long LastRunAt { get; set; }
    public long NextRunAt { get; set; }

    [MaxLength(36)]
    public string? LastRunServiceId { get; set; }

    [MaxLength(20)]
    public string? LastRunStatus { get; set; }

    public long CreatedBy { get; set; }
    public long TimeCreated { get; set; }
    public long TimeUpdated { get; set; }
    public long TimeDeleted { get; set; }
}
