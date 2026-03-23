using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Bikiran.Engine.Scheduling;

/// <summary>
/// Manages Quartz.NET job registrations for all active flow schedules.
/// Call InitializeAsync() on app startup to load and register all active schedules.
/// </summary>
public class FlowSchedulerService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FlowSchedulerService>? _logger;

    public FlowSchedulerService(
        ISchedulerFactory schedulerFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<FlowSchedulerService>? logger = null)
    {
        _schedulerFactory = schedulerFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Loads all active schedules from the database and registers them with Quartz.NET.</summary>
    public async Task InitializeAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EngineDbContext>();

        var schedules = await db.FlowSchedule
            .Where(s => s.IsActive && s.TimeDeleted == 0)
            .ToListAsync();

        var scheduler = await _schedulerFactory.GetScheduler();

        foreach (var schedule in schedules)
        {
            try
            {
                await RegisterScheduleInternalAsync(scheduler, schedule);
                _logger?.LogInformation(
                    "Bikiran.Engine: Registered schedule '{Key}' ({Type})", schedule.ScheduleKey, schedule.ScheduleType);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to register schedule '{Key}'.", schedule.ScheduleKey);
            }
        }

        await scheduler.Start();
    }

    /// <summary>Adds or updates a schedule in Quartz without requiring an application restart.</summary>
    public async Task RegisterScheduleAsync(FlowSchedule schedule)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await RegisterScheduleInternalAsync(scheduler, schedule);
    }

    /// <summary>Removes a schedule from Quartz by its key.</summary>
    public async Task UnregisterScheduleAsync(string scheduleKey)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(scheduleKey, "BikiranEngine");
        if (await scheduler.CheckExists(jobKey))
            await scheduler.DeleteJob(jobKey);
    }

    /// <summary>Returns the next expected trigger time for a schedule, or null if not registered.</summary>
    public async Task<DateTimeOffset?> GetNextFireTimeAsync(string scheduleKey)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var triggerKey = new TriggerKey(scheduleKey, "BikiranEngine");
        var trigger = await scheduler.GetTrigger(triggerKey);
        return trigger?.GetNextFireTimeUtc();
    }

    private async Task RegisterScheduleInternalAsync(IScheduler scheduler, FlowSchedule schedule)
    {
        var jobKey = new JobKey(schedule.ScheduleKey, "BikiranEngine");
        var triggerKey = new TriggerKey(schedule.ScheduleKey, "BikiranEngine");

        // Remove any existing registration for this key
        if (await scheduler.CheckExists(jobKey))
            await scheduler.DeleteJob(jobKey);

        var job = JobBuilder.Create<ScheduledFlowJob>()
            .WithIdentity(jobKey)
            .UsingJobData(ScheduledFlowJob.ScheduleKeyData, schedule.ScheduleKey)
            .StoreDurably()
            .Build();

        ITrigger trigger = schedule.ScheduleType switch
        {
            "cron" => BuildCronTrigger(schedule, triggerKey),
            "interval" => BuildIntervalTrigger(schedule, triggerKey),
            "once" => BuildOnceTrigger(schedule, triggerKey),
            _ => throw new InvalidOperationException($"Unknown schedule type '{schedule.ScheduleType}'.")
        };

        await scheduler.ScheduleJob(job, trigger);
    }

    private static ITrigger BuildCronTrigger(FlowSchedule schedule, TriggerKey triggerKey)
    {
        if (string.IsNullOrEmpty(schedule.CronExpression))
            throw new InvalidOperationException(
                $"Schedule '{schedule.ScheduleKey}': CronExpression is required for cron type.");

        var tz = TimeZoneInfo.Utc;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone ?? "UTC");
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"Schedule '{schedule.ScheduleKey}': Invalid TimeZone '{schedule.TimeZone}'. " +
                "Provide a valid IANA or Windows timezone ID.", ex);
        }

        return TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule(schedule.CronExpression, x => x
                .InTimeZone(tz)
                .WithMisfireHandlingInstructionDoNothing())
            .Build();
    }

    private static ITrigger BuildIntervalTrigger(FlowSchedule schedule, TriggerKey triggerKey)
    {
        if (!schedule.IntervalMinutes.HasValue || schedule.IntervalMinutes.Value <= 0)
            throw new InvalidOperationException(
                $"Schedule '{schedule.ScheduleKey}': IntervalMinutes must be positive for interval type.");

        return TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithSimpleSchedule(x => x
                .WithIntervalInMinutes(schedule.IntervalMinutes.Value)
                .RepeatForever()
                .WithMisfireHandlingInstructionNextWithRemainingCount())
            .Build();
    }

    private static ITrigger BuildOnceTrigger(FlowSchedule schedule, TriggerKey triggerKey)
    {
        if (!schedule.RunOnceAt.HasValue)
            throw new InvalidOperationException(
                $"Schedule '{schedule.ScheduleKey}': RunOnceAt is required for once type.");

        var fireAt = DateTimeOffset.FromUnixTimeSeconds(schedule.RunOnceAt.Value);

        return TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .StartAt(fireAt)
            .WithSimpleSchedule(x => x
                .WithRepeatCount(0)
                .WithMisfireHandlingInstructionFireNow())
            .Build();
    }
}
