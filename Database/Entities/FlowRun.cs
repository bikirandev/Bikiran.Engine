using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bikiran.Engine.Database.Entities;

/// <summary>Tracks one workflow execution from start to finish.</summary>
[Table("FlowRun")]
public class FlowRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>Unique run identifier (UUID).</summary>
    [Required]
    [MaxLength(36)]
    public string ServiceId { get; set; } = "";

    /// <summary>Name of the flow.</summary>
    [Required]
    [MaxLength(100)]
    public string FlowName { get; set; } = "";

    /// <summary>Execution status: pending / running / completed / failed / cancelled.</summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>Where the flow was triggered from (e.g., controller name).</summary>
    [MaxLength(100)]
    public string TriggerSource { get; set; } = "";

    /// <summary>JSON-serialized FlowConfig.</summary>
    [Required]
    public string Config { get; set; } = "{}";

    /// <summary>JSON snapshot of caller context (IP, user ID, path).</summary>
    [Required]
    public string ContextMeta { get; set; } = "{}";

    public int TotalNodes { get; set; }
    public int CompletedNodes { get; set; }

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    public long StartedAt { get; set; }
    public long CompletedAt { get; set; }
    public long DurationMs { get; set; }
    public long CreatorUserId { get; set; }

    [MaxLength(100)]
    public string IpString { get; set; } = "";

    public long TimeCreated { get; set; }
    public long TimeUpdated { get; set; }
    public long TimeDeleted { get; set; }
}
