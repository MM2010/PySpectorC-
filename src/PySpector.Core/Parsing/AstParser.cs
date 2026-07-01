using System.Text.Json;
using PySpector.Core.Models;

namespace PySpector.Core.Parsing;

/// <summary>
/// Deserializes AST JSON into AstNode tree.
/// 1:1 mapping from ast_parser.rs — PythonFile::new and serde Deserialize.
/// </summary>
public static class AstParser
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Parse a JSON AST string into an AstNode tree.</summary>
    public static AstNode? Parse(string astJson)
    {
        try
        {
            return JsonSerializer.Deserialize<AstNode>(astJson, _options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parse from UTF-8 bytes (avoids string allocation for hot path).</summary>
    public static AstNode? Parse(ReadOnlySpan<byte> astJsonUtf8)
    {
        try
        {
            return JsonSerializer.Deserialize<AstNode>(astJsonUtf8, _options);
        }
        catch
        {
            return null;
        }
    }
}
