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

    /// <summary>Sum of all main nodes' ApproxExecutionTime in ms.</summary>
    public long TotalApproxMs { get; set; }

    /// <summary>Rolling sum of completed main nodes' approx execution times in ms.</summary>
    public long CompletedApproxMs { get; set; }

    /// <summary>Approx execution time of the currently executing node in ms (0 when idle).</summary>
    public long CurrentNodeApproxMs { get; set; }

    /// <summary>UTC milliseconds when the current node started executing (0 when idle).</summary>
    public long CurrentNodeStartedAtMs { get; set; }

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    /// <summary>Progress message from the currently executing node.</summary>
    [MaxLength(500)]
    public string? CurrentProgressMessage { get; set; }

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
