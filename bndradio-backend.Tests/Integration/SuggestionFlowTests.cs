using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BndRadio.Tests.Integration;

// Feature: bndradio-streaming-service, Integration: end-to-end suggestion flow
// Validates: Requirements 4.3

[Trait("Category", "Integration")]
public class SuggestionFlowTests : IClassFixture<SuggestionFlowTests.TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SuggestionFlowTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Suggest_ValidSongId_Returns200_AndNextSongIsUpdated()
    {
        // Step 1: Upload a song to get a valid song ID
        var mp3Bytes = CreateMinimalMp3();
        using var uploadContent = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(mp3Bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        uploadContent.Add(fileContent, "file", "suggestion-test.mp3");
        uploadContent.Add(new StringContent("Suggestion Test Song"), "title");
        uploadContent.Add(new StringContent("Suggestion Test Artist"), "artist");

        var uploadResponse = await _client.PostAsync("/songs/upload", uploadContent);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = JsonDocument.Parse(uploadBody);
        var uploadedId = uploadDoc.RootElement.GetProperty("id").GetString()!;
        Assert.True(Guid.TryParse(uploadedId, out _), "Uploaded song ID should be a valid GUID");

        // Step 2: POST to /queue/suggest with the uploaded song ID
        var suggestPayload = JsonSerializer.Serialize(new { songId = uploadedId });
        using var suggestContent = new StringContent(suggestPayload, Encoding.UTF8, "application/json");

        var suggestResponse = await _client.PostAsync("/queue/suggest", suggestContent);
        Assert.Equal(HttpStatusCode.OK, suggestResponse.StatusCode);

        // Step 3: GET /queue/next and assert the returned song matches the suggested song
        var nextResponse = await _client.GetAsync("/queue/next");
        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);

        var nextBody = await nextResponse.Content.ReadAsStringAsync();
        using var nextDoc = JsonDocument.Parse(nextBody);
        var nextId = nextDoc.RootElement.GetProperty("id").GetString();

        Assert.Equal(uploadedId, nextId);
    }

    /// <summary>
    /// Minimal ID3v2.3 header followed by a single MPEG audio frame header.
    /// TagLibSharp can parse this and will report duration as 0 ms.
    /// </summary>
    private static byte[] CreateMinimalMp3()
    {
        return new byte[]
        {
            // ID3v2.3 header
            0x49, 0x44, 0x33, // "ID3"
            0x03, 0x00,       // version 2.3, revision 0
            0x00,             // flags
            0x00, 0x00, 0x00, 0x00, // syncsafe size = 0

            // MPEG1 Layer3 frame header: sync + MPEG1 + Layer3 + 128kbps + 44100Hz + stereo
            0xFF, 0xFB, 0x90, 0x00,

            // Minimal padding to form a plausible frame body (silence)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
    }

    public class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
                        ?? "Host=localhost;Port=5432;Database=bndradio_test;Username=bndradio;Password=bndradio",
                    ["Admin:JwtSecret"] = "test-jwt-secret-for-unit-tests-only-32chars",
                });
            });
        }
    }
}
