using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using BndRadio.Interfaces;

namespace BndRadio.Controllers;

[ApiController]
[Route("")]
public class StreamController : ControllerBase
{
    private readonly IStreamServer _streamServer;

    public StreamController(IStreamServer streamServer)
    {
        _streamServer = streamServer;
    }

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
