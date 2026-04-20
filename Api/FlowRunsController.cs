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

        var (weightedPercent, livePercent) = ComputeProgress(run);

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
                run.TotalApproxMs,
                run.CompletedApproxMs,
                weightedProgressPercent = weightedPercent,
                liveProgressPercent = livePercent,
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

        var (weightedPercent, livePercent) = ComputeProgress(run);

        return Ok(new
        {
            error = false,
            data = new
            {
                run.ServiceId,
                run.Status,
                run.TotalNodes,
                run.CompletedNodes,
                run.TotalApproxMs,
                run.CompletedApproxMs,
                weightedProgressPercent = weightedPercent,
                liveProgressPercent = livePercent,
                run.CurrentProgressMessage
            }
        });
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

    // --- Helpers ---

    /// <summary>
    /// Calculates weighted (post-node) and live (intra-node) progress percentages.
    /// <para>
    /// Weighted: CompletedApproxMs / TotalApproxMs * 100 — updated after each node finishes.
    /// Live:     also adds the elapsed portion of the currently executing node, capped at its approx time.
    /// Falls back to step-count progress when TotalApproxMs is zero.
    /// </para>
    /// </summary>
    private static (double weighted, double live) ComputeProgress(Database.Entities.FlowRun run)
    {
        if (run.TotalApproxMs > 0)
        {
            var weighted = Math.Min(100.0,
                Math.Round(run.CompletedApproxMs / (double)run.TotalApproxMs * 100.0, 2));

            var utcNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var intraNodeMs = run.CurrentNodeStartedAtMs > 0
                ? Math.Min(utcNowMs - run.CurrentNodeStartedAtMs, run.CurrentNodeApproxMs)
                : 0L;
            var live = Math.Min(100.0,
                Math.Round((run.CompletedApproxMs + intraNodeMs) / (double)run.TotalApproxMs * 100.0, 2));

            return (weighted, live);
        }

        // Fallback: uniform step-count progress
        var fallback = run.TotalNodes > 0
            ? Math.Round((double)run.CompletedNodes / run.TotalNodes * 100.0, 2)
            : 0.0;
        return (fallback, fallback);
    }
}