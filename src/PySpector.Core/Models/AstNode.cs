using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace PySpector.Core.Models;

/// <summary>
/// Generic AST node for Python code analysis.
/// 1:1 mapping from ast_parser.rs AstNode struct.
/// </summary>
public sealed record AstNode
{
    [JsonPropertyName("node_type")]
    public string NodeType { get; init; } = string.Empty;

    [JsonPropertyName("lineno")]
    public int Lineno { get; init; } = -1;

    [JsonPropertyName("col_offset")]
    public int ColOffset { get; init; } = -1;

    [JsonPropertyName("children")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableDictionary<string, ImmutableArray<AstNode>>? Children { get; init; }

    [JsonPropertyName("fields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableDictionary<string, System.Text.Json.JsonElement?>? Fields { get; init; }

    /// <summary>
    /// Lookup a child list by name. Returns empty array if not found.
    /// </summary>
    public ImmutableArray<AstNode> GetChildren(string name)
    {
        if (Children is not null && Children.TryGetValue(name, out var list))
            return list;
        return [];
    }

    /// <summary>
    /// Get the first child in a named child list, or null.
    /// </summary>
    public AstNode? GetFirstChild(string name)
    {
        var children = GetChildren(name);
        return children.IsDefaultOrEmpty ? null : children[0];
    }
}
