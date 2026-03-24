using BndRadio.Domain;

namespace BndRadio.Interfaces;

public interface IQueueManager
{
    Song? CurrentSong { get; }
    Song? NextSong { get; }
    void Advance();
    Task<bool?> SuggestAsync(Guid songId, string? sessionId = null);
    Task ReloadAsync();
    IReadOnlyList<(Song Song, int QueuePosition)> GetQueueList();
    int GetVoteCount(Guid songId);
    int GetVoteCooldownSeconds(Guid songId);
    Task UnvoteAsync(Guid songId, string sessionId);
}
