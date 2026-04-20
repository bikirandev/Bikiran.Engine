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
    private int _waitNodeCounter;

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
    /// Adds a <see cref="Bikiran.Engine.Nodes.StartingNode"/> as the first step of the flow.
    /// </summary>
    /// <param name="progressMessage">Progress message shown while this node runs.</param>
    /// <param name="waitTime">How long to pause before handing off. Defaults to 1 second.</param>
    public FlowBuilder StartingNode(string progressMessage = "Flow started.", TimeSpan? waitTime = null)
    {
        return AddNode(new Nodes.StartingNode
        {
            ProgressMessage = progressMessage,
            WaitTime = waitTime ?? TimeSpan.FromSeconds(1)
        });
    }

    /// <summary>
    /// Adds an <see cref="Bikiran.Engine.Nodes.EndingNode"/> as the final step of the flow.
    /// </summary>
    /// <param name="progressMessage">Progress message shown while this node runs.</param>
    public FlowBuilder EndingNode(string progressMessage = "Flow completed.")
    {
        return AddNode(new Nodes.EndingNode { ProgressMessage = progressMessage });
    }

    /// <summary>
    /// Adds a <see cref="Bikiran.Engine.Nodes.WaitNode"/> that pauses for the given duration.
    /// </summary>
    /// <param name="progressMessage">Progress message shown while waiting.</param>
    /// <param name="delay">How long to pause.</param>
    public FlowBuilder Wait(string progressMessage, TimeSpan delay)
    {
        _waitNodeCounter++;
        return AddNode(new Nodes.WaitNode($"Wait{_waitNodeCounter}")
        {
            ProgressMessage = progressMessage,
            Delay = delay
        });
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
        ValidateFlow();

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

    /// <summary>
    /// Validates all flow configuration and nodes before execution begins.
    /// Throws <see cref="InvalidOperationException"/> with a clear message if anything is misconfigured.
    /// </summary>
    private void ValidateFlow()
    {
        // Flow name
        if (string.IsNullOrWhiteSpace(_flowName))
            throw new InvalidOperationException(
                "Flow name is required. Pass a non-empty name to FlowBuilder.Create(\"my_flow\").");

        // At least one node
        if (_nodes.Count == 0)
            throw new InvalidOperationException(
                $"Flow '{_flowName}' has no nodes. Add at least one node with .AddNode() before starting.");

        // MaxExecutionTime
        if (_config.MaxExecutionTime <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"Flow '{_flowName}': MaxExecutionTime must be a positive duration, but was {_config.MaxExecutionTime}.");

        // Validate each node name and collect for duplicate check
        var allNodes = new List<(string phase, IFlowNode node)>();
        foreach (var n in _nodes) allNodes.Add(("main", n));
        foreach (var n in _onSuccessNodes) allNodes.Add(("OnSuccess", n));
        foreach (var n in _onFailNodes) allNodes.Add(("OnFail", n));
        foreach (var n in _onFinishNodes) allNodes.Add(("OnFinish", n));

        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (phase, node) in allNodes)
        {
            // Null node check
            if (node == null)
                throw new InvalidOperationException(
                    $"Flow '{_flowName}': A null node was added to the {phase} phase. Remove it or provide a valid IFlowNode instance.");

            // Name validation (PascalCase, non-empty)
            try
            {
                FlowNodeNameValidator.Validate(node.Name);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Flow '{_flowName}', {phase} phase: {ex.Message}");
            }

            // Duplicate name check
            if (!seenNames.Add(node.Name))
                throw new InvalidOperationException(
                    $"Flow '{_flowName}': Duplicate node name '{node.Name}' found. Each node must have a unique name across all phases (main, OnSuccess, OnFail, OnFinish).");
        }
    }
}
