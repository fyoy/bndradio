// Reads audio from the repository and broadcasts it to all connected listeners
// as raw MP3 byte chunks via bounded channels.
// Paces delivery to match the song's real-time bitrate so listeners stay in sync.
// Retries repository reads for up to 30 s before pausing the stream.
using System.Collections.Concurrent;
using System.Threading.Channels;
using BndRadio.Domain;
using BndRadio.Interfaces;

namespace BndRadio.Services;

public class StreamServer(
    IQueueManager queueManager,
    ISongRepository repository) : IStreamServer, IHostedService
{
    private readonly IQueueManager _queueManager = queueManager;
    private readonly ISongRepository _repository = repository;

    private readonly ConcurrentDictionary<Guid, Channel<byte[]>> _listeners = new();

    private Song? _currentSong;
    private DateTimeOffset _songStartTime;

    private CancellationTokenSource? _cts;
    private Task? _broadcastTask;
    private CancellationTokenSource _songCts = new();

    public ChannelReader<byte[]> AddListener(CancellationToken ct)
    {
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = false
        });

        var id = Guid.NewGuid();
        _listeners[id] = channel;

        ct.Register(() =>
        {
            if (_listeners.TryRemove(id, out var ch))
                ch.Writer.TryComplete();
        });

        return channel.Reader;
    }

    public BroadcastState GetBroadcastState()
    {
        var current = _currentSong ?? new Song(Guid.Empty, "Loading...", "", 0);
        var next = _queueManager.NextSong ?? new Song(Guid.Empty, "", "", 0);
        var elapsed = DateTimeOffset.UtcNow - _songStartTime;
        return new BroadcastState(current, next, elapsed);
    }

    public void SkipCurrent()
    {
        var old = Interlocked.Exchange(ref _songCts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _broadcastTask = Task.Run(() => BroadcastLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
            await _cts.CancelAsync();

        if (_broadcastTask is not null)
        {
            try { await _broadcastTask; }
            catch (OperationCanceledException) { }
        }
    }

    private async Task BroadcastLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var song = _queueManager.CurrentSong;
            if (song is null)
            {
                await _queueManager.ReloadAsync().ConfigureAwait(false);
                await Task.Delay(2000, ct).ConfigureAwait(false);
                continue;
            }

            _currentSong = song;
            _songStartTime = DateTimeOffset.UtcNow;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _songCts.Token);
            var songCt = linked.Token;

            try
            {
                await using var stream = await OpenWithRetryAsync(song.Id, songCt).ConfigureAwait(false);

                var buffer = new byte[4096];
                long totalBytes = stream.Length;
                double durationSec = song.DurationMs > 0
                    ? song.DurationMs / 1000.0
                    : totalBytes * 8.0 / 128_000.0;
                double bytesPerMs = totalBytes / (durationSec * 1000.0);

                long bytesSent = 0;
                var started = DateTimeOffset.UtcNow;

                while (true)
                {
                    songCt.ThrowIfCancellationRequested();

                    int read = await stream.ReadAsync(buffer, songCt).ConfigureAwait(false);
                    if (read == 0) break;

                    BroadcastChunk(buffer[..read]);
                    bytesSent += read;

                    double expectedMs = bytesSent / bytesPerMs;
                    double elapsedMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                    int delayMs = (int)(expectedMs - elapsedMs) - 500;
                    if (delayMs > 10)
                        await Task.Delay(delayMs, songCt).ConfigureAwait(false);
                }

                _queueManager.Advance();
                await _queueManager.ReloadAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                _queueManager.Advance();
                await _queueManager.ReloadAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                _queueManager.Advance();
            }
        }
    }

    private async Task<Stream> OpenWithRetryAsync(Guid songId, CancellationToken ct)
    {
        const int maxRetries = 30;
        const int retryDelayMs = 1_000;

        int attempts = 0;
        bool paused = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var stream = await _repository.OpenAudioStreamAsync(songId).ConfigureAwait(false);

                return stream;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                attempts++;

                if (attempts >= maxRetries && !paused)
                {
                    PauseAllListeners();
                    paused = true;
                }

                await Task.Delay(retryDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    private void BroadcastChunk(byte[] chunk)
    {
        foreach (var (id, channel) in _listeners)
        {
            if (!channel.Writer.TryWrite(chunk))
            {
                if (_listeners.TryRemove(id, out var removed))
                    removed.Writer.TryComplete();
            }
        }
    }

    private void PauseAllListeners()
    {
        foreach (var (id, channel) in _listeners)
        {
            if (_listeners.TryRemove(id, out var removed))
                removed.Writer.TryComplete();
        }
    }
}
