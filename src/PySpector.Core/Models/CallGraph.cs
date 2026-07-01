namespace PySpector.Core.Models;

/// <summary>
/// Inter-procedural call graph. 1:1 mapping from call_graph_builder.rs CallGraph.
/// </summary>
public sealed class CallGraph
{
    /// <summary>Maps function ID (file::function_name) to its AST node.</summary>
    public Dictionary<string, AstNode> Functions { get; } = [];

    /// <summary>Maps function ID to set of callee function IDs.</summary>
    public Dictionary<string, HashSet<string>> Graph { get; } = [];

    /// <summary>Maps file path to file content (for line extraction and taint pre-filter).</summary>
    public Dictionary<string, string> FileContents { get; } = [];
}
