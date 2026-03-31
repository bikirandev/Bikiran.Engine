using Bikiran.Engine.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bikiran.Engine.Api;

/// <summary>
/// Admin endpoints for viewing and managing flow run records.
/// </summary>
[ApiController]
[Route("api/bikiran-engine/runs")]
public class FlowRunsController : ControllerBase
{
    private readonly EngineDbContext _db;

    public FlowRunsController(EngineDbContext db) => _db = db;

    /// <summary>List all flow runs, paginated and ordered by most recent first.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var items = await _db.FlowRun
            .Where(r => r.TimeDeleted == 0)
            .OrderByDescending(r => r.TimeCreated)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { error = false, message = "Flow runs", data = items });
    }

    /// <summary>Get full details of a single run including all node logs.</summary>
    [HttpGet("{serviceId}")]
    public async Task<IActionResult> GetByServiceId(string serviceId)
    {
        var run = await _db.FlowRun.FirstOrDefaultAsync(r => r.ServiceId == serviceId);
        if (run == null)
            return NotFound(new { error = true, message = "Flow run not found" });

        var logs = await _db.FlowNodeLog
            .Where(l => l.ServiceId == serviceId)
            .OrderBy(l => l.Sequence)
            .ToListAsync();

        var progressPercent = run.TotalNodes > 0
            ? (int)Math.Round((double)run.CompletedNodes / run.TotalNodes * 100)
            : 0;

        return Ok(new
        {
            error = false,
            message = "Flow run details",
            data = new
            {
                run.ServiceId,
                run.FlowName,
                run.Status,
                run.TriggerSource,
                run.TotalNodes,
                run.CompletedNodes,
                progressPercent,
                run.CurrentProgressMessage,
                run.DurationMs,
                run.StartedAt,
                run.CompletedAt,
                run.ErrorMessage,
                nodeLogs = logs
            }
        });
    }

    /// <summary>Get the current progress percentage of a running flow.</summary>
    [HttpGet("{serviceId}/progress")]
    public async Task<IActionResult> GetProgress(string serviceId)
    {
        var run = await _db.FlowRun.FirstOrDefaultAsync(r => r.ServiceId == serviceId);
        if (run == null)
            return NotFound(new { error = true, message = "Flow run not found" });

        var percent = run.TotalNodes > 0
            ? (int)Math.Round((double)run.CompletedNodes / run.TotalNodes * 100)
            : 0;

        return Ok(new { error = false, data = new { run.ServiceId, run.Status, percent, run.CompletedNodes, run.TotalNodes, run.CurrentProgressMessage } });
    }

    /// <summary>Filter runs by status.</summary>
    [HttpGet("status/{status}")]
    public async Task<IActionResult> GetByStatus(string status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var items = await _db.FlowRun
            .Where(r => r.Status == status && r.TimeDeleted == 0)
            .OrderByDescending(r => r.TimeCreated)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { error = false, message = $"Runs with status '{status}'", data = items });
    }

    /// <summary>Soft-delete a run record.</summary>
    [HttpDelete("{serviceId}")]
    public async Task<IActionResult> Delete(string serviceId)
    {
        var run = await _db.FlowRun.FirstOrDefaultAsync(r => r.ServiceId == serviceId);
        if (run == null)
            return NotFound(new { error = true, message = "Flow run not found" });

        run.TimeDeleted = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        run.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SaveChangesAsync();

        return Ok(new { error = false, message = "Flow run deleted" });
    }
}
