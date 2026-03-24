using StackExchange.Redis;
using System.Text.Json;

namespace BndRadio.Services;

public class RedisCacheService
{
    private readonly IDatabase? _db;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConfiguration config, ILogger<RedisCacheService> logger)
    {
        _logger = logger;
        var cs = config.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(cs)) return;
        try
        {
            var mux = ConnectionMultiplexer.Connect(cs);
            _db = mux.GetDatabase();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — caching disabled");
        }
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        if (_db == null) return default;
        try
        {
            var val = await _db.StringGetAsync(key);
            if (!val.HasValue) return default;
            return JsonSerializer.Deserialize<T>((string)val!);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Redis GET failed for {Key}", key); return default; }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl)
    {
        if (_db == null) return;
        try
        {
            var json = JsonSerializer.Serialize(value);
            await _db.StringSetAsync(key, json, ttl);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Redis SET failed for {Key}", key); }
    }

    public async Task DeleteAsync(string key)
    {
        if (_db == null) return;
        try { await _db.KeyDeleteAsync(key); }
        catch (Exception ex) { _logger.LogWarning(ex, "Redis DEL failed for {Key}", key); }
    }

    public bool IsAvailable => _db != null;
}
