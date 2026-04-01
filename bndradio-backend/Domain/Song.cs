// Domain model — immutable record representing a song in the catalogue.
namespace BndRadio.Domain;

public record Song(
    Guid Id,
    string Title,
    string Artist,
    int DurationMs,
    int PlayCount = 0,
    string? FileHash = null
);
