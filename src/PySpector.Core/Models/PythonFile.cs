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
            ast = System.Text.Json.JsonSerializer.Deserialize<AstNode>(astJson, _jsonOptions);
        }
        catch { /* AST parse failure is non-fatal; file scanned by regex rules */ }
        return new PythonFile(filePath, content, ast);
    }
}
