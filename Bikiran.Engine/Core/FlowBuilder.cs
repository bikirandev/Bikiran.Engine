using Bikiran.Engine.Credentials;
using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Bikiran.Engine.Core;

/// <summary>
/// Fluent builder for constructing and starting flow executions.
/// </summary>
public class FlowBuilder
{
    private readonly string _flowName;
    private readonly List<IFlowNode> _nodes = new();
    private FlowConfig _config = new();
    private Action<FlowContext>? _contextSetup;

    // Credentials registry — set by BikiranEngineOptions and injected at startup
    internal static Dictionary<string, IEngineCredential> RegisteredCredentials { get; set; } = new();

    // Service provider — set by the DI registration
    internal static IServiceProvider? ServiceProvider { get; set; }

    private FlowBuilder(string flowName) => _flowName = flowName;

    /// <summary>Creates a new FlowBuilder for the named flow.</summary>
    public static FlowBuilder Create(string flowName) => new(flowName);

    /// <summary>Sets runtime configuration options.</summary>
    public FlowBuilder Configure(Action<FlowConfig> configure)
    {
        configure(_config);
        return this;
    }

    /// <summary>Injects services (DbContext, HttpContext, Logger, etc.) into the flow context.</summary>
    public FlowBuilder WithContext(Action<FlowContext> setup)
    {
        _contextSetup = setup;
        return this;
    }

    /// <summary>Adds a node to the execution sequence.</summary>
    public FlowBuilder AddNode(IFlowNode node)
    {
        _nodes.Add(node);
        return this;
    }

    /// <summary>
    /// Saves the run record, starts background execution, and returns the ServiceId immediately.
    /// </summary>
    public async Task<string> StartAsync()
    {
        var (context, runner) = await PrepareAsync();
        _ = runner.RunAsync(context, _nodes, CancellationToken.None);
        return context.ServiceId;
    }

    /// <summary>
    /// Same as StartAsync() but waits for the flow to complete before returning.
    /// </summary>
    public async Task<string> StartAndWaitAsync()
    {
        var (context, runner) = await PrepareAsync();
        await runner.RunAsync(context, _nodes, CancellationToken.None);
        return context.ServiceId;
    }

    // --- Private helpers ---

    private async Task<(FlowContext context, FlowRunner runner)> PrepareAsync()
    {
        if (_nodes.Count == 0)
            throw new InvalidOperationException("A flow must have at least one node.");

        var context = new FlowContext
        {
            ServiceId = Guid.NewGuid().ToString(),
            FlowName = _flowName,
            Config = _config,
            Credentials = RegisteredCredentials
        };

        _contextSetup?.Invoke(context);

        // Obtain the database context from DI so FlowRunner can persist run records
        var scope = ServiceProvider?.CreateScope();
        var engineDb = scope?.ServiceProvider.GetService<EngineDbContext>();

        if (engineDb != null)
        {
            var contextMeta = ContextMeta.FromHttpContext(context.HttpContext);

            var runRecord = new FlowRun
            {
                ServiceId = context.ServiceId,
                FlowName = _flowName,
                Status = "pending",
                TriggerSource = _config.TriggerSource,
                Config = JsonSerializer.Serialize(_config),
                ContextMeta = JsonSerializer.Serialize(contextMeta),
                TotalNodes = _nodes.Count,
                CompletedNodes = 0,
                TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await engineDb.FlowRun.AddAsync(runRecord);
            await engineDb.SaveChangesAsync();
        }

        var runner = new FlowRunner(ServiceProvider);
        return (context, runner);
    }
}
