// GET /stream — opens a persistent HTTP connection and streams raw MP3 bytes.
// Each listener gets a dedicated channel reader from StreamServer.
// Response headers disable buffering so chunks arrive in real time.
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using BndRadio.Interfaces;

namespace BndRadio.Controllers;

[ApiController]
[Route("")]
public class StreamController(IStreamServer streamServer) : ControllerBase
{
    private readonly IStreamServer _streamServer = streamServer;

    [HttpGet("stream")]
    public async Task StreamAsync(CancellationToken ct)
    {
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        Response.ContentType = "audio/mpeg";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.Headers["X-Content-Duration"] = "0";
        Response.Headers["icy-br"] = "128";
        Response.Headers["icy-metaint"] = "0";

        var reader = _streamServer.AddListener(ct);

        try
        {
            await foreach (var chunk in reader.ReadAllAsync(ct))
            {
                await Response.Body.WriteAsync(chunk, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}
