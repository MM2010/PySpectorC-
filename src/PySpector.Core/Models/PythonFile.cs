using System.Collections.Immutable;

namespace PySpector.Core.Models;

/// <summary>
/// Represents a Python file being scanned: path, content, and optional parsed AST.
/// 1:1 mapping from ast_parser.rs PythonFile struct.
/// </summary>
public readonly record struct PythonFile
{
    public string FilePath { get; init; }
    public string Content { get; init; }
    public AstNode? Ast { get; init; }

    public PythonFile(string filePath, string content, AstNode? ast = null)
    {
        FilePath = filePath;
        Content = content;
        Ast = ast;
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Factory: deserialize AST from JSON string (as done in Rust core).</summary>
    public static PythonFile FromAstJson(string filePath, string content, string astJson)
    {
        AstNode? ast = null;
        try
        {
            // Use standard collections for deserialization (ImmutableArray/Dict not supported)
            var raw = System.Text.Json.JsonSerializer.Deserialize<AstNodeRaw>(astJson, _jsonOptions);
            if (raw is not null)
                ast = raw.ToAstNode();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AST deserialize failed: {ex.Message}");
        }
        return new PythonFile(filePath, content, ast);
    }

    /// <summary>Intermediate type for JSON deserialization (standard collections).</summary>
    private sealed class AstNodeRaw
    {
        public string node_type { get; set; } = "";
        public int lineno { get; set; } = -1;
        public int col_offset { get; set; } = -1;
        public Dictionary<string, List<AstNodeRaw>>? children { get; set; }
        public Dictionary<string, System.Text.Json.JsonElement>? fields { get; set; }

        public AstNode ToAstNode()
        {
            ImmutableDictionary<string, ImmutableArray<AstNode>>? childrenDict = null;
            if (children is not null)
            {
                var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<AstNode>>();
                foreach (var kv in children)
                    builder[kv.Key] = kv.Value.Select(c => c.ToAstNode()).ToImmutableArray();
                childrenDict = builder.ToImmutable();
            }

            ImmutableDictionary<string, System.Text.Json.JsonElement?>? fieldsDict = null;
            if (fields is not null)
                fieldsDict = fields.ToImmutableDictionary(
                    kv => kv.Key, kv => (System.Text.Json.JsonElement?)kv.Value);

            return new AstNode
            {
                NodeType = node_type,
                Lineno = lineno,
                ColOffset = col_offset,
                Children = childrenDict,
                Fields = fieldsDict,
            };
        }
    }
}
