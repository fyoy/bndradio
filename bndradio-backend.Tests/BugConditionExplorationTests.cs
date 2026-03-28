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

// Bug Condition Exploration Tests
// These tests run on UNFIXED code and are EXPECTED TO FAIL.
// Failure confirms the bugs exist. DO NOT fix the code when tests fail.
// Validates: Requirements 2.1–2.18

[Trait("Category", "BugExploration")]
public class BugConditionExplorationTests : IClassFixture<BugConditionExplorationTests.BugExplorationFactory>
{
    private readonly HttpClient _client;

    public BugConditionExplorationTests(BugExplorationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Plain text bytes — not a valid audio file.</summary>
    private static byte[] TextFileBytes() =>
        Encoding.UTF8.GetBytes("This is a plain text file, not audio.");

    /// <summary>Minimal valid MP3 magic bytes (ID3 header + MPEG frame).</summary>
    private static byte[] ValidMp3Bytes() => new byte[]
    {
        0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0xFB, 0x90, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    private static string RandomTitle(int length) =>
        new string('A', length);

    private static StringContent Json(object obj) =>
        new StringContent(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    // ── Fix 1: Magic bytes validation ────────────────────────────────────────

    /// <summary>
    /// Bug 1: Upload a text file with Content-Type audio/mpeg.
    /// Expected: 415 (Unsupported Media Type)
    /// Currently: 201 (Created) — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix1_UploadTextFileWithAudioMimeType_ShouldReturn415_CurrentlyReturns201()
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TextFileBytes());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "file", "evil.mp3");
        content.Add(new StringContent("Evil Text File"), "title");

        var response = await _client.PostAsync("/songs/upload", content);

        // BUG: currently returns 201 because only Content-Type is checked, not magic bytes
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // ── Fix 2: Announce length validation ────────────────────────────────────

    /// <summary>
    /// Bug 2: POST /presence/announce with 150-char text.
    /// Expected: 400 (Bad Request)
    /// Currently: 200 (OK) — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix2_AnnounceWith150CharText_ShouldReturn400_CurrentlyReturns200()
    {
        var longText = new string('X', 150);
        var response = await _client.PostAsync("/presence/announce", Json(new { text = longText }));

        // BUG: no length validation on announce text
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Fix 3: Username validation ───────────────────────────────────────────

    /// <summary>
    /// Bug 3: Ping with XSS username.
    /// Expected: 400 (Bad Request)
    /// Currently: 200 (OK) — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix3_PingWithXssUsername_ShouldReturn400_CurrentlyReturns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/presence/ping");
        request.Headers.Add("X-Session-Id", "test-session-fix3");
        request.Content = Json(new { username = "<script>alert(1)</script>", color = "#30d158" });

        var response = await _client.SendAsync(request);

        // BUG: no username validation — arbitrary strings accepted
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Fix 4: Color validation ──────────────────────────────────────────────

    /// <summary>
    /// Bug 4: Ping with invalid color value.
    /// Expected: 400 (Bad Request)
    /// Currently: 200 (OK) — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix4_PingWithInvalidColor_ShouldReturn400_CurrentlyReturns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/presence/ping");
        request.Headers.Add("X-Session-Id", "test-session-fix4");
        request.Content = Json(new { username = "testuser", color = "not-a-color" });

        var response = await _client.SendAsync(request);

        // BUG: no color format validation
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Fix 5: Emoji validation ──────────────────────────────────────────────

    /// <summary>
    /// Bug 5: POST /presence/react with 5-emoji string.
    /// Expected: 400 (Bad Request)
    /// Currently: 200 (OK) — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix5_ReactWith5Emojis_ShouldReturn400_CurrentlyReturns200()
    {
        // 5 emoji grapheme clusters — exceeds max of 4
        var fiveEmojis = "🎵🎵🎵🎵🎵";
        var response = await _client.PostAsync("/presence/react", Json(new { emoji = fiveEmojis }));

        // BUG: no emoji length validation
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Fix 6: Title length validation ──────────────────────────────────────

    /// <summary>
    /// Bug 6: Upload with 300-char title.
    /// Expected: 400 (Bad Request)
    /// Currently: 201 (Created) — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix6_UploadWith300CharTitle_ShouldReturn400_CurrentlyReturns201()
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ValidMp3Bytes());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "file", "song.mp3");
        content.Add(new StringContent(RandomTitle(300)), "title");

        var response = await _client.PostAsync("/songs/upload", content);

        // BUG: no title length validation
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Fix 7: Security headers ──────────────────────────────────────────────

    /// <summary>
    /// Bug 7: GET any endpoint — response missing X-Frame-Options header.
    /// Expected: X-Frame-Options header present
    /// Currently: header absent — bug confirmed when test FAILS
    /// Note: This tests the backend directly; nginx headers are separate.
    /// </summary>
    [Fact]
    public async Task Fix7_GetAnyEndpoint_ShouldHaveXFrameOptionsHeader_CurrentlyMissing()
    {
        var response = await _client.GetAsync("/presence/count");

        // BUG: no security headers configured in nginx or backend
        Assert.True(
            response.Headers.Contains("X-Frame-Options"),
            "Response should include X-Frame-Options header but it is missing");
    }

    // ── Fix 8: Rate limiting ─────────────────────────────────────────────────

    /// <summary>
    /// Bug 8: Send 200 requests — all return 200, no 429.
    /// Expected: 429 after rate limit exceeded
    /// Currently: all 200 — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix8_Send200Requests_ShouldGet429_CurrentlyAllReturn200()
    {
        var tasks = Enumerable.Range(0, 200)
            .Select(_ => _client.GetAsync("/presence/count"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        var statusCodes = responses.Select(r => (int)r.StatusCode).ToList();

        // BUG: no rate limiting middleware — all requests succeed
        Assert.Contains(429, statusCodes);
    }

    // ── Fix 9: CORS from environment variable ────────────────────────────────

    /// <summary>
    /// Bug 9: CORS request from non-localhost origin with CORS_ORIGINS set.
    /// Expected: CORS allowed for configured origin
    /// Currently: blocked — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix9_CorsRequestFromNonLocalhostOrigin_ShouldBeAllowed_CurrentlyBlocked()
    {
        // Simulate a CORS preflight from a production origin
        var request = new HttpRequestMessage(HttpMethod.Options, "/presence/count");
        request.Headers.Add("Origin", "https://bndradio.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        var allowOrigin = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? values.FirstOrDefault()
            : null;

        // BUG: CORS is hardcoded to localhost:5173 — production origins are blocked
        Assert.Equal("https://bndradio.example.com", allowOrigin);
    }

    // ── Fix 10: Vote race condition ──────────────────────────────────────────

    /// <summary>
    /// Bug 10: 10 concurrent votes on same song — vote count may be less than 10.
    /// Expected: exactly 10 votes
    /// Currently: may be less due to race condition — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix10_10ConcurrentVotesOnSameSong_ShouldRecordExactly10Votes_CurrentlyMayBeLess()
    {
        // First upload a song to vote on
        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ValidMp3Bytes());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        uploadContent.Add(fileContent, "file", "race-test.mp3");
        uploadContent.Add(new StringContent($"Race Test Song {Guid.NewGuid()}"), "title");

        var uploadResponse = await _client.PostAsync("/songs/upload", uploadContent);
        if (uploadResponse.StatusCode != HttpStatusCode.Created)
        {
            // Skip if upload fails (e.g., no DB) — test infrastructure issue
            return;
        }

        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = JsonDocument.Parse(uploadBody);
        var songId = uploadDoc.RootElement.GetProperty("id").GetString()!;

        // Send 10 concurrent votes from 10 distinct sessions
        var tasks = Enumerable.Range(0, 10).Select(i =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/queue/suggest");
            req.Headers.Add("X-Session-Id", $"race-session-{i}-{Guid.NewGuid()}");
            req.Content = Json(new { songId });
            return _client.SendAsync(req);
        }).ToArray();

        await Task.WhenAll(tasks);

        // Check vote count via queue list
        var listResponse = await _client.GetAsync("/queue/list");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listBody);

        var voteCount = listDoc.RootElement.EnumerateArray()
            .Where(s => s.TryGetProperty("id", out var id) && id.GetString() == songId)
            .Select(s => s.TryGetProperty("voteCount", out var vc) ? vc.GetInt32() : 0)
            .FirstOrDefault();

        // BUG: non-atomic read-modify-write in SuggestAsync can lose votes under concurrency
        Assert.Equal(10, voteCount);
    }

    // ── Fix 11: Skip cooldown server-side ────────────────────────────────────

    /// <summary>
    /// Bug 11: Two skip votes within 5s from same session — both accepted.
    /// Expected: second vote returns 429
    /// Currently: both accepted — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix11_TwoSkipVotesWithin5Seconds_SecondShouldReturn429_CurrentlyBothAccepted()
    {
        var sessionId = $"skip-test-session-{Guid.NewGuid()}";

        // First skip vote
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/queue/skip");
        req1.Headers.Add("X-Session-Id", sessionId);
        var response1 = await _client.SendAsync(req1);

        // Second skip vote immediately (within 5s)
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/queue/skip");
        req2.Headers.Add("X-Session-Id", sessionId);
        var response2 = await _client.SendAsync(req2);

        // BUG: no server-side cooldown — both votes accepted
        Assert.Equal(HttpStatusCode.TooManyRequests, response2.StatusCode);
    }

    // ── Fix 12: Duration validation on add-song ──────────────────────────────

    /// <summary>
    /// Bug 12: Add song with durationMs=0 — currently stored.
    /// Expected: 400 (Bad Request)
    /// Currently: stored successfully — bug confirmed when test FAILS
    /// Note: This tests the direct add path; Fix 18 tests the upload path.
    /// The upload controller doesn't have a separate "add song" endpoint,
    /// so we test via upload with a file that TagLib reports 0ms for.
    /// </summary>
    [Fact]
    public async Task Fix12_UploadFileWithZeroDuration_ShouldReturn400_CurrentlyStored()
    {
        // A minimal MP3 that TagLib will parse as 0ms duration
        var zeroDurationMp3 = new byte[]
        {
            0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFB, 0x90, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(zeroDurationMp3);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "file", "zero-duration.mp3");
        content.Add(new StringContent($"Zero Duration Song {Guid.NewGuid()}"), "title");

        var response = await _client.PostAsync("/songs/upload", content);

        // BUG: no durationMs > 0 validation — zero-duration files are stored
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Fix 15: Catalog pagination ───────────────────────────────────────────

    /// <summary>
    /// Bug 15: GET /songs with 500 songs seeded — all 500 returned (no pagination).
    /// Expected: paginated results with page/pageSize support
    /// Currently: all records returned — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix15_GetSongsWithManyRecords_ShouldSupportPagination_CurrentlyReturnsAll()
    {
        // GET /songs?page=1&pageSize=10 — should return at most 10 items
        var response = await _client.GetAsync("/songs?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        // BUG: CatalogController returns all records without pagination
        // After fix, response should be an object with { items, total, page, pageSize }
        // Currently it's a flat array — check that it's NOT a flat array of > 10 items
        // OR that it has pagination metadata
        bool hasPaginationMetadata = doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("items", out _)
            && doc.RootElement.TryGetProperty("total", out _);

        Assert.True(hasPaginationMetadata,
            "GET /songs should return paginated response with 'items' and 'total' fields, but currently returns a flat array");
    }

    // ── Fix 16: Presence timeout ─────────────────────────────────────────────

    /// <summary>
    /// Bug 16: PresenceService.TimeoutSeconds is 60, should be 30.
    /// Expected: users marked absent after 30s
    /// Currently: 60s timeout — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix16_PresenceTimeoutShouldBe30Seconds_CurrentlyIs60()
    {
        // We verify the constant directly via reflection since we can't wait 35s in a test
        var presenceServiceType = typeof(BndRadio.Services.PresenceService);
        var field = presenceServiceType.GetField("TimeoutSeconds",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(field);
        var value = (int)field!.GetValue(null)!;

        // BUG: TimeoutSeconds is 60, should be 30
        Assert.Equal(30, value);
    }

    // ── Fix 17: Health endpoint ──────────────────────────────────────────────

    /// <summary>
    /// Bug 17: GET /health → 404 (should be 200).
    /// Expected: 200 OK with {"status":"healthy"}
    /// Currently: 404 — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix17_GetHealth_ShouldReturn200_CurrentlyReturns404()
    {
        var response = await _client.GetAsync("/health");

        // BUG: no /health endpoint exists
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Fix 18: Duration validation on file upload ───────────────────────────

    /// <summary>
    /// Bug 18: Upload file where TagLib reports 0ms duration — stored (should return 400).
    /// Expected: 400 (Bad Request)
    /// Currently: 201 (Created) — bug confirmed when test FAILS
    /// </summary>
    [Fact]
    public async Task Fix18_UploadFileWithZeroTagLibDuration_ShouldReturn400_CurrentlyStored()
    {
        // Minimal ID3v2.3 + MPEG frame — TagLib reports 0ms duration for this
        var minimalMp3 = new byte[]
        {
            0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFB, 0x90, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(minimalMp3);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "file", "zero-taglib.mp3");
        content.Add(new StringContent($"Zero TagLib Duration {Guid.NewGuid()}"), "title");

        var response = await _client.PostAsync("/songs/upload", content);

        // BUG: no check that TagLib-parsed durationMs > 0 before storing
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── WebApplicationFactory ────────────────────────────────────────────────

    public class BugExplorationFactory : WebApplicationFactory<Program>
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
