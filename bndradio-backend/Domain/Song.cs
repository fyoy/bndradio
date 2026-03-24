namespace BndRadio.Domain;

public record Song(
    Guid Id,
    string Title,
    string Artist,
    int DurationMs,
    int PlayCount = 0
);
