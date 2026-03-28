using BndRadio.Domain;
using BndRadio.Interfaces;

namespace BndRadio.Tests;

/// <summary>
/// No-op ISongRepository stub for tests that don't need a real database.
/// All methods return empty/default results without connecting to any external service.
/// </summary>
public class NoOpSongRepository : ISongRepository
{
    public Task EnsureSchemaAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<Song>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<Song>>(Array.Empty<Song>());

    public Task<Song?> GetByIdAsync(Guid id) =>
        Task.FromResult<Song?>(null);

    public Task<Stream> OpenAudioStreamAsync(Guid id) =>
        throw new KeyNotFoundException($"Song {id} not found");

    public Task<Song> AddAsync(string title, string artist, int durationMs, Stream audioData) =>
        throw new InvalidOperationException("NoOpSongRepository does not support AddAsync");

    public Task IncrementPlayCountAsync(Guid id) => Task.CompletedTask;

    public Task DeleteAsync(Guid id) => Task.CompletedTask;

    public Task<bool> ExistsByHashAsync(string fileHash) =>
        Task.FromResult(false);
}
