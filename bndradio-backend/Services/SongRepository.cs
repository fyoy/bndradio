using System.Data;
using Dapper;
using Npgsql;
using BndRadio.Domain;
using BndRadio.Interfaces;

namespace BndRadio.Services;

public class SongRepository : ISongRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public SongRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
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
        await using var conn = await _dataSource.OpenConnectionAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT audio_data FROM songs WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        if (!await reader.ReadAsync())
            throw new KeyNotFoundException($"Song {id} not found.");

        using var columnStream = reader.GetStream(0);
        var ms = new MemoryStream();
        await columnStream.CopyToAsync(ms);
        ms.Position = 0;
        return ms;
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

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO songs (title, artist, duration_ms, audio_data, file_hash) " +
            "VALUES (@title, @artist, @durationMs, @audioData, @fileHash) RETURNING id";
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("artist", artist);
        cmd.Parameters.AddWithValue("durationMs", durationMs);
        cmd.Parameters.AddWithValue("audioData", bytes);
        cmd.Parameters.AddWithValue("fileHash", hash);

        var id = (Guid)(await cmd.ExecuteScalarAsync())!;
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
    }

    public async Task EnsureSchemaAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS songs (
                id          UUID    PRIMARY KEY DEFAULT gen_random_uuid(),
                title       TEXT    NOT NULL,
                artist      TEXT    NOT NULL,
                duration_ms INTEGER NOT NULL,
                audio_data  BYTEA   NOT NULL,
                play_count  INTEGER NOT NULL DEFAULT 0,
                file_hash   TEXT    NULL
            )
            """);
        await conn.ExecuteAsync(
            "ALTER TABLE songs ADD COLUMN IF NOT EXISTS play_count INTEGER NOT NULL DEFAULT 0");
        await conn.ExecuteAsync(
            "ALTER TABLE songs ADD COLUMN IF NOT EXISTS file_hash TEXT NULL");
    }
}
