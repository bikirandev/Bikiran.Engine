using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Entities;
using Bikiran.Engine.Definitions;
using Bikiran.Engine.Definitions.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bikiran.Engine.Api;

/// <summary>
/// Admin endpoints for creating, updating, and triggering flow definitions.
/// </summary>
[ApiController]
[Route("api/bikiran-engine/definitions")]
public class FlowDefinitionsController : ControllerBase
{
    private readonly EngineDbContext _db;
    private readonly FlowDefinitionRunner _runner;

    public FlowDefinitionsController(EngineDbContext db, FlowDefinitionRunner runner)
    {
        _db = db;
        _runner = runner;
    }

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
            return NotFound(new { error = true, message = "Definition not found" });

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

    /// <summary>Create a new flow definition.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FlowDefinitionSaveRequestDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DefinitionKey))
            return BadRequest(new { error = true, message = "DefinitionKey is required" });

        var def = new FlowDefinition
        {
            DefinitionKey = dto.DefinitionKey,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            FlowJson = dto.FlowJson,
            Tags = dto.Tags,
            Version = 1,
            IsActive = true,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _db.FlowDefinition.Add(def);
        await _db.SaveChangesAsync();

        return Ok(new { error = false, message = "Definition created", data = def });
    }

    /// <summary>Update a definition (auto-increments version).</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] FlowDefinitionSaveRequestDTO dto)
    {
        var latest = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        var nextVersion = (latest?.Version ?? 0) + 1;

        var def = new FlowDefinition
        {
            DefinitionKey = key,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            FlowJson = dto.FlowJson,
            Tags = dto.Tags,
            Version = nextVersion,
            IsActive = true,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _db.FlowDefinition.Add(def);
        await _db.SaveChangesAsync();

        return Ok(new { error = false, message = $"Definition updated to v{nextVersion}", data = def });
    }

    /// <summary>Enable or disable a definition.</summary>
    [HttpPatch("{key}/toggle")]
    public async Task<IActionResult> Toggle(string key)
    {
        var def = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        if (def == null)
            return NotFound(new { error = true, message = "Definition not found" });

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
            return NotFound(new { error = true, message = "Definition not found" });

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var d in defs)
        {
            d.TimeDeleted = now;
            d.TimeUpdated = now;
        }

        await _db.SaveChangesAsync();
        return Ok(new { error = false, message = "Definition deleted" });
    }

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
                .Where(d => d.DefinitionKey == key && d.TimeDeleted == 0)
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
            return BadRequest(new { error = true, message = ex.Message });
        }
    }

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
}
