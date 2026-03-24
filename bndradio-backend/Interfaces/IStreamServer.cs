using System.Threading.Channels;
using BndRadio.Domain;

namespace BndRadio.Interfaces;

public record BroadcastState(
    Song CurrentSong,
    Song NextSong,
    TimeSpan ElapsedInCurrentSong
);

public interface IStreamServer
{
    ChannelReader<byte[]> AddListener(CancellationToken ct);
    BroadcastState GetBroadcastState();
    void SkipCurrent();
}
