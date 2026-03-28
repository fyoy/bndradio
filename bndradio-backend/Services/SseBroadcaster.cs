using BndRadio.Interfaces;

namespace BndRadio.Services;

public class SseBroadcaster : BackgroundService
{
    private readonly SseHub _hub;
    private readonly IQueueManager _queue;
    private readonly IStreamServer _stream;
    private readonly PresenceService _presence;
    private readonly RedisCacheService _cache;
    private readonly ILogger<SseBroadcaster> _logger;

    private string _lastStateHash = "";
    private string _lastPresenceHash = "";
    private string _lastSkipRequestsHash = "";

    public SseBroadcaster(
        SseHub hub,
        IQueueManager queue,
        IStreamServer stream,
        PresenceService presence,
        RedisCacheService cache,
        ILogger<SseBroadcaster> logger)
    {
        _hub = hub;
        _queue = queue;
        _stream = stream;
        _presence = presence;
        _cache = cache;
        _logger = logger;
    }

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
        var broadcastState = _stream.GetBroadcastState();
        var current = broadcastState.CurrentSong;
        var next = _queue.NextSong;
        var skipVotes = _presence.GetSkipVoteCount(current.Id);

        var hash = $"{current.Id}|{next?.Id}|{skipVotes}";
        if (hash == _lastStateHash) return;
        _lastStateHash = hash;

        var payload = new
        {
            current = new { id = current.Id, title = current.Title, durationMs = current.DurationMs },
            next    = next == null ? null : new { id = next.Id, title = next.Title },
            elapsedMs  = (long)broadcastState.ElapsedInCurrentSong.TotalMilliseconds,
            skipVotes,
            skipNeeded = PresenceService.SkipThreshold,
        };

        await _cache.SetAsync("queue:state", payload, TimeSpan.FromSeconds(2));
        await _hub.BroadcastAsync("state", payload);
    }

    private async Task BroadcastSkipRequestsIfChangedAsync()
    {
        var requests = _presence.GetSkipRequests();
        var hash = string.Join("|", requests.Select(r => r.SessionId));
        if (hash == _lastSkipRequestsHash) return;
        _lastSkipRequestsHash = hash;

        var payload = new { requests = requests.Select(r => new { sessionId = r.SessionId, username = r.Username }) };
        await _hub.BroadcastAsync("skip_requests", payload);
    }


    private async Task BroadcastPresenceIfChangedAsync()
    {
        var users = _presence.GetActiveUsers();
        var hash = string.Join("|", users.Select(u => $"{u.Username}:{u.Color}"));
        if (hash == _lastPresenceHash) return;
        _lastPresenceHash = hash;

        var payload = new { count = users.Count, users = users.Select(u => new { username = u.Username, color = u.Color }) };
        await _hub.BroadcastAsync("presence", payload);
    }
}