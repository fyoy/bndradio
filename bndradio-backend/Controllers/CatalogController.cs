// GET  /songs          — paginated song catalogue (public).
// DELETE /songs/{id}   — removes a song and its MinIO object (admin only).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BndRadio.Interfaces;

namespace BndRadio.Controllers;

[ApiController]
[Route("songs")]
public class CatalogController(ISongRepository repository) : ControllerBase
{
    private readonly ISongRepository _repository = repository;

    [HttpGet]
    public async Task<IActionResult> GetAllAsync([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 200);

        var songs = await _repository.GetAllAsync();
        var total = songs.Count;
        var items = songs
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new { id = s.Id, title = s.Title, artist = s.Artist, durationMs = s.DurationMs });

        return Ok(new { items, total, page, pageSize });
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id)
    {
        var song = await _repository.GetByIdAsync(id);
        if (song is null) return NotFound();
        await _repository.DeleteAsync(id);
        return NoContent();
    }
}
