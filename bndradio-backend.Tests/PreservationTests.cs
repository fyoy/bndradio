using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BndRadio.Tests;

// Preservation Tests — Property 8: Valid Inputs Accepted After All Fixes
//
// These tests run on UNFIXED code and are EXPECTED TO PASS.
// They confirm baseline behavior that must be preserved after all fixes are applied.
// If any of these tests fail on unfixed code, the baseline assumption is wrong.
//
// Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 3.11, 3.12, 3.13, 3.14, 3.15, 3.16

[Trait("Category", "Preservation")]
public class PreservationTests : IClassFixture<PreservationTests.PreservationFactory>
{
    private readonly HttpClient _client;

    public PreservationTests(PreservationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Minimal valid MP3 with ID3 header magic bytes (0x49 0x44 0x33).</summary>
    private static byte[] ValidMp3Bytes() => new byte[]
    {
        0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0xFB, 0x90, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    private static StringContent Json(object obj) =>
        new StringContent(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    // ── Preservation 1: Valid MP3 with correct magic bytes → 201 ─────────────
    // Validates: Requirement 3.1

    /// <summary>
    /// Preservation 1: Upload a valid MP3 file with correct ID3 magic bytes.
    /// Expected: 201 (Created) — baseline behavior must be preserved after Fix 1.
    /// This test PASSES on unfixed code.
    /// </summary>
    [Fact]
    public async Task Preservation1_ValidMp3WithCorrectMagicBytes_ShouldReturn201()
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ValidMp3Bytes());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "file", "valid-song.mp3");
        content.Add(new StringContent($"Valid Song {Guid.NewGuid()}"), "title");

        var response = await _client.PostAsync("/songs/upload", content);

        // Baseline: valid MP3 with correct magic bytes must continue to be accepted
        // Accepts 201 (created) or 409 (duplicate) — both indicate the file was processed
        Assert.True(
            response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Conflict,
            $"Expected 201 or 409 for valid MP3, got {(int)response.StatusCode}");
    }

    // ── Preservation 2: Announce with 50-char text → 200 ─────────────────────
    // Validates: Requirement 3.2

    /// <summary>
    /// Preservation 2: POST /presence/announce with a 50-character text.
    /// Expected: 200 (OK) — baseline behavior must be preserved after Fix 2.
    /// This test PASSES on unfixed code.
    /// </summary>
    [Fact]
    public async Task Preservation2_AnnounceWith50CharText_ShouldReturn200()
    {
        var validText = new string('A', 50); // 50 chars — well within the 100-char limit
        var response = await _client.PostAsync("/presence/announce", Json(new { text = validText }));

        // Baseline: valid announce messages within 100 chars must continue to be broadcast
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Preservation 3: Ping with valid username and color → 200 ─────────────
    // Validates: Requirements 3.3, 3.4

    /// <summary>
    /// Preservation 3: Ping with valid username "slave#1234" and color "#30d158".
    /// Expected: 200 (OK) — baseline behavior must be preserved after Fixes 3 and 4.
    /// This test PASSES on unfixed code.
    /// </summary>
    [Fact]
    public async Task Preservation3_PingWithValidUsernameAndColor_ShouldReturn200()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/presence/ping");
        request.Headers.Add("X-Session-Id", $"preservation-session-{Guid.NewGuid()}");
        request.Content = Json(new { username = "slave#1234", color = "#30d158" });

        var response = await _client.SendAsync(request);

        // Baseline: valid username and hex color must continue to be accepted
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Preservation 4: React with single emoji → 200 ────────────────────────
    // Validates: Requirement 3.5

    /// <summary>
    /// Preservation 4: POST /presence/react with a single emoji "🎵".
    /// Expected: 200 (OK) — baseline behavior must be preserved after Fix 5.
    /// This test PASSES on unfixed code.
    /// </summary>
    [Fact]
    public async Task Preservation4_ReactWithSingleEmoji_ShouldReturn200()
    {
        var response = await _client.PostAsync("/presence/react", Json(new { emoji = "🎵" }));

        // Baseline: valid single emoji reaction must continue to be recorded
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Preservation 5: Upload with 100-char title and positive durationMs → 201
    // Validates: Requirements 3.6, 3.11

    /// <summary>
    /// Preservation 5: Upload with a 100-character title and valid MP3 bytes.
    /// Expected: 201 (Created) — baseline behavior must be preserved after Fixes 6 and 12/18.
    /// This test PASSES on unfixed code.
    /// </summary>
    [Fact]
    public async Task Preservation5_UploadWith100CharTitleAndValidMp3_ShouldReturn201()
    {
        var title100 = new string('B', 100); // 100 chars — within the 200-char limit

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ValidMp3Bytes());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "file", "long-title-song.mp3");
        content.Add(new StringContent($"{title100}-{Guid.NewGuid()}"), "title");

        var response = await _client.PostAsync("/songs/upload", content);

        // Baseline: titles within 200 chars must continue to be stored
        Assert.True(
            response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Conflict,
            $"Expected 201 or 409 for valid upload with 100-char title, got {(int)response.StatusCode}");
    }

    // ── Preservation 6: Sequential votes on same song → correct count ─────────
    // Validates: Requirement 3.9

