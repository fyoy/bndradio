using Amazon.S3;
using Npgsql;
using BndRadio;
using BndRadio.Interfaces;
using BndRadio.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var corsOrigins = (Environment.GetEnvironmentVariable("CORS_ORIGINS") ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("ViteDev", policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromSeconds(60),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
    options.RejectionStatusCode = 429;
});

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["Admin:JwtSecret"];

if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException("JWT_SECRET is not configured.");

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

// Map MINIO_* env vars to MinIO:* config keys (single-underscore convention)
builder.Configuration.AddEnvironmentVariables();
var minioEnvMap = new Dictionary<string, string?>
{
    ["MINIO_ENDPOINT"]    = builder.Configuration["MINIO_ENDPOINT"],
    ["MINIO_ACCESS_KEY"]  = builder.Configuration["MINIO_ACCESS_KEY"],
    ["MINIO_SECRET_KEY"]  = builder.Configuration["MINIO_SECRET_KEY"],
    ["MINIO_BUCKET_NAME"] = builder.Configuration["MINIO_BUCKET_NAME"],
};
if (minioEnvMap["MINIO_ENDPOINT"]    != null) builder.Configuration["MinIO:Endpoint"]   = minioEnvMap["MINIO_ENDPOINT"];
if (minioEnvMap["MINIO_ACCESS_KEY"]  != null) builder.Configuration["MinIO:AccessKey"]  = minioEnvMap["MINIO_ACCESS_KEY"];
if (minioEnvMap["MINIO_SECRET_KEY"]  != null) builder.Configuration["MinIO:SecretKey"]  = minioEnvMap["MINIO_SECRET_KEY"];
if (minioEnvMap["MINIO_BUCKET_NAME"] != null) builder.Configuration["MinIO:BucketName"] = minioEnvMap["MINIO_BUCKET_NAME"];

builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection("MinIO"));

var minioOpts = builder.Configuration.GetSection("MinIO").Get<MinioOptions>() ?? new MinioOptions();
if (string.IsNullOrWhiteSpace(minioOpts.Endpoint))
    throw new InvalidOperationException("MinIO configuration is missing: Endpoint (MINIO_ENDPOINT)");
if (string.IsNullOrWhiteSpace(minioOpts.AccessKey))
    throw new InvalidOperationException("MinIO configuration is missing: AccessKey (MINIO_ACCESS_KEY)");
if (string.IsNullOrWhiteSpace(minioOpts.SecretKey))
    throw new InvalidOperationException("MinIO configuration is missing: SecretKey (MINIO_SECRET_KEY)");

builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var opts = builder.Configuration.GetSection("MinIO").Get<MinioOptions>() ?? new MinioOptions();
    var config = new AmazonS3Config
    {
        ServiceURL = $"http://{opts.Endpoint}",
        ForcePathStyle = true,
    };
    return new AmazonS3Client(opts.AccessKey, opts.SecretKey, config);
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());

builder.Services.AddSingleton<RedisCacheService>();

builder.Services.AddSingleton<SseHub>();
builder.Services.AddHostedService<SseBroadcaster>();

builder.Services.AddSingleton<ISongRepository, SongRepository>();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<QueueManager>();
builder.Services.AddSingleton<IQueueManager>(sp => sp.GetRequiredService<QueueManager>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<QueueManager>());
builder.Services.AddSingleton<StreamServer>();
builder.Services.AddSingleton<IStreamServer>(sp => sp.GetRequiredService<StreamServer>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<StreamServer>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<ISongRepository>();
    try
    {
        await repo.EnsureSchemaAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "EnsureSchemaAsync failed — continuing startup (external services may be unavailable)");
    }
}

app.UseCors("ViteDev");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();

app.Run();

public partial class Program { }
