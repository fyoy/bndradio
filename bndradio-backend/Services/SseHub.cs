using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace BndRadio.Services;

public class SseHub(ILogger<SseHub> logger)
{
    private readonly ConcurrentDictionary<Guid, SseClient> _clients = new();
    private readonly ILogger<SseHub> _logger = logger;

    public int ClientCount => _clients.Count;

    public (Guid id, SseClient client) AddClient(HttpResponse response, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var client = new SseClient(response, ct);
        _clients[id] = client;

        ct.Register(() =>
        {
            _clients.TryRemove(id, out _);
        });

        return (id, client);
    }

    public async Task BroadcastAsync(string eventName, object data)
    {
        if (_clients.IsEmpty) return;

        var json = JsonSerializer.Serialize(data);
        var message = $"event: {eventName}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(message);

        var dead = new List<Guid>();

        foreach (var (id, client) in _clients)
        {
            try
            {
                await client.WriteAsync(bytes);
            }
            catch
            {
                dead.Add(id);
            }
        }

        foreach (var id in dead)
            _clients.TryRemove(id, out _);
    }
}

public class SseClient(HttpResponse response, CancellationToken ct)
{
    private readonly HttpResponse _response = response;
    private readonly CancellationToken _ct = ct;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task WriteAsync(byte[] data)
    {
        await _writeLock.WaitAsync(_ct);
        try
        {
            await _response.Body.WriteAsync(data, _ct);
            await _response.Body.FlushAsync(_ct);
        }
        finally { _writeLock.Release(); }
    }
}
