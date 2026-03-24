using Bikiran.Engine.Credentials;
using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Bikiran.Engine.Core;

/// <summary>
/// Fluent builder for constructing and starting flow executions.
/// </summary>
public class FlowBuilder
{
    private readonly string _flowName;
    private readonly List<IFlowNode> _nodes = new();
    private readonly List<IFlowNode> _onSuccessNodes = new();
    private readonly List<IFlowNode> _onFailNodes = new();
    private readonly List<IFlowNode> _onFinishNodes = new();
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

    /// <summary>Adds a node that runs only when all main nodes complete successfully.</summary>
    public FlowBuilder OnSuccess(IFlowNode node)
    {
        _onSuccessNodes.Add(node);
        return this;
    }

    /// <summary>Adds a node that runs only when the flow fails (error or timeout).</summary>
    public FlowBuilder OnFail(IFlowNode node)
    {
        _onFailNodes.Add(node);
        return this;
    }

    /// <summary>Adds a node that always runs after success/fail handlers complete.</summary>
    public FlowBuilder OnFinish(IFlowNode node)
    {
        _onFinishNodes.Add(node);
        return this;
    }

    /// <summary>
    /// Saves the run record, starts background execution, and returns the ServiceId immediately.
    /// </summary>
    public async Task<string> StartAsync()
    {
        var (context, scope, runner) = await PrepareAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                await runner.RunAsync(context, _nodes, _onSuccessNodes, _onFailNodes, _onFinishNodes, CancellationToken.None);
            }
            catch (Exception ex)
            {
                context.Logger?.LogError(ex, "Unhandled exception in background flow '{FlowName}'", _flowName);
            }
            finally
            {
                scope?.Dispose();
            }
        });

        return context.ServiceId;
    }

    /// <summary>
    /// Same as StartAsync() but waits for the flow to complete before returning.
    /// </summary>
    public async Task<string> StartAndWaitAsync()
    {
        var (context, scope, runner) = await PrepareAsync();
        try
        {
            await runner.RunAsync(context, _nodes, _onSuccessNodes, _onFailNodes, _onFinishNodes, CancellationToken.None);
        }
        finally
        {
            scope?.Dispose();
        }
        return context.ServiceId;
    }

    // --- Private helpers ---

    private async Task<(FlowContext context, IServiceScope? scope, FlowRunner runner)> PrepareAsync()
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

        // Expose the DI scope to user nodes so they can resolve services
        // (e.g., AppDbContext) that outlive the HTTP request.
        if (scope != null)
        {
            context.Services = scope.ServiceProvider;
        }

        if (engineDb != null)
        {
            var contextMeta = ContextMeta.FromHttpContext(context.HttpContext);

            var runRecord = new FlowRun
            {
                ServiceId = context.ServiceId,
                FlowName = _flowName,
                Status = FlowRunStatus.Pending.ToString().ToLowerInvariant(),
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

        var runner = new FlowRunner(engineDb);
        return (context, scope, runner);
    }
}
