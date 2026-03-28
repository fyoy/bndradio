using System.Collections.Concurrent;

namespace BndRadio.Services;

public class PresenceService
{
    private const int TimeoutSeconds = 30;
    public const int SkipThreshold = 3;

    private readonly ConcurrentDictionary<string, (DateTime LastSeen, string Username, string Color)> _sessions = new();
    private readonly ConcurrentDictionary<string, Guid> _votes = new();
    private readonly ConcurrentDictionary<string, Guid> _skipVotes = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastSkipTime = new();
    // sessionIds granted skip permission by admin
    private readonly ConcurrentDictionary<string, bool> _skipGranted = new();
    // sessionIds that requested skip (username -> sessionId)
    private readonly ConcurrentDictionary<string, string> _skipRequests = new(); // sessionId -> username
    private Guid _currentSongId = Guid.Empty;

    public void Ping(string sessionId, string? username = null, string? color = null)
    {
        _sessions.AddOrUpdate(
            sessionId,
            _ => (DateTime.UtcNow, username ?? sessionId[..8], color ?? "#30d158"),
            (_, old) => (DateTime.UtcNow, username ?? old.Username, color ?? old.Color));
    }

    private record AnnounceEntry(string Text, DateTime At);
    private readonly ConcurrentQueue<AnnounceEntry> _announces = new();
    private DateTime _lastAnnounce = DateTime.MinValue;

    public void Announce(string text)
    {
        _announces.Enqueue(new AnnounceEntry(text, DateTime.UtcNow));
        _lastAnnounce = DateTime.UtcNow;
        while (_announces.Count > 20) _announces.TryDequeue(out _);
    }

    public IReadOnlyList<string> GetAnnouncesSince(DateTime since)
    {
        return _announces
            .Where(a => a.At > since)
            .Select(a => a.Text)
            .ToList();
    }

    public int GetActiveCount()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-TimeoutSeconds);
        return _sessions.Count(kv => kv.Value.LastSeen >= cutoff);
    }

    public IReadOnlyList<string> GetActiveUsernames()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-TimeoutSeconds);
        return _sessions
            .Where(kv => kv.Value.LastSeen >= cutoff)
            .Select(kv => kv.Value.Username)
            .OrderBy(n => n)
            .ToList();
    }

    public IReadOnlyList<(string Username, string Color)> GetActiveUsers()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-TimeoutSeconds);
        return _sessions
            .Where(kv => kv.Value.LastSeen >= cutoff)
            .Select(kv => (kv.Value.Username, kv.Value.Color))
            .OrderBy(u => u.Username)
            .ToList();
    }

    public string? GetUsername(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var s) ? s.Username : null;

    public Guid? GetVote(string sessionId) =>
        _votes.TryGetValue(sessionId, out var id) ? id : null;

    public bool SetVote(string sessionId, Guid songId)
    {
        if (_votes.TryGetValue(sessionId, out var existing) && existing == songId)
            return false;
        _votes[sessionId] = songId;
        return true;
    }

    public void ClearVote(string sessionId) => _votes.TryRemove(sessionId, out _);

    public int CheckSkipCooldown(string sessionId) => 0; // cooldown removed

    public void GrantSkip(string sessionId)
    {
        _skipGranted[sessionId] = true;
        _skipRequests.TryRemove(sessionId, out _);
    }

    public bool ConsumeSkipGrant(string sessionId) => _skipGranted.TryRemove(sessionId, out _);

    public bool HasSkipGrant(string sessionId) => _skipGranted.ContainsKey(sessionId);

    public void RequestSkip(string sessionId, string username)
    {
        _skipRequests[sessionId] = username;
    }

    public void CancelSkipRequest(string sessionId) => _skipRequests.TryRemove(sessionId, out _);

    public IReadOnlyList<(string SessionId, string Username)> GetSkipRequests() =>
        _skipRequests.Select(kv => (kv.Key, kv.Value)).ToList();

    public (int VoteCount, bool Triggered) VoteSkip(string sessionId, Guid songId)
    {
        if (songId != _currentSongId)
        {
            _skipVotes.Clear();
            _currentSongId = songId;
        }

        if (_skipVotes.TryGetValue(sessionId, out var existing) && existing == songId)
        {
            var currentCount = _skipVotes.Count(kv => kv.Value == songId);
            return (currentCount, false);
        }

        _skipVotes[sessionId] = songId;
        // _lastSkipTime removed

        var count = _skipVotes.Count(kv => kv.Value == songId);
        var triggered = count == SkipThreshold;
        return (count, triggered);
    }

    public int GetSkipVoteCount(Guid songId)
    {
        if (songId != _currentSongId) return 0;
        return _skipVotes.Count(kv => kv.Value == songId);
    }

    public bool HasVotedSkip(string sessionId, Guid songId) =>
        _skipVotes.TryGetValue(sessionId, out var id) && id == songId;

    public void ResetSkipVotes(Guid songId)
    {
        if (songId == _currentSongId)
            _skipVotes.Clear();
    }
}
