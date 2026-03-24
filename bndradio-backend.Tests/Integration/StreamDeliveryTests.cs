using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BndRadio.Tests.Integration;

// Feature: bndradio-streaming-service, Integration: stream delivery to multiple clients
// Validates: Requirements 1.2, 1.3

[Trait("Category", "Integration")]
public class StreamDeliveryTests : IClassFixture<StreamDeliveryTests.TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StreamDeliveryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Stream_TwoSimultaneousClients_BothReceiveNonEmptyAudioMpegData()
    {
        // Step 1: Upload a song so the stream has something to broadcast
        var mp3Bytes = CreateMinimalMp3();
        var uploadClient = _factory.CreateClient();

        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(mp3Bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        uploadContent.Add(fileContent, "file", "stream-test.mp3");
        uploadContent.Add(new StringContent("Stream Test Song"), "title");
        uploadContent.Add(new StringContent("Stream Test Artist"), "artist");

        var uploadResponse = await uploadClient.PostAsync("/songs/upload", uploadContent);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        // Step 2: Connect two clients to GET /stream simultaneously using ResponseHeadersRead
        // so we can start reading before the response completes (it never completes for a stream)
        var client1 = _factory.CreateClient();
        var client2 = _factory.CreateClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var task1 = ReadStreamBytesAsync(client1, cts.Token);
        var task2 = ReadStreamBytesAsync(client2, cts.Token);

        // Step 3: Read from both clients concurrently within the timeout.
        // WhenAll is awaited; if the CancellationToken fires, ReadStreamBytesAsync catches it
        // internally and returns whatever bytes were collected before cancellation.
        var results = await Task.WhenAll(task1, task2);
        var (bytes1, contentType1) = results[0];
        var (bytes2, contentType2) = results[1];

        // Step 4: Assert both clients received non-empty data
        Assert.True(bytes1.Length > 0, "Client 1 should have received audio data from the stream");
        Assert.True(bytes2.Length > 0, "Client 2 should have received audio data from the stream");

        // Step 5: Assert Content-Type is audio/mpeg for both clients
        Assert.NotNull(contentType1);
        Assert.Contains("audio/mpeg", contentType1, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(contentType2);
        Assert.Contains("audio/mpeg", contentType2, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Opens a streaming connection to GET /stream and reads up to 8192 bytes,
    /// returning the bytes read and the Content-Type header value.
    /// Uses HttpCompletionOption.ResponseHeadersRead to begin reading before the response ends.
    /// </summary>
    private static async Task<(byte[] Bytes, string? ContentType)> ReadStreamBytesAsync(
        HttpClient client,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        var contentType = response.Content.Headers.ContentType?.ToString();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[8192];
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (read == 0)
                break;

            totalRead += read;
        }

        return (buffer[..totalRead], contentType);
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
                        ?? "Host=localhost;Port=5432;Database=bndradio_test;Username=bndradio;Password=bndradio"
                });
            });
        }
    }
}
