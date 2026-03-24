using BndRadio.Domain;
using BndRadio.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BndRadio.Services;

public class QueueManager : IQueueManager, IHostedService
{
    private const int WindowSize = 5;

    private readonly ISongRepository _repository;
    private readonly ILogger<QueueManager> _logger;
    private readonly PresenceService _presence;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly List<Song> _window = new();
    private readonly Dictionary<Guid, (int Votes, DateTime FirstVotedAt)> _votes = new();
    private readonly HashSet<Guid> _recentlyPlayed = new();
    private List<Song> _allSongs = new();
    private readonly Dictionary<Guid, DateTime> _playedAt = new();
    private readonly List<(Guid Id, string Title, DateTime PlayedAt)> _history = new();

    public QueueManager(ISongRepository repository, ILogger<QueueManager> logger, PresenceService presence)
    {
        _repository = repository;
        _logger = logger;
        _presence = presence;
    }

    public Song? CurrentSong
    {
        get
        {
            _lock.Wait();
            try { return _window.Count > 0 ? _window[0] : null; }
            finally { _lock.Release(); }
        }
    }

    public Song? NextSong
    {
        get
        {
            _lock.Wait();
            try { return GetNextSongLocked(); }
            finally { _lock.Release(); }
        }
    }

    public void Advance()
    {
        Song? finished = null;

        _lock.Wait();
        try
        {
            if (_window.Count == 0) return;
            finished = _window[0];
            _window.RemoveAt(0);
            _votes.Remove(finished.Id);
        }
        finally { _lock.Release(); }

        if (finished != null)
        {
            _recentlyPlayed.Add(finished.Id);
            _playedAt[finished.Id] = DateTime.UtcNow;
            lock (_history)
            {
                _history.Insert(0, (finished.Id, finished.Title, DateTime.UtcNow));
                if (_history.Count > 20) _history.RemoveAt(_history.Count - 1);
            }
            _ = _repository.IncrementPlayCountAsync(finished.Id);
        }

        _ = RefillWindowAsync();
    }

    public async Task<bool?> SuggestAsync(Guid songId, string? sessionId = null)
    {
        var song = await _repository.GetByIdAsync(songId);
        if (song == null) return false;

        if (GetVoteCooldownSeconds(songId) > 0) return null;

        await _lock.WaitAsync();
        try
        {
            if (sessionId != null)
            {
                var existingVote = _presence.GetVote(sessionId);

                if (existingVote == songId) return true;

                if (existingVote != null)
                {
                    if (_votes.TryGetValue(existingVote.Value, out var old))
                    {
                        if (old.Votes > 1)
                            _votes[existingVote.Value] = (old.Votes - 1, old.FirstVotedAt);
                        else
                            _votes.Remove(existingVote.Value);
                    }
                }
            }

            if (_votes.TryGetValue(songId, out var entry))
                _votes[songId] = (entry.Votes + 1, entry.FirstVotedAt);
            else
                _votes[songId] = (1, DateTime.UtcNow);

            if (_window.Count > 0 && _window[0].Id != songId && !_window.Any(s => s.Id == songId))
                _window.Add(song);
        }
        finally { _lock.Release(); }

        if (sessionId != null)
            _presence.SetVote(sessionId, songId);

        return true;
    }

    public async Task ReloadAsync()
    {
        try
        {
            var songs = await _repository.GetAllAsync();
            await _lock.WaitAsync();
            try { _allSongs = new List<Song>(songs); }
            finally { _lock.Release(); }

            await RefillWindowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload catalogue.");
        }
    }

    public IReadOnlyList<(Song Song, int QueuePosition)> GetQueueList()
    {
        _lock.Wait();
        try
        {
            var result = new List<(Song, int)>();
            if (_window.Count == 0) return result;

            result.Add((_window[0], 0));

            var rest = _window.Skip(1)
                .OrderByDescending(s => _votes.TryGetValue(s.Id, out var v) ? v.Votes : 0)
                .ThenBy(s => _votes.TryGetValue(s.Id, out var v) ? v.FirstVotedAt : DateTime.MaxValue)
                .ToList();

            for (int i = 0; i < rest.Count; i++)
                result.Add((rest[i], i + 1));

            return result;
        }
        finally { _lock.Release(); }
    }

    public int GetVoteCount(Guid songId)
    {
        _lock.Wait();
        try { return _votes.TryGetValue(songId, out var v) ? v.Votes : 0; }
        finally { _lock.Release(); }
    }

    public int GetVoteCooldownSeconds(Guid songId)
    {
        if (!_playedAt.TryGetValue(songId, out var playedAt)) return 0;
        var remaining = TimeSpan.FromMinutes(30) - (DateTime.UtcNow - playedAt);
        return remaining > TimeSpan.Zero ? (int)remaining.TotalSeconds : 0;
    }

    public IReadOnlyList<(Guid Id, string Title, DateTime PlayedAt)> GetHistory()
    {
        lock (_history) { return _history.ToList(); }
    }

    public async Task UnvoteAsync(Guid songId, string sessionId)
    {
        var existingVote = _presence.GetVote(sessionId);
        if (existingVote != songId) return;

        await _lock.WaitAsync();
        try
        {
            if (_votes.TryGetValue(songId, out var entry))
            {
                if (entry.Votes > 1)
                    _votes[songId] = (entry.Votes - 1, entry.FirstVotedAt);
                else
                    _votes.Remove(songId);
            }
        }
        finally { _lock.Release(); }

        _presence.ClearVote(sessionId);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReloadAsync();
        _ = ReloadLoopAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Song? GetNextSongLocked()
    {
        if (_window.Count < 2) return null;

        return _window.Skip(1)
            .OrderByDescending(s => _votes.TryGetValue(s.Id, out var v) ? v.Votes : 0)
            .ThenBy(s => _votes.TryGetValue(s.Id, out var v) ? v.FirstVotedAt : DateTime.MaxValue)
            .FirstOrDefault();
    }

    private async Task RefillWindowAsync()
    {
        try
        {
            var fresh = await _repository.GetAllAsync();

            await _lock.WaitAsync();
            try
            {
                _allSongs = new List<Song>(fresh);

                if (_allSongs.Count == 0) { _window.Clear(); _recentlyPlayed.Clear(); return; }

                if (_allSongs.All(s => _recentlyPlayed.Contains(s.Id)))
                    _recentlyPlayed.Clear();

                var inWindow = _window.Select(s => s.Id).ToHashSet();

                while (_window.Count < WindowSize)
                {
                    var candidate = _allSongs
                        .Where(s => !_recentlyPlayed.Contains(s.Id) && !inWindow.Contains(s.Id))
                        .OrderBy(_ => Guid.NewGuid())
                        .FirstOrDefault();

                    candidate ??= _allSongs
                        .Where(s => !inWindow.Contains(s.Id))
                        .OrderBy(_ => Guid.NewGuid())
                        .FirstOrDefault();

                    if (candidate == null) break;

                    _window.Add(candidate);
                    inWindow.Add(candidate.Id);
                }
            }
            finally { _lock.Release(); }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refill queue window.");
        }
    }

    private async Task ReloadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
            catch (TaskCanceledException) { break; }
            await ReloadAsync();
        }
    }
}
