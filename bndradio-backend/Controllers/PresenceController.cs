// Handles listener presence and social features:
// POST /presence/ping      — heartbeat that keeps a session alive (every ~20 s from frontend).
// GET  /presence/count     — returns active listener count and user list.
// POST /presence/announce  — broadcasts a text message to all SSE clients.
// POST /presence/react     — broadcasts an emoji reaction to all SSE clients.
using Microsoft.AspNetCore.Mvc;
using BndRadio.Services;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BndRadio.Controllers;

public record PingRequest(string? Username, string? Color);
public record AnnounceRequest(string? Text);
public record ReactRequest(string? Emoji);

[ApiController]
[Route("presence")]
public class PresenceController(PresenceService presence) : ControllerBase
{
    private readonly PresenceService _presence = presence;
    private static readonly Regex UsernameRegex = new(@"^[\w\s#\-\.]{1,32}$", RegexOptions.Compiled);

    [HttpPost("ping")]
    public IActionResult Ping([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PingRequest? body)
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest("X-Session-Id header required");

        if (body?.Username is not null)
        {
            if (body.Username.Length > 32 || !UsernameRegex.IsMatch(body.Username))
                return BadRequest("username must be 32 characters or fewer and contain only letters, digits, spaces, #, -, .");
        }

        _presence.Ping(sessionId, body?.Username);

        return Ok(new
        {
            count = _presence.GetActiveCount(),
            users = _presence.GetActiveUsers().Select(u => new { username = u })
        });
    }

    [HttpGet("count")]
    public IActionResult Count() => Ok(new
    {
        count = _presence.GetActiveCount(),
        users = _presence.GetActiveUsers()
            .Select(u => new { username = u })
    });

    [HttpPost("announce")]
    public IActionResult Announce([FromBody] AnnounceRequest body)
    {
        if (string.IsNullOrWhiteSpace(body?.Text)) return BadRequest();
        if (body.Text.Length > 100) return BadRequest("text must be 100 characters or fewer");
        _presence.Announce(body.Text.Trim().ToUpperInvariant());
        return Ok();
    }

    [HttpGet("announces")]
    public IActionResult GetAnnounces([FromQuery] long since = 0)
    {
        var sinceDate = since > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(since).UtcDateTime
            : DateTime.UtcNow.AddSeconds(-5);
        var items = _presence.GetAnnouncesSince(sinceDate);
        return Ok(new { items, now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    }
}
