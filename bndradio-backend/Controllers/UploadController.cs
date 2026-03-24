using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BndRadio.Interfaces;

namespace BndRadio.Controllers;

[ApiController]
[Route("songs")]
[RequestSizeLimit(500_000_000)]
public class UploadController : ControllerBase
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mpeg", "audio/mp3", "audio/ogg", "audio/wav", "audio/wave",
        "audio/x-wav", "audio/flac", "audio/x-flac", "audio/mp4", "audio/x-m4a",
        "audio/aac", "audio/opus", "audio/webm",
    };

    private static bool HasValidAudioMagicBytes(byte[] header)
    {
        if (header.Length < 4) return false;

        if (header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33) return true;
        if (header[0] == 0xFF && (header[1] == 0xFB || header[1] == 0xF3 || header[1] == 0xF2)) return true;
        if (header[0] == 0x4F && header[1] == 0x67 && header[2] == 0x67 && header[3] == 0x53) return true;
        if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46) return true;
        if (header[0] == 0x66 && header[1] == 0x4C && header[2] == 0x61 && header[3] == 0x43) return true;
        if (header.Length >= 8 &&
            header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70) return true;

        return false;
    }

    private readonly ISongRepository _repository;

    public UploadController(ISongRepository repository)
    {
        _repository = repository;
    }

    [Authorize]
    [HttpPost("upload")]
    public async Task<IActionResult> UploadAsync(IFormFile file, [FromForm] string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest("title is required");

        if (title.Length > 200)
            return BadRequest("title must be 200 characters or fewer");

        if (file is null || !AllowedMimeTypes.Contains(file.ContentType))
            return StatusCode(415, "Unsupported audio format");

        var existing = await _repository.GetAllAsync();
        var duplicate = existing.Any(s =>
            string.Equals(s.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));

        if (duplicate)
            return Conflict($"Трек «{title}» уже существует в каталоге.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        var header = new byte[12];
        var bytesRead = ms.Read(header, 0, header.Length);
        if (!HasValidAudioMagicBytes(header[..bytesRead]))
            return StatusCode(415, "Unsupported audio format");
        ms.Position = 0;

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ms.ToArray())).ToLowerInvariant();
        if (await _repository.ExistsByHashAsync(hash))
            return Conflict("Этот файл уже загружен в каталог.");

        ms.Position = 0;
        var tagFile = TagLib.File.Create(new StreamFileAbstraction(file.FileName, ms));
        var durationMs = (int)tagFile.Properties.Duration.TotalMilliseconds;
        if (durationMs <= 0)
            return BadRequest("audio duration must be greater than zero");
        ms.Position = 0;

        var song = await _repository.AddAsync(title, string.Empty, durationMs, ms);

        return CreatedAtAction(null, null, new { id = song.Id, title = song.Title, durationMs = song.DurationMs });
    }
}

file sealed class StreamFileAbstraction : TagLib.File.IFileAbstraction
{
    private readonly Stream _stream;

    public StreamFileAbstraction(string name, Stream stream)
    {
        Name = name;
        _stream = stream;
    }

    public string Name { get; }
    public Stream ReadStream => _stream;
    public Stream WriteStream => _stream;
    public void CloseStream(Stream stream) { }
}
