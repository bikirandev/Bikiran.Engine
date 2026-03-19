using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bikiran.Engine.Database.Entities;

/// <summary>Links a flow run to the definition and parameters that triggered it.</summary>
[Table("FlowDefinitionRun")]
public class FlowDefinitionRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(36)]
    public string FlowRunServiceId { get; set; } = "";

    public long DefinitionId { get; set; }

    [Required]
    [MaxLength(100)]
    public string DefinitionKey { get; set; } = "";

    public int DefinitionVersion { get; set; }
    public string Parameters { get; set; } = "{}";
    public long TriggerUserId { get; set; }

    [MaxLength(100)]
    public string TriggerSource { get; set; } = "";

    public long TimeCreated { get; set; }
}
