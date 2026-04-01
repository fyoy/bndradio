// Application entry point.
// Stack: MinIO (all storage) + Redis-free in-memory queue + SSE push.
using Amazon.S3;
using BndRadio;
using BndRadio.Interfaces;
using BndRadio.Services;
using System.Threading.RateLimiting;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Rate limiter — 100 req/min per IP
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromSeconds(60),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    });
    options.RejectionStatusCode = 429;
});

// JWT auth for admin endpoints
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["Admin:JwtSecret"]
    ?? throw new InvalidOperationException("JWT_SECRET is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<JwtService>();

// MinIO — map MINIO_* env vars → MinIO:* config section
builder.Configuration.AddEnvironmentVariables();
var envMap = new Dictionary<string, string?>
{
    ["MINIO_ENDPOINT"]    = builder.Configuration["MINIO_ENDPOINT"],
    ["MINIO_ACCESS_KEY"]  = builder.Configuration["MINIO_ACCESS_KEY"],
    ["MINIO_SECRET_KEY"]  = builder.Configuration["MINIO_SECRET_KEY"],
    ["MINIO_BUCKET_NAME"] = builder.Configuration["MINIO_BUCKET_NAME"],
};
if (envMap["MINIO_ENDPOINT"]    != null) builder.Configuration["MinIO:Endpoint"]   = envMap["MINIO_ENDPOINT"];
if (envMap["MINIO_ACCESS_KEY"]  != null) builder.Configuration["MinIO:AccessKey"]  = envMap["MINIO_ACCESS_KEY"];
if (envMap["MINIO_SECRET_KEY"]  != null) builder.Configuration["MinIO:SecretKey"]  = envMap["MINIO_SECRET_KEY"];
if (envMap["MINIO_BUCKET_NAME"] != null) builder.Configuration["MinIO:BucketName"] = envMap["MINIO_BUCKET_NAME"];

builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection("MinIO"));

var minioOpts = builder.Configuration.GetSection("MinIO").Get<MinioOptions>() ?? new MinioOptions();
if (string.IsNullOrWhiteSpace(minioOpts.Endpoint))
    throw new InvalidOperationException("MinIO:Endpoint (MINIO_ENDPOINT) is not configured.");
if (string.IsNullOrWhiteSpace(minioOpts.AccessKey))
    throw new InvalidOperationException("MinIO:AccessKey (MINIO_ACCESS_KEY) is not configured.");
if (string.IsNullOrWhiteSpace(minioOpts.SecretKey))
    throw new InvalidOperationException("MinIO:SecretKey (MINIO_SECRET_KEY) is not configured.");

builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var opts = builder.Configuration.GetSection("MinIO").Get<MinioOptions>()!;
    return new AmazonS3Client(opts.AccessKey, opts.SecretKey, new AmazonS3Config
    {
        ServiceURL = $"http://{opts.Endpoint}",
        ForcePathStyle = true,
    });
});

// Core services
builder.Services.AddSingleton<ISongRepository, SongRepository>();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<QueueManager>();
builder.Services.AddSingleton<IQueueManager>(sp => sp.GetRequiredService<QueueManager>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<QueueManager>());
builder.Services.AddSingleton<StreamServer>();
builder.Services.AddSingleton<IStreamServer>(sp => sp.GetRequiredService<StreamServer>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<StreamServer>());
builder.Services.AddSingleton<SseHub>();
builder.Services.AddHostedService<SseBroadcaster>();

var app = builder.Build();

// Ensure MinIO bucket exists on startup
using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<ISongRepository>();
    try { await repo.EnsureSchemaAsync(); }
    catch (Exception ex)
    {
        var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        log.LogWarning(ex, "EnsureSchemaAsync failed — MinIO may not be ready yet");
    }
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();

app.Run();

public partial class Program { }