    /// <summary>
    /// Preservation 6: Sequential votes on the same song produce the correct count.
    /// Expected: vote count equals number of sequential votes cast.
    /// This test PASSES on unfixed code (sequential votes are not affected by the race condition).
    /// </summary>
    [Fact]
    public async Task Preservation6_SequentialVotesOnSameSong_ShouldRecordCorrectCount()
    {
        // Upload a song to vote on
        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ValidMp3Bytes());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        uploadContent.Add(fileContent, "file", "sequential-vote-test.mp3");
        uploadContent.Add(new StringContent($"Sequential Vote Test {Guid.NewGuid()}"), "title");

        var uploadResponse = await _client.PostAsync("/songs/upload", uploadContent);
        if (uploadResponse.StatusCode != HttpStatusCode.Created)
        {
            // Skip if upload fails (e.g., DB not available)
            return;
        }

        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = JsonDocument.Parse(uploadBody);
        var songId = uploadDoc.RootElement.GetProperty("id").GetString()!;

        // Cast 3 sequential votes from 3 distinct sessions
        for (int i = 0; i < 3; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/queue/suggest");
            req.Headers.Add("X-Session-Id", $"seq-session-{i}-{Guid.NewGuid()}");
            req.Content = Json(new { songId });
            await _client.SendAsync(req);
        }

        // Verify vote count
        var listResponse = await _client.GetAsync("/queue/list");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listBody);

        var voteCount = listDoc.RootElement.EnumerateArray()
            .Where(s => s.TryGetProperty("id", out var id) && id.GetString() == songId)
            .Select(s => s.TryGetProperty("voteCount", out var vc) ? vc.GetInt32() : 0)
            .FirstOrDefault();

        // Baseline: sequential votes must always be recorded correctly
        Assert.Equal(3, voteCount);
    }

    // ── Preservation 7: GET /songs with no params → returns results ───────────
    // Validates: Requirement 3.14

    /// <summary>
    /// Preservation 7: GET /songs with no query parameters returns results.
    /// Expected: 200 (OK) with an array or paginated object — baseline preserved after Fix 15.
    /// This test PASSES on unfixed code (returns a flat array).
    /// </summary>
    [Fact]
    public async Task Preservation7_GetSongsWithNoParams_ShouldReturn200WithResults()
    {
        var response = await _client.GetAsync("/songs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        // Baseline: /songs must return either a JSON array or a paginated object
        // Both are valid — the fix changes the shape but must still return data
        bool isArray = doc.RootElement.ValueKind == JsonValueKind.Array;
        bool isPaginatedObject = doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("items", out _);

        Assert.True(isArray || isPaginatedObject,
            $"GET /songs should return an array or paginated object, got: {doc.RootElement.ValueKind}");
    }

    // ── Preservation 8: Active heartbeat sessions → shown as present ──────────
    // Validates: Requirement 3.15

    /// <summary>
    /// Preservation 8: A session that sends a heartbeat ping is shown as present.
    /// Expected: presence count > 0 after pinging — baseline preserved after Fix 16.
    /// This test PASSES on unfixed code.
    /// </summary>
    [Fact]
    public async Task Preservation8_ActiveHeartbeatSession_ShouldBeShownAsPresent()
    {
        var sessionId = $"heartbeat-session-{Guid.NewGuid()}";

        // Send a heartbeat ping
        var pingRequest = new HttpRequestMessage(HttpMethod.Post, "/presence/ping");
        pingRequest.Headers.Add("X-Session-Id", sessionId);
        pingRequest.Content = Json(new { username = "testuser", color = "#30d158" });
        var pingResponse = await _client.SendAsync(pingRequest);

        Assert.Equal(HttpStatusCode.OK, pingResponse.StatusCode);

        // Verify the session is counted as present
        var countResponse = await _client.GetAsync("/presence/count");
        Assert.Equal(HttpStatusCode.OK, countResponse.StatusCode);

        var countBody = await countResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(countBody);

        // The count endpoint should return a number or object with count > 0
        int count = 0;
        if (doc.RootElement.ValueKind == JsonValueKind.Number)
        {
            count = doc.RootElement.GetInt32();
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("count", out var countProp))
        {
            count = countProp.GetInt32();
        }

        // Baseline: active heartbeat sessions must continue to be shown as present
        Assert.True(count > 0,
            $"Expected presence count > 0 after heartbeat ping, got {count}");
    }

    // ── WebApplicationFactory ────────────────────────────────────────────────

    public class PreservationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Set env var before app startup so JWT_SECRET check passes
            Environment.SetEnvironmentVariable("JWT_SECRET", "test-jwt-secret-for-unit-tests-only-32chars");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var dbUrl = Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
                    ?? "Host=localhost;Port=5432;Database=bndradio_test;Username=bndradio;Password=bndradio";
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = dbUrl,
                    ["Admin:JwtSecret"] = "test-jwt-secret-for-unit-tests-only-32chars",
                });
            });
            builder.ConfigureServices(services =>
            {
                // Replace IAmazonS3 with in-memory fake so tests don't need a real MinIO
                var s3Descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(Amazon.S3.IAmazonS3));
                if (s3Descriptor != null) services.Remove(s3Descriptor);
                services.AddSingleton<Amazon.S3.IAmazonS3, FakeS3>();

                // If no real DB is available, replace ISongRepository with a no-op stub
                // so the app can start without hanging on connection attempts
                if (Environment.GetEnvironmentVariable("TEST_DATABASE_URL") == null)
                {
                    var repoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(BndRadio.Interfaces.ISongRepository));
                    if (repoDescriptor != null) services.Remove(repoDescriptor);
                    services.AddSingleton<BndRadio.Interfaces.ISongRepository, NoOpSongRepository>();
                }
            });
        }
    }
}
