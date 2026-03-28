using Amazon.S3;
using Amazon.S3.Model;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using BndRadio.Domain;
using BndRadio.Interfaces;

namespace BndRadio.Services;

public class SongRepository : ISongRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;

    public SongRepository(
        NpgsqlDataSource dataSource,
        IAmazonS3 s3,
        IOptions<MinioOptions> minioOptions)
    {
        _dataSource = dataSource;
        _s3 = s3;
        _bucketName = minioOptions.Value.BucketName;
    }

    public async Task<IReadOnlyList<Song>> GetAllAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<Song>(
            "SELECT id AS Id, title AS Title, artist AS Artist, duration_ms AS DurationMs, play_count AS PlayCount FROM songs");
        return rows.ToList();
    }

    public async Task<Song?> GetByIdAsync(Guid id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Song>(
            "SELECT id AS Id, title AS Title, artist AS Artist, duration_ms AS DurationMs, play_count AS PlayCount FROM songs WHERE id = @id",
            new { id });
    }

    public async Task<Stream> OpenAudioStreamAsync(Guid id)
    {
        var key = $"songs/{id}";
        try
        {
            var response = await _s3.GetObjectAsync(_bucketName, key);
            var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            throw new KeyNotFoundException($"Audio object not found for song {id}.");
        }
    }

    public async Task<bool> ExistsByHashAsync(string fileHash)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM songs WHERE file_hash = @fileHash",
            new { fileHash });
        return count > 0;
    }

    public async Task<Song> AddAsync(string title, string artist, int durationMs, Stream audioData)
    {
        using var ms = new MemoryStream();
        await audioData.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var id = Guid.NewGuid();
        var key = $"songs/{id}";

        // Upload to MinIO first — if this throws, no DB row is inserted (req 3.3)
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = new MemoryStream(bytes),
            ContentType = "application/octet-stream",
        });

        // Insert metadata — if this throws, roll back the MinIO object (req 3.4)
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO songs (id, title, artist, duration_ms, file_hash) " +
                "VALUES (@id, @title, @artist, @durationMs, @fileHash)";
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("title", title);
            cmd.Parameters.AddWithValue("artist", artist);
            cmd.Parameters.AddWithValue("durationMs", durationMs);
            cmd.Parameters.AddWithValue("fileHash", hash);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Rollback: delete the MinIO object
            try { await _s3.DeleteObjectAsync(_bucketName, key); } catch { /* best-effort */ }
            throw;
        }

        return new Song(id, title, artist, durationMs);
    }

    public async Task IncrementPlayCountAsync(Guid id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE songs SET play_count = play_count + 1 WHERE id = @id",
            new { id });
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM songs WHERE id = @id", new { id });

        var key = $"songs/{id}";
        try
        {
            await _s3.DeleteObjectAsync(_bucketName, key);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            // Silently ignore — idempotent delete (req 5.3)
        }
    }

    public async Task EnsureSchemaAsync()
    {
        var bucketsResponse = await _s3.ListBucketsAsync();
        if (!bucketsResponse.Buckets.Any(b => b.BucketName == _bucketName))
        {
            await _s3.PutBucketAsync(_bucketName);
        }

        await using var conn = await _dataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS songs (
                id          UUID    PRIMARY KEY DEFAULT gen_random_uuid(),
                title       TEXT    NOT NULL,
                artist      TEXT    NOT NULL,
                duration_ms INTEGER NOT NULL,
                play_count  INTEGER NOT NULL DEFAULT 0,
                file_hash   TEXT    NULL
            )
            """);
        await conn.ExecuteAsync(
            "ALTER TABLE songs ADD COLUMN IF NOT EXISTS play_count INTEGER NOT NULL DEFAULT 0");
        await conn.ExecuteAsync(
            "ALTER TABLE songs ADD COLUMN IF NOT EXISTS file_hash TEXT NULL");
        await conn.ExecuteAsync(
            "ALTER TABLE songs DROP COLUMN IF EXISTS audio_data");
    }
}
