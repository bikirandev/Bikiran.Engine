using Bikiran.Engine.Core;
using Microsoft.EntityFrameworkCore;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// Runs an EF Core query against the host application's DbContext and stores the result in context.
/// Generic parameter TContext must match the type passed to FlowContext.DbContext.
/// </summary>
public class DatabaseQueryNode<TContext> : IFlowNode where TContext : DbContext
{
    public string Name { get; }
    public string NodeType => "DatabaseQuery";

    /// <summary>The EF Core query to execute. Receives the typed DbContext and a CancellationToken.</summary>
    public required Func<TContext, CancellationToken, Task<object?>> Query { get; set; }

    /// <summary>Context key where the query result is stored. Defaults to "{Name}_result".</summary>
    public string? OutputKey { get; set; }

    /// <summary>When true, the node fails if the query returns null. Default is false.</summary>
    public bool FailIfNull { get; set; }

    /// <summary>Error message used when FailIfNull is true and the query returns null.</summary>
    public string NullErrorMessage { get; set; } = "Query returned null";

    public DatabaseQueryNode(string name) => Name = name;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        var outputKey = OutputKey ?? $"{Name}_result";

        // Try the explicitly-set DbContext first, then fall back to DI resolution.
        var db = context.DbContext as TContext ?? context.GetDbContext<TContext>();
        if (db == null)
            return NodeResult.Fail(
                $"DatabaseQueryNode '{Name}': {typeof(TContext).Name} is not available. " +
                $"Either set FlowContext.DbContext or register {typeof(TContext).Name} in the DI container.");

        try
        {
            var result = await Query(db, cancellationToken);

            if (result == null && FailIfNull)
                return NodeResult.Fail(NullErrorMessage);

            context.Set(outputKey, result);
            return NodeResult.Ok(result);
        }
        catch (Exception ex)
        {
            return NodeResult.Fail($"Database query failed: {ex.Message}");
        }
    }
}
