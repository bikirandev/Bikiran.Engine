using Bikiran.Engine.Core;
using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Migration;
using Bikiran.Engine.Definitions;
using Bikiran.Engine.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bikiran.Engine.Extensions;

/// <summary>
/// Extension methods for registering Bikiran.Engine services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Bikiran.Engine services.
    /// The database provider must be configured separately by the host application
    /// (e.g., via .UseNpgsql(), .UseSqlServer(), or .UseMySql() on EngineDbContext).
    /// </summary>
    public static IServiceCollection AddBikiranEngine(
        this IServiceCollection services,
        Action<BikiranEngineOptions> configure,
        Action<DbContextOptionsBuilder>? dbOptionsAction = null)
    {
        var options = new BikiranEngineOptions();
        configure(options);

        // Register EngineDbContext — consumer must add their own EF provider package.
        // Default: in-memory (for tests/demos). Pass dbOptionsAction to override.
        services.AddDbContext<EngineDbContext>(dbOpts =>
        {
            if (dbOptionsAction != null)
                dbOptionsAction(dbOpts);
            else
                dbOpts.UseInMemoryDatabase("BikiranEngine");
        });

        // Share credentials and service provider with FlowBuilder
        services.AddSingleton<BikiranEngineOptions>(options);

        // Register core services
        services.AddScoped<FlowDefinitionRunner>();
        services.AddSingleton<FlowDefinitionParser>();
        services.AddScoped<SchemaMigrator>();

        // Register scheduler (Quartz must already be registered by the host app)
        services.AddSingleton<FlowSchedulerService>();

        // Register the Quartz job
        services.AddScoped<ScheduledFlowJob>();

        // Wire FlowBuilder with the service provider and credentials on startup
        services.AddHostedService<EngineStartupService>();

        return services;
    }
}
