using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Entities;
using Bikiran.Engine.Scheduling;
using Bikiran.Engine.Scheduling.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quartz;
using System.Text.Json;

namespace Bikiran.Engine.Api;

/// <summary>
/// Admin endpoints for managing automated flow schedule triggers.
/// </summary>
[ApiController]
[Route("api/bikiran-engine/schedules")]
public class FlowSchedulesController : ControllerBase
{
    private readonly EngineDbContext _db;
    private readonly FlowSchedulerService _scheduler;
    private readonly ISchedulerFactory _schedulerFactory;

    public FlowSchedulesController(EngineDbContext db, FlowSchedulerService scheduler, ISchedulerFactory schedulerFactory)
    {
        _db = db;
        _scheduler = scheduler;
        _schedulerFactory = schedulerFactory;
    }

    /// <summary>List all schedules.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var schedules = await _db.FlowSchedule
            .Where(s => s.TimeDeleted == 0)
            .OrderByDescending(s => s.TimeCreated)
            .ToListAsync();

        return Ok(new { error = false, data = schedules });
    }

    /// <summary>Get schedule details including next fire time.</summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> GetByKey(string key)
    {
        var schedule = await _db.FlowSchedule.FirstOrDefaultAsync(s => s.ScheduleKey == key && s.TimeDeleted == 0);
        if (schedule == null)
            return NotFound(new { error = true, message = "Schedule not found" });

        var nextFire = await _scheduler.GetNextFireTimeAsync(key);

        return Ok(new
        {
            error = false,
            data = new FlowScheduleSummaryDTO
            {
                Id = schedule.Id,
                ScheduleKey = schedule.ScheduleKey,
                DisplayName = schedule.DisplayName,
                DefinitionKey = schedule.DefinitionKey,
                ScheduleType = schedule.ScheduleType,
                IsActive = schedule.IsActive,
                LastRunAt = schedule.LastRunAt,
                LastRunStatus = schedule.LastRunStatus,
                LastRunServiceId = schedule.LastRunServiceId,
                NextRunAt = nextFire.HasValue ? nextFire.Value.ToUnixTimeSeconds() : 0
            }
        });
    }

    /// <summary>Create a new schedule.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FlowScheduleSaveRequestDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ScheduleKey))
            return BadRequest(new { error = true, message = "ScheduleKey is required" });

        var schedule = new FlowSchedule
        {
            ScheduleKey = dto.ScheduleKey,
            DisplayName = dto.DisplayName,
            DefinitionKey = dto.DefinitionKey,
            ScheduleType = dto.ScheduleType,
            CronExpression = dto.CronExpression,
            IntervalMinutes = dto.IntervalMinutes,
            RunOnceAt = dto.RunOnceAt,
            DefaultParameters = JsonSerializer.Serialize(dto.DefaultParameters),
            IsActive = true,
            TimeZone = dto.TimeZone,
            MaxConcurrent = dto.MaxConcurrent,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        _db.FlowSchedule.Add(schedule);
        await _db.SaveChangesAsync();

        await _scheduler.RegisterScheduleAsync(schedule);

        return Ok(new { error = false, message = "Schedule created", data = schedule });
    }

    /// <summary>Update a schedule and re-register it in Quartz.</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] FlowScheduleSaveRequestDTO dto)
    {
        var schedule = await _db.FlowSchedule.FirstOrDefaultAsync(s => s.ScheduleKey == key && s.TimeDeleted == 0);
        if (schedule == null)
            return NotFound(new { error = true, message = "Schedule not found" });

        schedule.DisplayName = dto.DisplayName;
        schedule.DefinitionKey = dto.DefinitionKey;
        schedule.ScheduleType = dto.ScheduleType;
        schedule.CronExpression = dto.CronExpression;
        schedule.IntervalMinutes = dto.IntervalMinutes;
        schedule.RunOnceAt = dto.RunOnceAt;
        schedule.DefaultParameters = JsonSerializer.Serialize(dto.DefaultParameters);
        schedule.TimeZone = dto.TimeZone;
        schedule.MaxConcurrent = dto.MaxConcurrent;
        schedule.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await _db.SaveChangesAsync();
        await _scheduler.RegisterScheduleAsync(schedule);

        return Ok(new { error = false, message = "Schedule updated" });
    }

    /// <summary>Enable or disable a schedule.</summary>
    [HttpPatch("{key}/toggle")]
    public async Task<IActionResult> Toggle(string key)
    {
        var schedule = await _db.FlowSchedule.FirstOrDefaultAsync(s => s.ScheduleKey == key && s.TimeDeleted == 0);
        if (schedule == null)
            return NotFound(new { error = true, message = "Schedule not found" });

        schedule.IsActive = !schedule.IsActive;
        schedule.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SaveChangesAsync();

        if (schedule.IsActive)
            await _scheduler.RegisterScheduleAsync(schedule);
        else
            await _scheduler.UnregisterScheduleAsync(key);

        return Ok(new { error = false, message = $"Schedule is now {(schedule.IsActive ? "active" : "paused")}" });
    }

    /// <summary>Soft-delete and unregister a schedule.</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key)
    {
        var schedule = await _db.FlowSchedule.FirstOrDefaultAsync(s => s.ScheduleKey == key && s.TimeDeleted == 0);
        if (schedule == null)
            return NotFound(new { error = true, message = "Schedule not found" });

        schedule.TimeDeleted = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        schedule.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SaveChangesAsync();

        await _scheduler.UnregisterScheduleAsync(key);

        return Ok(new { error = false, message = "Schedule deleted" });
    }

    /// <summary>Trigger a schedule immediately.</summary>
    [HttpPost("{key}/run-now")]
    public async Task<IActionResult> RunNow(string key)
    {
        var schedule = await _db.FlowSchedule.FirstOrDefaultAsync(s => s.ScheduleKey == key && s.TimeDeleted == 0);
        if (schedule == null)
            return NotFound(new { error = true, message = "Schedule not found" });

        var quartzScheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(key, "BikiranEngine");

        if (!await quartzScheduler.CheckExists(jobKey))
            await _scheduler.RegisterScheduleAsync(schedule);

        await quartzScheduler.TriggerJob(jobKey);

        return Ok(new { error = false, message = "Schedule triggered immediately" });
    }

    /// <summary>List all runs triggered by this schedule.</summary>
    [HttpGet("{key}/runs")]
    public async Task<IActionResult> GetRuns(string key, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var triggerSource = $"FlowSchedule:{key}";
        var runs = await _db.FlowRun
            .Where(r => r.TriggerSource == triggerSource && r.TimeDeleted == 0)
            .OrderByDescending(r => r.TimeCreated)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { error = false, data = runs });
    }
}
