using Microsoft.AspNetCore.Mvc;
using BndRadio.Interfaces;
using BndRadio.Services;

namespace BndRadio.Controllers;

public record SuggestRequest(Guid SongId);
public record GrantSkipRequest(string SessionId);

[ApiController]
[Route("queue")]
public class QueueController : ControllerBase
{
    private readonly IQueueManager _queueManager;
    private readonly IStreamServer _streamServer;
    private readonly ISongRepository _repository;
    private readonly PresenceService _presence;
    private readonly RedisCacheService _cache;

    public QueueController(IQueueManager queueManager, IStreamServer streamServer, ISongRepository repository, PresenceService presence, RedisCacheService cache)
    {
        _queueManager = queueManager;
        _streamServer = streamServer;
        _repository = repository;
        _presence = presence;
        _cache = cache;
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetList()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        var myVote = sessionId != null ? _presence.GetVote(sessionId) : null;

        var cacheKey = "queue:list";
        var cached = await _cache.GetAsync<List<SongListItem>>(cacheKey);

        List<SongListItem> baseList;
        if (cached != null)
        {
            baseList = cached;
        }
        else
        {
            var allSongs = await _repository.GetAllAsync();
            var queueItems = _queueManager.GetQueueList();
            var positionById = new Dictionary<Guid, int>();
            foreach (var (song, pos) in queueItems)
            {
                if (!positionById.TryGetValue(song.Id, out var existing) || pos < existing)
                    positionById[song.Id] = pos;
            }

            baseList = allSongs.Select(s => new SongListItem(
                s.Id, s.Title, s.Artist, s.DurationMs, s.PlayCount,
                positionById.TryGetValue(s.Id, out var p) ? (int?)p : null,
                _queueManager.GetVoteCount(s.Id),
                _queueManager.GetVoteCooldownSeconds(s.Id)
            )).ToList();

            await _cache.SetAsync(cacheKey, baseList, TimeSpan.FromSeconds(1.5));
        }

        return Ok(baseList.Select(s => new
        {
            id            = s.Id,
            title         = s.Title,
            artist        = s.Artist,
            durationMs    = s.DurationMs,
            playCount     = s.PlayCount,
            queuePosition = s.QueuePosition,
            myVote        = myVote.HasValue && myVote.Value == s.Id,
            voteCount     = s.VoteCount,
            voteCooldown  = s.VoteCooldown,
        }));
    }

    private record SongListItem(
        Guid Id, string Title, string? Artist, int DurationMs, int PlayCount,
        int? QueuePosition, int VoteCount, int VoteCooldown);

    [HttpGet("state")]
    public async Task<IActionResult> GetState()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        var broadcastState = _streamServer.GetBroadcastState();
        var cId = broadcastState.CurrentSong.Id;

        return Ok(new
        {
            current = new { id = cId, title = broadcastState.CurrentSong.Title, artist = broadcastState.CurrentSong.Artist, durationMs = broadcastState.CurrentSong.DurationMs },
            elapsedMs  = (long)broadcastState.ElapsedInCurrentSong.TotalMilliseconds,
            skipVotes  = _presence.GetSkipVoteCount(cId),
            skipNeeded = PresenceService.SkipThreshold,
            mySkipVote = sessionId != null && _presence.HasVotedSkip(sessionId, cId)
        });
    }

    [HttpGet("next")]
    public IActionResult GetNext()
    {
        var song = _queueManager.NextSong;
        if (song is null) return NoContent();
        return Ok(new { id = song.Id, title = song.Title, artist = song.Artist });
    }

    [HttpGet("current")]
    public IActionResult GetCurrent()
    {
        var song = _queueManager.CurrentSong;
        if (song is null) return NoContent();
        return Ok(new { id = song.Id, title = song.Title, artist = song.Artist });
    }

    [HttpPost("suggest")]
    public async Task<IActionResult> SuggestAsync([FromBody] SuggestRequest request)
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();

        var cooldown = _queueManager.GetVoteCooldownSeconds(request.SongId);
        if (cooldown > 0)
            return StatusCode(429, new { error = "cooldown", secondsRemaining = cooldown });

        var result = await _queueManager.SuggestAsync(request.SongId, sessionId);

        await _cache.DeleteAsync("queue:list");

        return result == true ? Ok() : NotFound();
    }

    [HttpDelete("suggest")]
    public async Task<IActionResult> UnvoteAsync([FromBody] SuggestRequest request)
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest("X-Session-Id header required");

        await _queueManager.UnvoteAsync(request.SongId, sessionId);
        await _cache.DeleteAsync("queue:list");

        return Ok();
    }

    [HttpPost("skip")]
    public async Task<IActionResult> Skip()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        var current = _queueManager.CurrentSong;
        if (current == null) return NoContent();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest("X-Session-Id header required");

        // Admins can skip directly (JWT required)
        bool isAdmin = User.Identity?.IsAuthenticated == true;

        if (!isAdmin)
        {
            // Non-admins need a grant from admin
            if (!_presence.ConsumeSkipGrant(sessionId))
                return StatusCode(403, new { error = "no_permission", message = "Запросите разрешение у администратора" });
        }

        // Admin direct skip — bypass vote threshold
        if (isAdmin)
        {
            _presence.ResetSkipVotes(current.Id);
            _streamServer.SkipCurrent();
            await _cache.DeleteAsync("queue:state");
            return Ok(new { skipped = true, votes = 0, needed = PresenceService.SkipThreshold });
        }

        // Granted user skip — also direct
        _presence.ResetSkipVotes(current.Id);
        _streamServer.SkipCurrent();
        await _cache.DeleteAsync("queue:state");
        return Ok(new { skipped = true, votes = 0, needed = PresenceService.SkipThreshold });
    }

    [HttpPost("skip/grant")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult GrantSkip([FromBody] GrantSkipRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            return BadRequest("sessionId required");

        _presence.GrantSkip(request.SessionId);
        return Ok(new { granted = true });
    }

    [HttpGet("skip/status")]
    public IActionResult SkipStatus()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest("X-Session-Id header required");

        return Ok(new { hasGrant = _presence.HasSkipGrant(sessionId) });
    }

    [HttpPost("skip/request")]
    public async Task<IActionResult> RequestSkip()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest("X-Session-Id header required");

        // Get username from presence
        var username = _presence.GetUsername(sessionId) ?? (sessionId.Length >= 8 ? sessionId[..8] : sessionId);
        _presence.RequestSkip(sessionId, username);

        return Ok(new { requested = true });
    }

    [HttpDelete("skip/request")]
    public IActionResult CancelSkipRequest()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sessionId))
            _presence.CancelSkipRequest(sessionId);
        return Ok();
    }

    [HttpGet("skip/requests")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult GetSkipRequests()
    {
        var requests = _presence.GetSkipRequests();
        return Ok(requests.Select(r => new { sessionId = r.SessionId, username = r.Username }));
    }

    [HttpGet("history")]
    public IActionResult GetHistory()
    {
        if (_queueManager is QueueManager qm)
        {
            var history = qm.GetHistory();
            return Ok(history.Select(h => new
            {
                id = h.Id,
                title = h.Title,
                playedAt = h.PlayedAt.ToString("o"),
            }));
        }
        return Ok(Array.Empty<object>());
    }
}
