// Background service that polls state every 800 ms and pushes SSE events
// only when something has actually changed (hash-based diffing).
// Broadcasts three event types: "state", "presence", "skip_requests".
using BndRadio.Interfaces;

namespace BndRadio.Services;

public class SseBroadcaster(SseHub hub, IQueueManager queue, IStreamServer stream, PresenceService presence, ILogger<SseBroadcaster> logger) : BackgroundService
{
    private readonly SseHub _hub = hub;
    private readonly IQueueManager _queue = queue;
    private readonly IStreamServer _stream = stream;
    private readonly PresenceService _presence = presence;
    private readonly ILogger<SseBroadcaster> _logger = logger;

    private string _lastStateHash = "";
    private string _lastPresenceHash = "";
    private string _lastSkipRequestsHash = "";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(800, ct);
                if (_hub.ClientCount == 0) continue;
                await BroadcastStateIfChangedAsync();
                await BroadcastPresenceIfChangedAsync();
                await BroadcastSkipRequestsIfChangedAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "SseBroadcaster tick error"); }
        }
    }

    private async Task BroadcastStateIfChangedAsync()
    {
        var state = _stream.GetBroadcastState();
        var current = state.CurrentSong;
        var next = _queue.NextSong;

        var hash = $"{current.Id}|{next?.Id}";
        if (hash == _lastStateHash) return;
        _lastStateHash = hash;

        await _hub.BroadcastAsync("state", new
        {
            current    = new { id = current.Id, title = current.Title, durationMs = current.DurationMs },
            next       = next == null ? null : new { id = next.Id, title = next.Title },
            elapsedMs  = (long)state.ElapsedInCurrentSong.TotalMilliseconds,
        });
    }

    private async Task BroadcastPresenceIfChangedAsync()
    {
        var users = _presence.GetActiveUsers();
        var hash = string.Join("|", users.Select(u => u));
        if (hash == _lastPresenceHash) return;
        _lastPresenceHash = hash;

        await _hub.BroadcastAsync("presence", new
        {
            count = users.Count,
            users = users.Select(u => new { username = u }),
        });
    }

    private async Task BroadcastSkipRequestsIfChangedAsync()
    {
        var requests = _presence.GetSkipRequests();
        var hash = string.Join("|", requests.Select(r => r.SessionId));
        if (hash == _lastSkipRequestsHash) return;
        _lastSkipRequestsHash = hash;

        await _hub.BroadcastAsync("skip_requests", new
        {
            requests = requests.Select(r => new { sessionId = r.SessionId, username = r.Username }),
        });
    }
}
