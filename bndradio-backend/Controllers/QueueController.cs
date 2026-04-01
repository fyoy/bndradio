// Queue management endpoints:
// GET  /queue/list          — full catalogue with vote counts and queue positions.
// GET  /queue/state         — current song, elapsed time, skip vote count.
// POST /queue/suggest       — cast a vote for a song.
// DELETE /queue/suggest     — retract a vote.
// POST /queue/skip          — admin skips directly; granted users skip once.
// POST /queue/skip/request  — user requests skip permission from admin.
// POST /queue/skip/grant    — admin grants skip permission to a session (JWT required).
// GET  /queue/skip/status   — check if current session has a skip grant.
// GET  /queue/history       — last 20 played tracks.
using Microsoft.AspNetCore.Mvc;
using BndRadio.Interfaces;
using BndRadio.Services;

namespace BndRadio.Controllers;

public record SuggestRequest(Guid SongId);
public record GrantSkipRequest(string SessionId);

[ApiController]
[Route("queue")]
public class QueueController(IQueueManager queue, IStreamServer stream, ISongRepository repo, PresenceService presence) : ControllerBase
{
    private readonly IQueueManager _queue = queue;
    private readonly IStreamServer _stream = stream;
    private readonly ISongRepository _repo = repo;
    private readonly PresenceService _presence = presence;

    [HttpGet("list")]
    public async Task<IActionResult> GetList()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        var myVote = sessionId != null ? _presence.GetVote(sessionId) : null;

        var allSongs = await _repo.GetAllAsync();
        var queueItems = _queue.GetQueueList();
        var positionById = new Dictionary<Guid, int>();
        foreach (var (song, pos) in queueItems)
            if (!positionById.TryGetValue(song.Id, out var ex) || pos < ex)
                positionById[song.Id] = pos;

        return Ok(allSongs.Select(s => new
        {
            id            = s.Id,
            title         = s.Title,
            artist        = s.Artist,
            durationMs    = s.DurationMs,
            playCount     = s.PlayCount,
            queuePosition = positionById.TryGetValue(s.Id, out var p) ? (int?)p : null,
            myVote        = myVote.HasValue && myVote.Value == s.Id,
            voteCount     = _queue.GetVoteCount(s.Id),
        }));
    }

    [HttpGet("state")]
    public IActionResult GetState()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        var state = _stream.GetBroadcastState();
        var cId = state.CurrentSong.Id;
        return Ok(new
        {
            current    = new { id = cId, title = state.CurrentSong.Title, artist = state.CurrentSong.Artist, durationMs = state.CurrentSong.DurationMs },
            elapsedMs  = (long)state.ElapsedInCurrentSong.TotalMilliseconds,
        });
    }

    [HttpGet("next")]
    public IActionResult GetNext()
    {
        var song = _queue.NextSong;
        return song is null ? NoContent() : Ok(new { id = song.Id, title = song.Title, artist = song.Artist });
    }

    [HttpGet("current")]
    public IActionResult GetCurrent()
    {
        var song = _queue.CurrentSong;
        return song is null ? NoContent() : Ok(new { id = song.Id, title = song.Title, artist = song.Artist });
    }

    [HttpPost("suggest")]
    public async Task<IActionResult> SuggestAsync([FromBody] SuggestRequest request)
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        var result = await _queue.SuggestAsync(request.SongId, sessionId);
        return result == true ? Ok() : NotFound();
    }

    [HttpDelete("suggest")]
    public async Task<IActionResult> UnvoteAsync([FromBody] SuggestRequest request)
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest("X-Session-Id required");
        await _queue.UnvoteAsync(request.SongId, sessionId);
        return Ok();
    }

    [HttpPost("skip")]
    public IActionResult Skip()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        var current = _queue.CurrentSong;
        if (current == null) return NoContent();
        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest("X-Session-Id required");

        bool isAdmin = User.Identity?.IsAuthenticated == true;
        if (!isAdmin && !_presence.ConsumeSkipGrant(sessionId))
            return StatusCode(403, new { error = "no_permission" });
            
        _stream.SkipCurrent();
        return Ok(new { skipped = true });
    }

    [HttpPost("skip/grant")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult GrantSkip([FromBody] GrantSkipRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId)) return BadRequest("sessionId required");
        _presence.GrantSkip(request.SessionId);
        return Ok(new { granted = true });
    }

    [HttpGet("skip/status")]
    public IActionResult SkipStatus()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest("X-Session-Id required");
        return Ok(new { hasGrant = _presence.HasSkipGrant(sessionId) });
    }

    [HttpPost("skip/request")]
    public IActionResult RequestSkip()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest("X-Session-Id required");
        var username = _presence.GetUsername(sessionId) ?? sessionId[..Math.Min(8, sessionId.Length)];
        _presence.RequestSkip(sessionId, username);
        return Ok(new { requested = true });
    }

    [HttpDelete("skip/request")]
    public IActionResult CancelSkipRequest()
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sessionId)) _presence.CancelSkipRequest(sessionId);
        return Ok();
    }

    [HttpGet("skip/requests")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public IActionResult GetSkipRequests() =>
        Ok(_presence.GetSkipRequests().Select(r => new { sessionId = r.SessionId, username = r.Username }));
}
