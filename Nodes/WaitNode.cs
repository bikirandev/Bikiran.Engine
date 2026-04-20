using Bikiran.Engine.Core;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// Pauses flow execution for a configured duration.
/// </summary>
public class WaitNode : IFlowNode
{
    public string Name { get; }

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

    /// <inheritdoc />
    public TimeSpan ApproxExecutionTime { get; set; } = TimeSpan.FromSeconds(1);

    private TimeSpan _delay = TimeSpan.FromSeconds(1);

    /// <summary>Duration to pause. Default is 1 second. Also sets <see cref="ApproxExecutionTime"/>.</summary>
    public TimeSpan Delay
    {
        get => _delay;
        set
        {
            _delay = value;
            ApproxExecutionTime = value;
        }
    }

    public WaitNode(string name)
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(Delay, cancellationToken);
        return NodeResult.Ok($"Waited {Delay.TotalMilliseconds}ms");
    }
}
