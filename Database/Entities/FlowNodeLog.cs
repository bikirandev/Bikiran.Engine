using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bikiran.Engine.Database.Entities;

/// <summary>Records execution details for one node within a flow run.</summary>
[Table("FlowNodeLog")]
public class FlowNodeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(36)]
    public string ServiceId { get; set; } = "";

    [Required]
    [MaxLength(100)]
    public string NodeName { get; set; } = "";

    [Required]
    [MaxLength(50)]
    public string NodeType { get; set; } = "";

    public int Sequence { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    public string InputData { get; set; } = "{}";
    public string OutputData { get; set; } = "{}";

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    [MaxLength(20)]
    public string? BranchTaken { get; set; }

    public int RetryCount { get; set; }
    public long StartedAt { get; set; }
    public long CompletedAt { get; set; }
    public long DurationMs { get; set; }
    public long TimeCreated { get; set; }
    public long TimeUpdated { get; set; }
}
