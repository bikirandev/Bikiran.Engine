using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bikiran.Engine.Database.Entities;

/// <summary>Stores a reusable flow template as JSON.</summary>
[Table("FlowDefinition")]
public class FlowDefinition
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string DefinitionKey { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    [Required]
    public string FlowJson { get; set; } = "{}";

    [MaxLength(500)]
    public string Tags { get; set; } = "";

    public long LastModifiedBy { get; set; }
    public long TimeCreated { get; set; }
    public long TimeUpdated { get; set; }
    public long TimeDeleted { get; set; }
}
