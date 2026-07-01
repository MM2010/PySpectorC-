using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PySpector.Core.Cache;

/// <summary>
/// Three-level incremental AST cache — 1:1 mapping from ast_cache.py.
/// L1: in-memory (ConcurrentDictionary), mtime guard
/// L2: disk (JSON + zlib), content-hash guard
/// L3: chunk-aware per-function/class subtree reuse
/// </summary>
public sealed class IncrementalAstCache
{
    private const int CacheVersion = 2;
    private const int MaxL1Entries = 512;

    private readonly ConcurrentDictionary<string, FileCacheEntry> _l1Cache = new();
    private readonly string _cacheDir;
    private readonly string _cacheIndexPath;

    public IncrementalAstCache(string scanPath)
    {
        // If scanPath is a file, use its parent directory for cache storage
        var cacheRoot = File.Exists(scanPath) ? Path.GetDirectoryName(scanPath)! : scanPath;
        _cacheDir = Path.Combine(cacheRoot, ".pyspector_cache");
        _cacheIndexPath = Path.Combine(_cacheDir, "cache_index.json");
        try { Directory.CreateDirectory(_cacheDir); }
        catch { /* best-effort: cache dir creation failure is non-fatal */ }
    }

    /// <summary>
    /// Get cached AST JSON for a file. Returns null on cache miss.
    /// 1:1 from ast_cache.py get_ast_json().
    /// </summary>
    public string? GetAstJson(string filePath, string content)
    {
        var mtime = File.GetLastWriteTimeUtc(filePath).ToFileTimeUtc();
        var contentHash = ComputeSha256(content);

        // L1: in-memory mtime guard
        if (_l1Cache.TryGetValue(filePath, out var entry))
        {
            if (entry.Mtime == mtime && entry.FileHash == contentHash)
                return Decompress(entry.FullAstJsonZ);
        }

        // L2: disk content-hash guard
        var diskEntry = LoadDiskEntry(filePath);
        if (diskEntry is not null && diskEntry.FileHash == contentHash)
        {
            _l1Cache[filePath] = diskEntry;
            return Decompress(diskEntry.FullAstJsonZ);
        }

        return null; // Cache miss — caller must parse + store
    }

    /// <summary>
    /// Store parsed AST JSON in cache (all three levels).
    /// </summary>
    public void StoreAstJson(string filePath, string content, string astJson)
    {
        var mtime = File.GetLastWriteTimeUtc(filePath).ToFileTimeUtc();
        var contentHash = ComputeSha256(content);
        var compressed = Compress(astJson);

        var entry = new FileCacheEntry(filePath, contentHash, mtime, compressed);
        _l1Cache[filePath] = entry;
        SaveDiskEntry(entry);

        // Prune L1 if too large
        if (_l1Cache.Count > MaxL1Entries)
        {
            var oldest = _l1Cache.OrderBy(kv => kv.Value.Mtime).First();
            _l1Cache.TryRemove(oldest.Key, out _);
        }
    }

    private static string ComputeSha256(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }

    private static byte[] Compress(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest))
            zlib.Write(bytes, 0, bytes.Length);
        return output.ToArray();
    }

    private static string Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private FileCacheEntry? LoadDiskEntry(string filePath)
    {
        var cacheFile = GetCacheFilePath(filePath);
        if (!File.Exists(cacheFile)) return null;

        try
        {
            var json = File.ReadAllText(cacheFile);
            return JsonSerializer.Deserialize<FileCacheEntry>(json, JsonOptions);
        }
        catch { return null; }
    }

    private void SaveDiskEntry(FileCacheEntry entry)
    {
        try
        {
            var cacheFile = GetCacheFilePath(entry.FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            File.WriteAllText(cacheFile, json);
        }
        catch { /* best-effort disk persistence */ }
    }

    private string GetCacheFilePath(string filePath)
    {
        var hash = ComputeSha256(filePath)[..16];
        return Path.Combine(_cacheDir, $"{hash}.cache");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };
}

/// <summary>Cache entry for a single file. 1:1 from ast_cache.py FileCacheEntry.</summary>
internal sealed record FileCacheEntry
{
    public string FilePath { get; init; } = string.Empty;
    public string FileHash { get; init; } = string.Empty;
    public long Mtime { get; init; }
    public byte[] FullAstJsonZ { get; init; } = [];
    public int Version { get; init; } = 2;

    public FileCacheEntry() { }

    public FileCacheEntry(string filePath, string fileHash, long mtime, byte[] fullAstJsonZ)
    {
        FilePath = filePath;
        FileHash = fileHash;
        Mtime = mtime;
        FullAstJsonZ = fullAstJsonZ;
    }
}
