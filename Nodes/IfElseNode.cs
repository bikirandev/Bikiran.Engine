using Bikiran.Engine.Core;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// Evaluates a condition and executes one of two branches.
/// Records which branch was taken in context as "{Name}_branch_taken".
/// </summary>
public class IfElseNode : IFlowNode
{
    public string Name { get; }

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

    /// <inheritdoc />
    public TimeSpan ApproxExecutionTime { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The condition to evaluate against the current flow context.</summary>
    public required Func<FlowContext, bool> Condition { get; set; }

    /// <summary>Steps to run when the condition is true.</summary>
    public List<IFlowNode> TrueBranch { get; set; } = new();

    /// <summary>Steps to run when the condition is false.</summary>
    public List<IFlowNode> FalseBranch { get; set; } = new();

    public IfElseNode(string name)
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        var conditionResult = Condition(context);
        var branch = conditionResult ? TrueBranch : FalseBranch;
        var branchLabel = conditionResult ? "true" : "false";

        context.Set($"{Name}_branch_taken", branchLabel);

        foreach (var node in branch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await node.ExecuteAsync(context, cancellationToken);
            if (!result.Success)
                return NodeResult.Fail($"IfElse branch '{branchLabel}' failed at node '{node.Name}': {result.ErrorMessage}");
        }

        return NodeResult.Ok(branchLabel);
    }
}
