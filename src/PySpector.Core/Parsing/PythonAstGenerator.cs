using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace PySpector.Core.Parsing;

/// <summary>
/// Generates real Python AST JSON by invoking Python's ast module.
/// Uses batch mode: all files are sent in a single invocation via temp file,
/// read back from stdout. Falls back to empty AST if Python is unavailable.
/// </summary>
public static class PythonAstGenerator
{
    private static string? _pythonPath;
    private static bool _probed;
    private static readonly object _lock = new();

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static bool IsAvailable
    {
        get
        {
            if (!_probed) { lock (_lock) { if (!_probed) { _pythonPath = ProbePython(); _probed = true; } } }
            return _pythonPath is not null;
        }
    }

    /// <summary>Batch-generate AST JSON for multiple files via one Python invocation.</summary>
    public static IReadOnlyDictionary<string, string> GenerateBatch(
        IEnumerable<(string SourceCode, string FilePath)> files)
    {
        var results = new Dictionary<string, string>();
        var python = _pythonPath;
        if (python is null) return results;

        var batch = files.ToList();
        if (batch.Count == 0) return results;

        var tmpIn = Path.GetTempFileName();
        var tmpOut = Path.GetTempFileName();
        var tmpScript = Path.GetTempFileName() + ".py";

        try
        {
            // Write Python batch script — mirrors _ast_encode.py AstEncoder exactly
            File.WriteAllText(tmpScript, """
import ast, json, sys

class AstEncoder(json.JSONEncoder):
    def default(self, node):
        if isinstance(node, ast.AST):
            out = {
                "node_type": node.__class__.__name__,
                "lineno": getattr(node, "lineno", -1),
                "col_offset": getattr(node, "col_offset", -1),
            }
            child_nodes = {}
            simple_fields = {}
            for fname, value in ast.iter_fields(node):
                if type(value) is list:
                    if value and all(isinstance(n, ast.AST) for n in value):
                        child_nodes[fname] = value
                    else:
                        simple_fields[fname] = str(value) if value else []
                elif isinstance(value, ast.AST):
                    child_nodes[fname] = [value]
                else:
                    if isinstance(value, bytes):
                        simple_fields[fname] = value.decode("utf-8", errors="replace")
                    elif isinstance(value, int) and value.bit_length() > 14000:
                        simple_fields[fname] = 0
                    elif isinstance(value, (int, float, str, bool)) or value is None:
                        simple_fields[fname] = value
                    else:
                        simple_fields[fname] = str(value)
            out["children"] = child_nodes
            out["fields"] = simple_fields
            return out
        if isinstance(node, bytes):
            return node.decode("utf-8", errors="replace")
        return super().default(node)

with open(sys.argv[1], encoding='utf-8') as f:
    items = json.load(f)

results = []
for it in items:
    try:
        t = ast.parse(it['code'])
        results.append({
            'id': it['id'],
            'ok': True,
            'ast': json.dumps(t, cls=AstEncoder)
        })
    except Exception as e:
        results.append({'id': it['id'], 'ok': False, 'err': str(e)})

with open(sys.argv[2], 'w', encoding='utf-8') as f:
    json.dump(results, f)
""".Replace("\r\n", "\n"));

            // Write input JSON
            var items = batch.Select((f, i) => new { id = i, code = f.SourceCode }).ToList();
            File.WriteAllText(tmpIn, JsonSerializer.Serialize(items));

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"-u \"{tmpScript}\" \"{tmpIn}\" \"{tmpOut}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null) return results;

            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                return results;
            }

            if (process.ExitCode != 0 || !File.Exists(tmpOut))
                return results;

            // Parse results
            var output = File.ReadAllText(tmpOut);
            using var doc = JsonDocument.Parse(output);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                    continue;
                if (!element.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var idx))
                    continue;
                if (!element.TryGetProperty("ast", out var astEl) || astEl.GetString() is not { Length: > 2 } astStr)
                    continue;
                if (idx >= 0 && idx < batch.Count)
                    results[batch[idx].FilePath] = astStr;
            }
        }
        catch
        {
            // Python unavailable or failed — caller falls back to "{}"
        }
        finally
        {
            TryDelete(tmpIn);
            TryDelete(tmpOut);
            TryDelete(tmpScript);
        }

        return results;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>Generate AST JSON for a single Python file.</summary>
    public static string? Generate(string sourceCode, string filePath)
    {
        var results = GenerateBatch(new[] { (sourceCode, filePath) });
        return results.TryGetValue(filePath, out var ast) ? ast : null;
    }

    private static string? ProbePython()
    {
        foreach (var candidate in new[] { "python3", "python", "python3.14", "python3.12", "python3.11" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (process is not null)
                {
                    process.WaitForExit(2000);
                    if (process.ExitCode == 0)
                        return candidate;
                }
            }
            catch { }
        }
        return null;
    }
}
