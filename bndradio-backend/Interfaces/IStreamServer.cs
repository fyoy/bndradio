// Abstraction for the audio broadcast server.
// Consumers subscribe via AddListener and receive raw MP3 byte chunks.
using System.Threading.Channels;
using BndRadio.Domain;

namespace BndRadio.Interfaces;

// Snapshot of what the stream server is currently doing.
public record BroadcastState(
    Song CurrentSong,
    Song NextSong,
    TimeSpan ElapsedInCurrentSong  // how far into the current track we are
);

public interface IStreamServer
{
    // Returns a channel reader that receives audio chunks as they are broadcast.
    // The channel is completed when the listener disconnects.
    ChannelReader<byte[]> AddListener(CancellationToken ct);

    // Returns a point-in-time snapshot of the current broadcast state.
    BroadcastState GetBroadcastState();

    // Cancels the current song's playback, causing the loop to advance immediately.
    void SkipCurrent();
}
