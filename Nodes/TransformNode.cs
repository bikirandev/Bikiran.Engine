using Bikiran.Engine.Core;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// Reshapes or derives new values from existing context data without making external calls.
/// Provide either Transform (sync) or TransformAsync — if both are set, TransformAsync takes priority.
/// </summary>
public class TransformNode : IFlowNode
{
    public string Name { get; }

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

    /// <inheritdoc />
    public TimeSpan ApproxExecutionTime { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Synchronous transform function.</summary>
    public Func<FlowContext, object?>? Transform { get; set; }

    /// <summary>Asynchronous transform function. Takes priority over Transform if both are set.</summary>
    public Func<FlowContext, CancellationToken, Task<object?>>? TransformAsync { get; set; }

    /// <summary>Context key where the result is stored. Defaults to "{Name}_Result".</summary>
    public string? OutputKey { get; set; }

    /// <summary>When true (default), null results are not written to context.</summary>
    public bool SkipIfNullOutput { get; set; } = true;

    public TransformNode(string name)
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        var outputKey = OutputKey ?? $"{Name}_Result";

        object? result;

        if (TransformAsync != null)
            result = await TransformAsync(context, cancellationToken);
        else if (Transform != null)
            result = Transform(context);
        else
            return NodeResult.Fail($"TransformNode '{Name}': Neither Transform nor TransformAsync is set.");

        if (result != null || !SkipIfNullOutput)
            context.Set(outputKey, result);

        return NodeResult.Ok(result);
    }
}
