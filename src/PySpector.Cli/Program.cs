using PySpector.Core;
using PySpector.Core.Cache;
using PySpector.Core.Models;
using PySpector.Core.Parsing;
using PySpector.Reporting;

namespace PySpector.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        var scanPath = args[0];
        var format = GetArg(args, "--format", "-f")
                  ?? (HasFlag(args, "--json") ? "json" : null)
                  ?? "console";
        var severity = GetArg(args, "--severity", "-s") ?? "LOW";
        var ai = HasFlag(args, "--ai");
        var noAst = HasFlag(args, "--no-ast");
        var outputFile = GetArg(args, "--output", "-o");
        var debug = HasFlag(args, "--debug");

        await ExecuteScan(scanPath, format, severity, ai, noAst, outputFile, debug);
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        PySpector -- .NET 10 SAST Framework

        Usage: pyspector <PATH> [OPTIONS]

        Arguments:
          <PATH>              File or directory to scan

        Options:
          -f, --format FORMAT Output format: console (default), json, sarif, html
          --json              Shorthand for --format json
          -s, --severity LVL  Minimum severity: LOW, MEDIUM, HIGH, CRITICAL
          --no-ast            Skip Python AST generation (regex-only, much faster)
          --ai                Enable AI/LLM vulnerability rules
          -o, --output FILE   Write report to file instead of stdout
          --debug             Show debug/progress messages
          -h, --help          Show this help message
        """);
    }

    private static string? GetArg(string[] args, string longName, string shortName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == longName || args[i] == shortName)
                return i + 1 < args.Length ? args[i + 1] : null;
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag)
        => Array.IndexOf(args, flag) >= 0;

    private static async Task ExecuteScan(
        string scanPath, string format, string severity, bool ai, bool noAst,
        string? outputFile, bool debug)
    {
        try
        {
            if (!debug) Console.WriteLine("PySpector\n");

            var rulesToml = PySpector.Core.Services.ConfigService.GetDefaultRules(ai);

            var config = new ScanConfig
            {
                Exclude = PySpector.Core.Services.ConfigService.DefaultExclusions,
                Severity = severity,
            };

            var rootPath = Path.GetFullPath(scanPath);
            var cache = new IncrementalAstCache(rootPath);
            var pyFiles = new List<PythonFile>();

            var isDir = Directory.Exists(rootPath);
            var searchRoot = isDir ? rootPath : Path.GetDirectoryName(rootPath) ?? rootPath;
            var allPyFiles = isDir
                ? Directory.EnumerateFiles(searchRoot, "*.py", SearchOption.AllDirectories)
                : new[] { rootPath };

            // Phase 1: Collect all Python files (fast, no AST yet)
            var filesToProcess = new List<(string Path, string Content, string RelPath)>();

            foreach (var filePath in allPyFiles)
            {
                // Skip excluded paths early — avoid reading node_modules etc.
                var relPath = filePath.Replace('\\', '/');
                if (relPath.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
                    relPath.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
                    relPath.Contains("/.venv/", StringComparison.OrdinalIgnoreCase) ||
                    relPath.Contains("/venv/", StringComparison.OrdinalIgnoreCase) ||
                    relPath.Contains("/__pycache__/", StringComparison.OrdinalIgnoreCase) ||
                    relPath.Contains("/.pyspector_cache/", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var content = File.ReadAllText(filePath);
                    filesToProcess.Add((filePath, content, relPath));
                }
                catch (Exception ex)
                {
                    if (debug) Console.Error.WriteLine($"  [WARN] {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            // Phase 2: Batch-generate AST via Python (skip with --no-ast for regex-only speed)
            var astResults = (!noAst && PythonAstGenerator.IsAvailable)
                ? PythonAstGenerator.GenerateBatch(filesToProcess.Select(f => (f.Content, f.Path)))
                : new Dictionary<string, string>();

            // Phase 3: Build PythonFile objects
            foreach (var (filePath, content, relPath) in filesToProcess)
            {
                string? astJson = cache.GetAstJson(filePath, content);
                if (astJson is null)
                {
                    astResults.TryGetValue(filePath, out astJson);
                    astJson ??= "{}";
                    cache.StoreAstJson(filePath, content, astJson);
                }

                pyFiles.Add(PythonFile.FromAstJson(filePath, content, astJson));
            }

            if (debug) Console.WriteLine($"[*] Parsed {pyFiles.Count} Python file(s)");

            var engine = EngineFactory.Create();
            var issues = engine.RunScan(rootPath, rulesToml, config, pyFiles);

            var sevMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { ["LOW"] = 0, ["MEDIUM"] = 1, ["HIGH"] = 2, ["CRITICAL"] = 3 };
            issues = issues.Where(i => (int)i.Severity >= sevMap.GetValueOrDefault(severity, 0)).ToList();

            IReporter reporter = format.ToLowerInvariant() switch
            {
                "json" => new JsonReporter(),
                "sarif" => new SarifReporter(),
                "html" => new HtmlReporter(),
                _ => new ConsoleReporter(),
            };

            var report = reporter.Generate(issues);
            if (outputFile is not null) File.WriteAllText(outputFile, report);
            else Console.WriteLine(report);

            Console.WriteLine($"\n[*] Scan complete. {issues.Count} issue(s) found.");
            if (issues.Count > 0)
            {
                Console.WriteLine($"    Critical: {issues.Count(i => i.Severity == Severity.Critical)}");
                Console.WriteLine($"    High:     {issues.Count(i => i.Severity == Severity.High)}");
                Console.WriteLine($"    Medium:   {issues.Count(i => i.Severity == Severity.Medium)}");
                Console.WriteLine($"    Low:      {issues.Count(i => i.Severity == Severity.Low)}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            if (debug) Console.Error.WriteLine(ex.ToString());
        }
    }
}
