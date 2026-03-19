using Bikiran.Engine.Core;
using Bikiran.Engine.Database.Migration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bikiran.Engine.Extensions;

/// <summary>
/// Hosted service that wires up FlowBuilder on startup and runs the schema migrator.
/// </summary>
internal class EngineStartupService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly BikiranEngineOptions _options;
    private readonly ILogger<EngineStartupService>? _logger;

    public EngineStartupService(
        IServiceProvider services,
        BikiranEngineOptions options,
        ILogger<EngineStartupService>? logger = null)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Inject credentials and service provider into FlowBuilder's static fields
        FlowBuilder.RegisteredCredentials = _options.Credentials;
        FlowBuilder.ServiceProvider = _services;

        // Run auto-migration
        using var scope = _services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<SchemaMigrator>();

        try
        {
            await migrator.MigrateAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Bikiran.Engine: Startup migration failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
