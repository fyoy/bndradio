// Manages the playback queue using a sliding window of up to 5 songs.
// Songs are ordered by vote count (descending) then first-voted time (ascending).
// Reloads the catalogue from the repository every 30 seconds.
// All window mutations are protected by a SemaphoreSlim lock.
using BndRadio.Domain;
using BndRadio.Interfaces;

namespace BndRadio.Services;

public class QueueManager(ISongRepository repository, ILogger<QueueManager> logger, PresenceService presence) : IQueueManager, IHostedService
{
    private const int WindowSize = 5;
    private readonly ISongRepository _repository = repository;
    private readonly ILogger<QueueManager> _logger = logger;
    private readonly PresenceService _presence = presence;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly List<Song> _window = [];
    private readonly Dictionary<Guid, (int Votes, DateTime FirstVotedAt)> _votes = [];
    private List<Song> _allSongs = [];

    public Song? CurrentSong
    {
        get
        {
            lock (_lock)
            {
                return _window.Count > 0 ? _window[0] : null;
            }
        }
    }

    public Song? NextSong
    {
        get
        {
            lock (_lock)
            {
                return GetNextSongLocked();
            }
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
            SortWindowLocked();
        }
        finally { _lock.Release(); }

        if (finished != null)
        {
            _ = _repository.IncrementPlayCountAsync(finished.Id);
        }

        _ = RefillWindowAsync();
    }

    public async Task<bool?> SuggestAsync(Guid songId, string? sessionId = null)
    {
        var song = await _repository.GetByIdAsync(songId);
        if (song == null) return false;

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

            SortWindowLocked();
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
            try { _allSongs = [.. songs]; }
            finally { _lock.Release(); }

            await RefillWindowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "failed to reload catalogue.");
        }
    }

    public IReadOnlyList<(Song Song, int QueuePosition)> GetQueueList()
    {
        lock (_lock)
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
    }

    public int GetVoteCount(Guid songId)
    {
        lock (_lock)
        {
            return _votes.TryGetValue(songId, out var v) ? v.Votes : 0;
        }
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

    private Song? GetNextSongLocked() => _window.Count >= 2 ? _window[1] : null;

    private void SortWindowLocked()
    {
        if (_window.Count < 2) return;

        var current = _window[0];
        var sorted = _window.Skip(1)
            .OrderByDescending(s => _votes.TryGetValue(s.Id, out var v) ? v.Votes : 0)
            .ThenBy(s => _votes.TryGetValue(s.Id, out var v) ? v.FirstVotedAt : DateTime.MaxValue)
            .ToList();

        _window.Clear();
        _window.Add(current);
        _window.AddRange(sorted);
    }

    private async Task RefillWindowAsync()
    {
        try
        {
            var fresh = await _repository.GetAllAsync();

            await _lock.WaitAsync();
            try
            {
                _allSongs = [.. fresh];

                if (_allSongs.Count == 0) { _window.Clear(); return; }

                var inWindow = _window.Select(s => s.Id).ToHashSet();

                while (_window.Count < WindowSize)
                {
                    var candidate = _allSongs
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
            _logger.LogError(ex, "failed to refill queue window");
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
