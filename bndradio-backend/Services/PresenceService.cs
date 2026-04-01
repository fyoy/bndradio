// Tracks online listeners, their votes, and skip-permission state.
// All state is in-memory (ConcurrentDictionary) — resets on restart.
// Sessions are considered active if they pinged within the last 30 seconds.
using System.Collections.Concurrent;

namespace BndRadio.Services;

public class PresenceService
{
    private const int TimeoutSeconds = 30;       // inactivity threshold for presence

    // sessionId → (lastSeen, username, color)
    private readonly ConcurrentDictionary<string, (DateTime LastSeen, string Username)> _sessions = new();

    // sessionId → voted songId (one vote per session)
    private readonly ConcurrentDictionary<string, Guid> _votes = new();

    // sessionIds that have been granted skip permission by an admin
    private readonly ConcurrentDictionary<string, bool> _skipGranted = new();

    // sessionId → username for pending skip requests (user asked admin to allow skip)
    private readonly ConcurrentDictionary<string, string> _skipRequests = new();

    private readonly Guid _currentSongId = Guid.Empty;

    // Updates or creates a session entry with the current timestamp.
    public void Ping(string sessionId, string? username = null)
    {
        _sessions.AddOrUpdate(
            sessionId,
            _ => (DateTime.UtcNow, username ?? sessionId[..8]),
            (_, old) => (DateTime.UtcNow, username ?? old.Username));
    }

    // ── Announcements ────────────────────────────────────────────────────────
    private record AnnounceEntry(string Text, DateTime At);
    private readonly ConcurrentQueue<AnnounceEntry> _announces = new();

    // Stores a broadcast announcement (capped at 20 entries).
    public void Announce(string text)
    {
        _announces.Enqueue(new AnnounceEntry(text, DateTime.UtcNow));
        while (_announces.Count > 20) _announces.TryDequeue(out _);
    }

    public IReadOnlyList<string> GetAnnouncesSince(DateTime since) =>
        _announces.Where(a => a.At > since).Select(a => a.Text).ToList();

    // ── Active users ─────────────────────────────────────────────────────────
    public int GetActiveCount()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-TimeoutSeconds);
        return _sessions.Count(kv => kv.Value.LastSeen >= cutoff);
    }

    public IReadOnlyList<string> GetActiveUsernames()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-TimeoutSeconds);
        return [.. _sessions
            .Where(kv => kv.Value.LastSeen >= cutoff)
            .Select(kv => kv.Value.Username)
            .OrderBy(n => n)];
    }

    public IReadOnlyList<string> GetActiveUsers()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-TimeoutSeconds);
        return [.. _sessions
            .Where(kv => kv.Value.LastSeen >= cutoff)
            .Select(kv => kv.Value.Username)];
    }

    // Returns the display name for a session, or null if the session is unknown.
    public string? GetUsername(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var s) ? s.Username : null;

    // ── Song votes ───────────────────────────────────────────────────────────
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

    // ── Skip permission ──────────────────────────────────────────────────────

    // Admin grants a specific session the right to skip once.
    // Also removes any pending skip request from that session.
    public void GrantSkip(string sessionId)
    {
        _skipGranted[sessionId] = true;
        _skipRequests.TryRemove(sessionId, out _);
    }

    // Atomically checks and removes the grant — returns true if the session had one.
    public bool ConsumeSkipGrant(string sessionId) => _skipGranted.TryRemove(sessionId, out _);

    public bool HasSkipGrant(string sessionId) => _skipGranted.ContainsKey(sessionId);

    // User requests permission to skip from the admin.
    public void RequestSkip(string sessionId, string username) =>
        _skipRequests[sessionId] = username;

    public void CancelSkipRequest(string sessionId) => _skipRequests.TryRemove(sessionId, out _);

    public IReadOnlyList<(string SessionId, string Username)> GetSkipRequests() =>
        _skipRequests.Select(kv => (kv.Key, kv.Value)).ToList();
}
