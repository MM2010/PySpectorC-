using System.Text.Json;
using PySpector.Core;
using PySpector.Core.Models;

namespace PySpector.Web;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        });

        var app = builder.Build();
        app.UseCors();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", engine = "csharp", timestamp = DateTime.UtcNow }));
        app.MapPost("/scan", HandleScan);

        await app.RunAsync();
    }

    private static async Task<IResult> HandleScan(ScanRequest req)
    {
        if (req.Path is null && req.Url is null)
            return Results.BadRequest(new { error = "Either 'path' or 'url' must be provided." });

        try
        {
            var targetPath = req.Path ?? await CloneRepository(req.Url!);
            var rulesToml = Core.Services.ConfigService.GetDefaultRules(req.Ai);
            var config = new ScanConfig { Exclude = Core.Services.ConfigService.DefaultExclusions };

            var pyFiles = new List<PythonFile>();
            foreach (var file in Directory.EnumerateFiles(targetPath, "*.py", SearchOption.AllDirectories))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    pyFiles.Add(new PythonFile(file, content));
                }
                catch { /* skip unreadable */ }
            }

            var engine = EngineFactory.Create();
            var issues = engine.RunScan(targetPath, rulesToml, config, pyFiles);

            return req.JsonOutput
                ? Results.Json(issues)
                : Results.Text(string.Join("\n", issues.Select(i =>
                    $"[{i.Severity}] {i.RuleId} {i.FilePath}:{i.LineNumber} — {i.Description}")));
        }
        catch (Exception ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: 500);
        }
    }

    private static async Task<string> CloneRepository(string url)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tempPath = $"/tmp/pyspector_scan_{timestamp}";
        var psi = new System.Diagnostics.ProcessStartInfo("git", $"clone --depth 1 {url} {tempPath}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git clone failed: {await process.StandardError.ReadToEndAsync()}");
        return tempPath;
    }
}

public sealed record ScanRequest(string? Path, string? Url, bool Ai = false, bool JsonOutput = false);
