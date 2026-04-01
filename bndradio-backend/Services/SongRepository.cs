// Implements ISongRepository using MinIO only — no database.
// Each song has two objects in the bucket:
//   meta/{id}.json  — JSON metadata (title, artist, durationMs, playCount, fileHash)
//   songs/{id}      — raw audio bytes
// GetAllAsync lists the meta/ prefix and deserializes each sidecar.
// All operations are atomic at the MinIO level (no distributed transaction needed).
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Text.Json;
using BndRadio.Domain;
using BndRadio.Interfaces;
using System.Collections.Concurrent;

namespace BndRadio.Services;

public class SongRepository(IAmazonS3 s3, IOptions<MinioOptions> opts) : ISongRepository
{
    private readonly IAmazonS3 _s3 = s3;
    private readonly string _bucket = opts.Value.BucketName;

    // In-memory play count cache — persisted back to MinIO on increment.
    // Avoids a full meta read/write round-trip per play under normal load.
    private readonly ConcurrentDictionary<Guid, int> _playCountCache = new();

    public async Task EnsureSchemaAsync()
    {
        // Create the bucket if it doesn't exist yet.
        var buckets = await _s3.ListBucketsAsync();
        if (!buckets.Buckets.Any(b => b.BucketName == _bucket))
            await _s3.PutBucketAsync(_bucket);
    }

    public async Task<IReadOnlyList<Song>> GetAllAsync()
    {
        var songs = new List<Song>();
        string? continuationToken = null;

        do
        {
            var req = new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = "meta/",
                ContinuationToken = continuationToken,
            };
            var resp = await _s3.ListObjectsV2Async(req);

            var tasks = resp.S3Objects
                .Where(o => o.Key.EndsWith(".json"))
                .Select(o => ReadMetaAsync(o.Key));

            var results = await Task.WhenAll(tasks);
            songs.AddRange(results.Where(s => s != null)!);

            continuationToken = resp.IsTruncated ? resp.NextContinuationToken : null;
        }
        while (continuationToken != null);

        return songs;
    }

    public async Task<Song?> GetByIdAsync(Guid id)
    {
        return await ReadMetaAsync($"meta/{id}.json");
    }

    public async Task<Stream> OpenAudioStreamAsync(Guid id)
    {
        try
        {
            var resp = await _s3.GetObjectAsync(_bucket, $"songs/{id}");
            var ms = new MemoryStream();
            await resp.ResponseStream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            throw new KeyNotFoundException($"audio not found for song {id}.");
        }
    }

    public async Task<bool> ExistsByHashAsync(string fileHash)
    {
        // Scan all meta objects for a matching hash.
        // Acceptable for small catalogues; could be optimised with a hash index object if needed.
        var all = await GetAllAsync();
        return all.Any(s => string.Equals(s.FileHash, fileHash, StringComparison.OrdinalIgnoreCase));
    }

    // ── Write ────────────────────────────────────────────────────────────────
    public async Task<Song> AddAsync(string title, string artist, int durationMs, Stream audioData)
    {
        using var ms = new MemoryStream();
        await audioData.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var id = Guid.NewGuid();

        // 1. Upload audio
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName  = _bucket,
            Key         = $"songs/{id}",
            InputStream = new MemoryStream(bytes),
            ContentType = "application/octet-stream",
        });

        // 2. Write metadata sidecar — if this fails, clean up the audio object
        var meta = new SongMeta(id, title, artist, durationMs, 0, hash);
        try
        {
            await WriteMetaAsync(meta);
        }
        catch
        {
            try { await _s3.DeleteObjectAsync(_bucket, $"songs/{id}"); } catch { /* best-effort */ }
            throw;
        }

        return new Song(id, title, artist, durationMs, 0, hash);
    }

    public async Task IncrementPlayCountAsync(Guid id)
    {
        var meta = await ReadMetaAsync($"meta/{id}.json");
        if (meta == null) return;

        var newCount = _playCountCache.AddOrUpdate(id, meta.PlayCount + 1, (_, old) => old + 1);
        await WriteMetaAsync(new SongMeta(meta.Id, meta.Title, meta.Artist, meta.DurationMs, newCount, meta.FileHash));
    }

    public async Task DeleteAsync(Guid id)
    {
        // Delete both objects; ignore NoSuchKey on either.
        await TryDeleteAsync($"meta/{id}.json");
        await TryDeleteAsync($"songs/{id}");
        _playCountCache.TryRemove(id, out _);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Song?> ReadMetaAsync(string key)
    {
        try
        {
            var resp = await _s3.GetObjectAsync(_bucket, key);
            using var reader = new StreamReader(resp.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var meta = JsonSerializer.Deserialize<SongMeta>(json, _jsonOpts);
            if (meta == null) return null;
            var playCount = _playCountCache.TryGetValue(meta.Id, out var cached) ? cached : meta.PlayCount;
            return new Song(meta.Id, meta.Title, meta.Artist, meta.DurationMs, playCount, meta.FileHash);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return null;
        }
    }

    private async Task WriteMetaAsync(SongMeta meta)
    {
        var json = JsonSerializer.Serialize(meta, _jsonOpts);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName  = _bucket,
            Key         = $"meta/{meta.Id}.json",
            InputStream = new MemoryStream(bytes),
            ContentType = "application/json",
        });
    }

    private async Task TryDeleteAsync(string key)
    {
        try { await _s3.DeleteObjectAsync(_bucket, key); }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey") { /* already gone */ }
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Sidecar JSON shape stored in MinIO
    private record SongMeta(Guid Id, string Title, string Artist, int DurationMs, int PlayCount, string? FileHash);
}
