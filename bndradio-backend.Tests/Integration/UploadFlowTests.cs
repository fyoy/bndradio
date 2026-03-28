using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BndRadio.Tests.Integration;

// Feature: bndradio-streaming-service, Integration: end-to-end upload flow
// Validates: Requirements 6.1, 6.2

[Trait("Category", "Integration")]
public class UploadFlowTests : IClassFixture<UploadFlowTests.TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public UploadFlowTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Upload_ValidMp3_Returns201WithCorrectFields_AndAppearsInCatalog()
    {
        // Arrange
        var mp3Bytes = CreateMinimalMp3();
        using var content = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(mp3Bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(fileContent, "file", "test.mp3");
        content.Add(new StringContent("Test Song"), "title");
        content.Add(new StringContent("Test Artist"), "artist");

        // Act — POST /songs/upload
        var uploadResponse = await _client.PostAsync("/songs/upload", content);

        // Assert 201 Created
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var body = await uploadResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("id", out var idProp), "Response should contain 'id'");
        Assert.Equal(JsonValueKind.String, idProp.ValueKind);
        Assert.True(Guid.TryParse(idProp.GetString(), out var uploadedId), "'id' should be a valid GUID");

        Assert.Equal("Test Song", root.GetProperty("title").GetString());
        Assert.Equal("Test Artist", root.GetProperty("artist").GetString());
        Assert.True(root.GetProperty("durationMs").GetInt32() >= 0, "'durationMs' should be >= 0");

        // Act — GET /songs and assert the new song appears
        var catalogResponse = await _client.GetAsync("/songs");
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);

        var catalogBody = await catalogResponse.Content.ReadAsStringAsync();
        using var catalogDoc = JsonDocument.Parse(catalogBody);
        var songs = catalogDoc.RootElement.EnumerateArray().ToList();

        var found = songs.Any(s =>
            s.TryGetProperty("id", out var sid) &&
            Guid.TryParse(sid.GetString(), out var gid) &&
            gid == uploadedId);

        Assert.True(found, $"Uploaded song with id={uploadedId} should appear in GET /songs");
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
