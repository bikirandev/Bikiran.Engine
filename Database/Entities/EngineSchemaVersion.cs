using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bikiran.Engine.Database.Entities;

/// <summary>Single-row table tracking the current database schema version for auto-migration.</summary>
[Table("EngineSchemaVersion")]
public class EngineSchemaVersion
{
    /// <summary>Always 1 — this table always has exactly one row.</summary>
    [Key]
    public int Id { get; set; } = 1;

    [Required]
    [MaxLength(20)]
    public string SchemaVersion { get; set; } = "";

    public long AppliedAt { get; set; }

    [Required]
    [MaxLength(20)]
    public string PackageVersion { get; set; } = "";
}
