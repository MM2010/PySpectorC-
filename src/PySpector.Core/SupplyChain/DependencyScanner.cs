using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PySpector.Core.SupplyChain;

public static class DependencyScanner
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://api.osv.dev/"),
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static readonly string[] DepFilePatterns =
        ["requirements.txt", "requirements/*.txt", "pyproject.toml",
         "Pipfile", "Pipfile.lock", "poetry.lock"];

    public static async Task<IReadOnlyList<VulnerabilityMatch>> ScanDependenciesAsync(
        string projectPath, CancellationToken ct = default)
    {
        var depFiles = FindDependencyFiles(projectPath);
        var allDeps = new List<Dependency>();

        foreach (var file in depFiles)
        {
            var deps = await ParseDependencyFileAsync(file, ct);
            allDeps.AddRange(deps);
        }

        var uniqueDeps = allDeps
            .GroupBy(d => (d.Name, d.Version, d.Ecosystem))
            .Select(g => g.First())
            .ToList();

        var results = new ConcurrentBag<VulnerabilityMatch>();
        var semaphore = new SemaphoreSlim(10);
        await Parallel.ForEachAsync(uniqueDeps, ct, async (dep, token) =>
        {
            await semaphore.WaitAsync(token);
            try
            {
                var vulns = await QueryOsvAsync(dep, token);
                if (vulns.Count > 0)
                {
                    var files = allDeps
                        .Where(d => d.Name == dep.Name && d.Version == dep.Version)
                        .Select(d => d.SourceFile).Distinct().ToList();
                    results.Add(new VulnerabilityMatch(dep, vulns, files));
                }
            }
            catch { }
            finally { semaphore.Release(); }
        });

        return results.ToList();
    }

    private static List<string> FindDependencyFiles(string projectPath)
    {
        var files = new List<string>();
        foreach (var pattern in DepFilePatterns)
        {
            var fileName = Path.GetFileName(pattern);
            var dir = Path.GetDirectoryName(pattern);
            var searchDir = dir is { Length: > 0 } ? Path.Combine(projectPath, dir) : projectPath;
            if (!Directory.Exists(searchDir)) continue;
            files.AddRange(Directory.EnumerateFiles(searchDir, fileName, SearchOption.TopDirectoryOnly));
        }
        return files;
    }

    private static async Task<List<Dependency>> ParseDependencyFileAsync(
        string filePath, CancellationToken ct)
    {
        var deps = new List<Dependency>();
        var content = await File.ReadAllTextAsync(filePath, ct);
        var fileName = Path.GetFileName(filePath);

        try
        {
            if (fileName == "requirements.txt" || fileName.StartsWith("requirements-", StringComparison.Ordinal))
                deps.AddRange(ParseRequirementsTxt(content, filePath));
            else if (fileName == "pyproject.toml")
                deps.AddRange(ParsePyProjectToml(content, filePath));
            else if (fileName is "Pipfile" or "Pipfile.lock")
                deps.AddRange(ParsePipfile(content, filePath));
            else if (fileName == "poetry.lock")
                deps.AddRange(ParsePoetryLock(content, filePath));
        }
        catch { }

        return deps;
    }

    private static List<Dependency> ParseRequirementsTxt(string content, string sourceFile)
    {
        var deps = new List<Dependency>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith('-'))
                continue;
            var match = Regex.Match(trimmed,
                @"^([A-Za-z0-9_.-]+)\s*(==|>=|<=|~=|!=|>|<)\s*([A-Za-z0-9_.*+-]+)");
            if (match.Success)
                deps.Add(new Dependency(match.Groups[1].Value, match.Groups[3].Value, "PyPI", sourceFile));
        }
        return deps;
    }

    private static List<Dependency> ParsePyProjectToml(string content, string sourceFile)
    {
        var deps = new List<Dependency>();
        var inDeps = false;
        foreach (var line in content.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("[project]", StringComparison.Ordinal) || t == "dependencies = [")
                inDeps = true;
            else if (t.StartsWith('[')) inDeps = false;
            else if (inDeps && t.StartsWith('\"') && t.Contains('>'))
            {
                var cleaned = t.Trim('"', ',');
                var parts = cleaned.Split(">=", 2);
                if (parts.Length == 2)
                    deps.Add(new Dependency(parts[0].Trim(), parts[1].Trim().Trim('"'), "PyPI", sourceFile));
            }
        }
        return deps;
    }

    private static List<Dependency> ParsePipfile(string content, string sourceFile)
    {
        var deps = new List<Dependency>();
        var inPackages = false;
        foreach (var line in content.Split('\n'))
        {
            var t = line.Trim();
            if (t is "[packages]" or "[dev-packages]") inPackages = true;
            else if (t.StartsWith('[')) inPackages = false;
            else if (inPackages && t.Contains('=') && !t.StartsWith('#'))
            {
                var eqIdx = t.IndexOf('=');
                var name = t[..eqIdx].Trim().Trim('"');
                var version = t[(eqIdx + 1)..].Trim().Trim('"', '{', '}');
                if (name.Length > 0 && version.Length > 0)
                    deps.Add(new Dependency(name, version, "PyPI", sourceFile));
            }
        }
        return deps;
    }

    private static List<Dependency> ParsePoetryLock(string content, string sourceFile)
    {
        var deps = new List<Dependency>();
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "[[package]]") continue;

            string? name = null, version = null;
            for (int j = i + 1; j < lines.Length; j++)
            {
                var t = lines[j].TrimStart();
                if (t.StartsWith("[[", StringComparison.Ordinal)) break;
                if (t.StartsWith("name = ", StringComparison.Ordinal))
                    name = t.Split('"')[1];
                if (t.StartsWith("version = ", StringComparison.Ordinal))
                    version = t.Split('"')[1];
            }
            if (name is not null && version is not null)
                deps.Add(new Dependency(name, version, "PyPI", sourceFile));
        }
        return deps;
    }

    private static async Task<List<OsvVulnerability>> QueryOsvAsync(
        Dependency dep, CancellationToken ct)
    {
        var body = new { package = new { name = dep.Name, ecosystem = dep.Ecosystem }, version = dep.Version };
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync("v1/query", content, ct);
        if (!response.IsSuccessStatusCode) return [];

        var result = await response.Content.ReadFromJsonAsync<OsvResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        if (result?.Vulns is null) return [];

        return result.Vulns.Select(v =>
        {
            var scoreStr = v.Severity?.FirstOrDefault()?.Score;
            double? cvss = null;
            if (scoreStr is not null && double.TryParse(scoreStr,
                NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                cvss = parsed;

            return new OsvVulnerability(
                v.Id ?? "unknown",
                v.Summary ?? "No description",
                v.Aliases ?? [],
                cvss);
        }).ToList();
    }
}

public sealed record Dependency(string Name, string Version, string Ecosystem, string SourceFile);
public sealed record OsvVulnerability(string Id, string Summary, List<string> Aliases, double? CvssScore);
public sealed record VulnerabilityMatch(Dependency Dependency, List<OsvVulnerability> Vulnerabilities, List<string> Files);

internal sealed record OsvResponse([property: JsonPropertyName("vulns")] List<OsvVulnEntry>? Vulns);
internal sealed record OsvVulnEntry(
    string? Id, string? Summary, List<string>? Aliases,
    [property: JsonPropertyName("severity")] List<OsvSeverityEntry>? Severity);
internal sealed record OsvSeverityEntry(string? Type, string? Score);
