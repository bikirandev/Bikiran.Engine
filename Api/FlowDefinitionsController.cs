using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Entities;
using Bikiran.Engine.Definitions;
using Bikiran.Engine.Definitions.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Bikiran.Engine.Api;

/// <summary>
/// Admin endpoints for creating, updating, validating, importing/exporting,
/// versioning, and triggering flow definitions.
/// </summary>
[ApiController]
[Route("api/bikiran-engine/definitions")]
public class FlowDefinitionsController : ControllerBase
{
    private readonly EngineDbContext _db;
    private readonly FlowDefinitionRunner _runner;
    private readonly FlowJsonValidator _validator;

    public FlowDefinitionsController(EngineDbContext db, FlowDefinitionRunner runner, FlowJsonValidator validator)
    {
        _db = db;
        _runner = runner;
        _validator = validator;
    }

    // ── List / Read ─────────────────────────────────────────────────────

    /// <summary>List all definitions (latest version per key), paginated.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var definitions = await _db.FlowDefinition
            .Where(d => d.TimeDeleted == 0)
            .GroupBy(d => d.DefinitionKey)
            .Select(g => g.OrderByDescending(d => d.Version).First())
            .OrderByDescending(d => d.TimeCreated)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { error = false, message = "Flow definitions", data = definitions });
    }

    /// <summary>Get the latest version of a definition.</summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> GetByKey(string key)
    {
        var def = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        if (def == null)
            return NotFound(new { error = true, code = ErrorCodes.DefinitionNotFound, message = "Definition not found" });

        return Ok(new { error = false, data = def });
    }

    /// <summary>List all versions of a definition.</summary>
    [HttpGet("{key}/versions")]
    public async Task<IActionResult> GetVersions(string key)
    {
        var versions = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .ToListAsync();

        return Ok(new { error = false, data = versions });
    }

    // ── Create / Update ─────────────────────────────────────────────────

    /// <summary>Create a new flow definition (validates FlowJson before saving).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FlowDefinitionSaveRequestDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DefinitionKey))
            return BadRequest(new { error = true, code = ErrorCodes.KeyRequired, message = "DefinitionKey is required" });

        var validationErrors = _validator.Validate(dto.FlowJson);
        if (validationErrors.Count > 0)
            return BadRequest(new { error = true, code = ErrorCodes.InvalidFlowJson, message = "FlowJson validation failed", data = validationErrors });

        var def = new FlowDefinition
        {
            DefinitionKey = dto.DefinitionKey,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            FlowJson = dto.FlowJson,
            Tags = dto.Tags,
            ParameterSchema = dto.ParameterSchema,
            Version = 1,
            IsActive = true,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _db.FlowDefinition.Add(def);
        await _db.SaveChangesAsync();

        return Ok(new { error = false, message = "Definition created", data = def });
    }

    /// <summary>Update a definition (auto-increments version, validates FlowJson).</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] FlowDefinitionSaveRequestDTO dto)
    {
        var validationErrors = _validator.Validate(dto.FlowJson);
        if (validationErrors.Count > 0)
            return BadRequest(new { error = true, code = ErrorCodes.InvalidFlowJson, message = "FlowJson validation failed", data = validationErrors });

        var latest = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        // Optimistic concurrency via If-Match header
        if (latest != null && Request.Headers.ContainsKey("If-Match"))
        {
            var expectedVersion = Request.Headers["If-Match"].ToString().Trim('"');
            if (expectedVersion != latest.Version.ToString())
                return Conflict(new { error = true, code = ErrorCodes.VersionConflict, message = $"Version conflict: expected v{expectedVersion}, current is v{latest.Version}" });
        }

        var nextVersion = (latest?.Version ?? 0) + 1;

        var def = new FlowDefinition
        {
            DefinitionKey = key,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            FlowJson = dto.FlowJson,
            Tags = dto.Tags,
            ParameterSchema = dto.ParameterSchema,
            Version = nextVersion,
            IsActive = true,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _db.FlowDefinition.Add(def);
        await _db.SaveChangesAsync();

        Response.Headers["ETag"] = $"\"{nextVersion}\"";
        return Ok(new { error = false, message = $"Definition updated to v{nextVersion}", data = def });
    }

    // ── Toggle / Delete ─────────────────────────────────────────────────

    /// <summary>Enable or disable a definition.</summary>
    [HttpPatch("{key}/toggle")]
    public async Task<IActionResult> Toggle(string key)
    {
        var def = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        if (def == null)
            return NotFound(new { error = true, code = ErrorCodes.DefinitionNotFound, message = "Definition not found" });

        def.IsActive = !def.IsActive;
        def.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SaveChangesAsync();

        return Ok(new { error = false, message = $"Definition is now {(def.IsActive ? "active" : "inactive")}" });
    }

    /// <summary>Soft-delete a definition.</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key)
    {
        var defs = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0)
            .ToListAsync();

        if (defs.Count == 0)
            return NotFound(new { error = true, code = ErrorCodes.DefinitionNotFound, message = "Definition not found" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var d in defs)
        {
            d.TimeDeleted = now;
            d.TimeUpdated = now;
        }

        await _db.SaveChangesAsync();
        return Ok(new { error = false, message = "Definition deleted" });
    }

    // ── Trigger / Dry-Run ───────────────────────────────────────────────

    /// <summary>Trigger a definition with runtime parameters.</summary>
    [HttpPost("{key}/trigger")]
    public async Task<IActionResult> Trigger(string key, [FromBody] FlowDefinitionTriggerRequestDTO dto)
    {
        try
        {
            var serviceId = await _runner.TriggerAsync(
                key,
                dto.Parameters,
                triggerSource: dto.TriggerSource);

            var def = await _db.FlowDefinition
                .Where(d => d.DefinitionKey == key && d.IsActive && d.TimeDeleted == 0)
                .OrderByDescending(d => d.Version)
                .FirstAsync();

            return Ok(new
            {
                error = false,
                message = "Definition triggered",
                data = new FlowDefinitionTriggerResponseDTO
                {
                    ServiceId = serviceId,
                    DefinitionKey = key,
                    DefinitionVersion = def.Version,
                    FlowName = def.DisplayName
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = true, code = ErrorCodes.TriggerFailed, message = ex.Message });
        }
    }

    /// <summary>Dry-run a definition: validates and parses without executing.</summary>
    [HttpPost("{key}/dry-run")]
    public async Task<IActionResult> DryRun(string key, [FromBody] FlowDefinitionTriggerRequestDTO dto)
    {
        try
        {
            var result = await _runner.TriggerAsync(
                key,
                dto.Parameters,
                triggerSource: dto.TriggerSource,
                dryRun: true);

            return Ok(new { error = false, code = ErrorCodes.ValidationOnly, message = "Dry-run succeeded", data = new { dryRunId = result } });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = true, code = ErrorCodes.TriggerFailed, message = ex.Message });
        }
    }

    // ── Runs ────────────────────────────────────────────────────────────

    /// <summary>List all runs triggered from this definition.</summary>
    [HttpGet("{key}/runs")]
    public async Task<IActionResult> GetRuns(string key, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var runs = await _db.FlowDefinitionRun
            .Where(r => r.DefinitionKey == key)
            .OrderByDescending(r => r.TimeCreated)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { error = false, data = runs });
    }

    // ── Validation ──────────────────────────────────────────────────────

    /// <summary>Validates FlowJson without saving.</summary>
    [HttpPost("validate")]
    public IActionResult Validate([FromBody] FlowDefinitionSaveRequestDTO dto)
    {
        var errors = _validator.Validate(dto.FlowJson);
        if (errors.Count > 0)
            return BadRequest(new { error = true, code = ErrorCodes.InvalidFlowJson, message = "Validation failed", data = errors });

        return Ok(new { error = false, code = ErrorCodes.ValidationOnly, message = "FlowJson is valid" });
    }

    // ── Versioning ──────────────────────────────────────────────────────

    /// <summary>Activate a specific version of a definition (deactivates all other versions).</summary>
    [HttpPatch("{key}/versions/{version:int}/activate")]
    public async Task<IActionResult> ActivateVersion(string key, int version)
    {
        var allVersions = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0)
            .ToListAsync();

        var target = allVersions.FirstOrDefault(d => d.Version == version);
        if (target == null)
            return NotFound(new { error = true, code = ErrorCodes.VersionNotFound, message = $"Version {version} not found for '{key}'" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var d in allVersions)
        {
            d.IsActive = d.Version == version;
            d.TimeUpdated = now;
        }

        await _db.SaveChangesAsync();
        return Ok(new { error = false, message = $"Version {version} is now the active version for '{key}'" });
    }

    /// <summary>Compare two versions of a definition side-by-side.</summary>
    [HttpGet("{key}/versions/diff")]
    public async Task<IActionResult> VersionDiff(string key, [FromQuery] int v1, [FromQuery] int v2)
    {
        var versions = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0 && (d.Version == v1 || d.Version == v2))
            .ToListAsync();

        var ver1 = versions.FirstOrDefault(d => d.Version == v1);
        var ver2 = versions.FirstOrDefault(d => d.Version == v2);

        if (ver1 == null || ver2 == null)
            return NotFound(new { error = true, code = ErrorCodes.VersionNotFound, message = "One or both versions not found" });

        return Ok(new
        {
            error = false,
            data = new
            {
                key,
                version1 = new { ver1.Version, ver1.FlowJson, ver1.ParameterSchema, ver1.DisplayName, ver1.IsActive, ver1.TimeCreated },
                version2 = new { ver2.Version, ver2.FlowJson, ver2.ParameterSchema, ver2.DisplayName, ver2.IsActive, ver2.TimeCreated }
            }
        });
    }

    // ── Import / Export ─────────────────────────────────────────────────

    /// <summary>Export a single definition as a JSON document.</summary>
    [HttpGet("{key}/export")]
    public async Task<IActionResult> Export(string key)
    {
        var def = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        if (def == null)
            return NotFound(new { error = true, code = ErrorCodes.DefinitionNotFound, message = "Definition not found" });

        var export = new
        {
            _exportVersion = "1.0",
            def.DefinitionKey,
            def.DisplayName,
            def.Description,
            def.FlowJson,
            def.Tags,
            def.ParameterSchema,
            def.Version,
            ExportedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        return Ok(new { error = false, data = export });
    }

    /// <summary>Export all definitions (latest version each).</summary>
    [HttpGet("export-all")]
    public async Task<IActionResult> ExportAll()
    {
        var definitions = await _db.FlowDefinition
            .Where(d => d.TimeDeleted == 0)
            .GroupBy(d => d.DefinitionKey)
            .Select(g => g.OrderByDescending(d => d.Version).First())
            .ToListAsync();

        var exports = definitions.Select(def => new
        {
            _exportVersion = "1.0",
            def.DefinitionKey,
            def.DisplayName,
            def.Description,
            def.FlowJson,
            def.Tags,
            def.ParameterSchema,
            def.Version
        });

        return Ok(new { error = false, data = new { exportedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), definitions = exports } });
    }

    /// <summary>Import a definition from an exported JSON document. Creates a new version if the key exists.</summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] FlowDefinitionSaveRequestDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DefinitionKey))
            return BadRequest(new { error = true, code = ErrorCodes.KeyRequired, message = "DefinitionKey is required" });

        var validationErrors = _validator.Validate(dto.FlowJson);
        if (validationErrors.Count > 0)
            return BadRequest(new { error = true, code = ErrorCodes.ImportFailed, message = "Import validation failed", data = validationErrors });

        var latest = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == dto.DefinitionKey && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        var nextVersion = (latest?.Version ?? 0) + 1;

        var def = new FlowDefinition
        {
            DefinitionKey = dto.DefinitionKey,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            FlowJson = dto.FlowJson,
            Tags = dto.Tags,
            ParameterSchema = dto.ParameterSchema,
            Version = nextVersion,
            IsActive = true,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _db.FlowDefinition.Add(def);
        await _db.SaveChangesAsync();

        return Ok(new { error = false, message = $"Definition imported as v{nextVersion}", data = def });
    }

    // ── Parameter Extraction ────────────────────────────────────────────

    /// <summary>Extracts all {{placeholder}} parameter names from a FlowJson string.</summary>
    [HttpPost("extract-parameters")]
    public IActionResult ExtractParameters([FromBody] FlowDefinitionSaveRequestDTO dto)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(dto.FlowJson, @"\{\{(\w+)\}\}");
        var paramNames = matches
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        return Ok(new { error = false, data = paramNames });
    }
}
