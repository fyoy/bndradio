// Manages the playback queue: a sliding window of upcoming songs,
// vote-based ordering, and play-history tracking.
using BndRadio.Domain;

namespace BndRadio.Interfaces;

public interface IQueueManager
{
    // The song currently being broadcast (first in the window).
    Song? CurrentSong { get; }

    // The next song to be played, ordered by vote count then first-voted time.
    Song? NextSong { get; }

    // Removes the current song from the window and refills from the catalogue.
    void Advance();

    // Casts a vote for a song, moving it into the window if not already there.
    // Returns false if the song doesn't exist, null if it's on cooldown.
    Task<bool?> SuggestAsync(Guid songId, string? sessionId = null);

    // Reloads the full catalogue from the repository and refills the window.
    Task ReloadAsync();

    // Returns all songs in the window with their current queue positions.
    IReadOnlyList<(Song Song, int QueuePosition)> GetQueueList();

    // Returns the current vote count for a song.
    int GetVoteCount(Guid songId);

    // Removes a session's vote for a song.
    Task UnvoteAsync(Guid songId, string sessionId);
}
