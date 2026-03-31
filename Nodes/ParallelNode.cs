using Bikiran.Engine.Core;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// Runs multiple branches concurrently using Task.WhenAll.
/// Each branch is an independent list of nodes.
/// Thread-safety note: each branch should write to unique context keys.
/// </summary>
public class ParallelNode : IFlowNode
{
    public string Name { get; }
    public FlowNodeType NodeType => FlowNodeType.Parallel;

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

    /// <summary>
    /// List of branches to run concurrently. Each inner list is one parallel branch.
    /// IMPORTANT: branches share the same FlowContext; each branch must write to unique context keys
    /// to avoid race conditions.
    /// </summary>
    public List<List<IFlowNode>> Branches { get; set; } = new();

    /// <summary>When true (default), waits for all branches before continuing.</summary>
    public bool WaitAll { get; set; } = true;

    /// <summary>When true, cancels remaining branches if any branch fails. Default is false.</summary>
    public bool AbortOnBranchFailure { get; set; }

    public ParallelNode(string name)
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        if (Branches.Count == 0)
            return NodeResult.Ok("No branches to run");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var branchTasks = Branches.Select((branch, i) =>
            RunBranchAsync(branch, i + 1, context, cts)).ToList();

        NodeResult[] results;
        try
        {
            results = await Task.WhenAll(branchTasks);
        }
        catch (OperationCanceledException)
        {
            return NodeResult.Fail("ParallelNode was cancelled.");
        }

        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Count > 0)
            return NodeResult.Fail(
                $"ParallelNode '{Name}': {failures.Count} branch(es) failed. First error: {failures[0].ErrorMessage}");

        return NodeResult.Ok($"All {Branches.Count} branches completed");
    }

    private async Task<NodeResult> RunBranchAsync(
        List<IFlowNode> branch,
        int branchIndex,
        FlowContext context,
        CancellationTokenSource cts)
    {
        foreach (var node in branch)
        {
            cts.Token.ThrowIfCancellationRequested();

            var result = await node.ExecuteAsync(context, cts.Token);
            if (!result.Success)
            {
                if (AbortOnBranchFailure)
                    await cts.CancelAsync();

                return NodeResult.Fail(
                    $"Branch {branchIndex} failed at node '{node.Name}': {result.ErrorMessage}");
            }
        }

        return NodeResult.Ok($"Branch {branchIndex} completed");
    }
}
