using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using BndRadio.Services;
using BndRadio.Interfaces;
using System.Text;
using System.Text.Json;

namespace BndRadio.Controllers;

[ApiController]
[Route("events")]
public class SseController : ControllerBase
{
    private readonly SseHub _hub;
    private readonly IQueueManager _queue;
    private readonly IStreamServer _stream;
    private readonly PresenceService _presence;

    public SseController(SseHub hub, IQueueManager queue, IStreamServer stream, PresenceService presence)
    {
        _hub = hub;
        _queue = queue;
        _stream = stream;
        _presence = presence;
    }

    [HttpGet]
    public async Task ConnectAsync(CancellationToken ct)
    {
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var (_, client) = _hub.AddClient(Response, ct);

        await SendInitialStateAsync(client, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(25_000, ct);
                var ping = Encoding.UTF8.GetBytes(": ping\n\n");
                await client.WriteAsync(ping);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SendInitialStateAsync(SseClient client, CancellationToken ct)
    {
        try
        {
            var broadcastState = _stream.GetBroadcastState();
            var current = broadcastState.CurrentSong;
            var next = _queue.NextSong;

            var statePayload = new
            {
                current = new { id = current.Id, title = current.Title, durationMs = current.DurationMs },
                next    = next == null ? null : new { id = next.Id, title = next.Title },
                elapsedMs  = (long)broadcastState.ElapsedInCurrentSong.TotalMilliseconds,
                skipVotes  = _presence.GetSkipVoteCount(current.Id),
                skipNeeded = PresenceService.SkipThreshold,
            };

            var presencePayload = new
            {
                count = _presence.GetActiveCount(),
                users = _presence.GetActiveUsers().Select(u => new { username = u.Username, color = u.Color }),
            };

            var sb = new StringBuilder();
            sb.Append($"event: state\ndata: {JsonSerializer.Serialize(statePayload)}\n\n");
            sb.Append($"event: presence\ndata: {JsonSerializer.Serialize(presencePayload)}\n\n");

            await client.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()));
        }
        catch { }
    }
}
