// Repository abstraction — all persistence goes through MinIO.
using BndRadio.Domain;

namespace BndRadio.Interfaces;

public interface ISongRepository
{
    Task<IReadOnlyList<Song>> GetAllAsync();
    Task<Song?> GetByIdAsync(Guid id);
    Task<Stream> OpenAudioStreamAsync(Guid id);
    Task<Song> AddAsync(string title, string artist, int durationMs, Stream audioData);
    Task IncrementPlayCountAsync(Guid id);
    Task DeleteAsync(Guid id);
    Task EnsureSchemaAsync();       // creates the MinIO bucket if absent
    Task<bool> ExistsByHashAsync(string fileHash);
}
