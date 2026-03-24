using Microsoft.AspNetCore.Mvc;
using BndRadio.Services;
using System.Text.RegularExpressions;
using System.Globalization;

namespace BndRadio.Controllers;

public record PingRequest(string? Username, string? Color);
public record AnnounceRequest(string? Text);
public record ReactRequest(string? Emoji);

[ApiController]
[Route("presence")]
public class PresenceController : ControllerBase
{
    private readonly PresenceService _presence;
    private readonly SseHub _hub;

    public PresenceController(PresenceService presence, SseHub hub)
    {
        _presence = presence;
        _hub = hub;
    }

    private static readonly Regex UsernameRegex = new(@"^[\w\s#\-\.]{1,32}$", RegexOptions.Compiled);
    private static readonly Regex ColorRegex = new(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$", RegexOptions.Compiled);

    [HttpPost("ping")]
    public IActionResult Ping([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PingRequest? body)
    {
        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest("X-Session-Id header required");

        if (body?.Username is not null)
        {
            if (body.Username.Length > 32 || !UsernameRegex.IsMatch(body.Username))
                return BadRequest("username must be 32 characters or fewer and contain only letters, digits, spaces, #, -, .");
        }

        if (!string.IsNullOrEmpty(body?.Color))
        {
            if (!ColorRegex.IsMatch(body.Color))
                return BadRequest("color must be a valid hex color (#RGB or #RRGGBB)");
        }

        _presence.Ping(sessionId, body?.Username, body?.Color);

        return Ok(new
        {
            count = _presence.GetActiveCount(),
            users = _presence.GetActiveUsers()
                .Select(u => new { username = u.Username, color = u.Color })
        });
    }

    [HttpGet("count")]
    public IActionResult Count() => Ok(new
    {
        count = _presence.GetActiveCount(),
        users = _presence.GetActiveUsers()
            .Select(u => new { username = u.Username, color = u.Color })
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

    [HttpPost("react")]
    public async Task<IActionResult> React([FromBody] ReactRequest body)
    {
        if (string.IsNullOrWhiteSpace(body?.Emoji)) return BadRequest();

        var enumerator = StringInfo.GetTextElementEnumerator(body.Emoji);
        int clusterCount = 0;
        while (enumerator.MoveNext()) clusterCount++;
        if (clusterCount > 4) return BadRequest("emoji must be 4 grapheme clusters or fewer");

        await _hub.BroadcastAsync("reaction", new { emoji = body.Emoji });
        return Ok();
    }
}
