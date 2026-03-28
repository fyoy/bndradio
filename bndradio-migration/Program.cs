using Amazon.S3;
using Amazon.S3.Model;
using BndRadio;
using Npgsql;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var endpoint   = config["MINIO_ENDPOINT"]   ?? "localhost:9000";
var accessKey  = config["MINIO_ACCESS_KEY"]  ?? "minioadmin";
var secretKey  = config["MINIO_SECRET_KEY"]  ?? "minioadmin";
var bucketName = config["MINIO_BUCKET_NAME"] ?? "audio";
var connStr    = config["DATABASE_URL"]
    ?? config.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

var s3Config = new AmazonS3Config { ServiceURL = $"http://{endpoint}", ForcePathStyle = true };
using var s3 = new AmazonS3Client(accessKey, secretKey, s3Config);
await using var dataSource = new NpgsqlDataSourceBuilder(connStr).Build();

Console.WriteLine("bndradio-migration: starting");
Console.WriteLine($"  MinIO: {endpoint}, bucket: {bucketName}");

// Check if audio_data column exists; if not, migration was already applied
bool columnExists;
await using (var checkConn = await dataSource.OpenConnectionAsync())
{
    await using var checkCmd = checkConn.CreateCommand();
    checkCmd.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='songs' AND column_name='audio_data')";
    columnExists = (bool)(await checkCmd.ExecuteScalarAsync())!;
}

if (!columnExists)
{
    Console.WriteLine("audio_data column does not exist — migration already applied.");
    return;
}

// Query all songs with audio_data (req 7.1)
var songs = new List<(Guid Id, byte[] AudioData)>();
await using (var conn = await dataSource.OpenConnectionAsync())
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, audio_data FROM songs WHERE audio_data IS NOT NULL";
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var id = reader.GetGuid(0);
        var audioData = (byte[])reader[1];
        songs.Add((id, audioData));
    }
}

Console.WriteLine($"Found {songs.Count} songs to migrate.");

int succeeded = 0, skipped = 0, failed = 0;

foreach (var (id, audioData) in songs)
{
    var key = $"songs/{id}";
    try
    {
        // Check if already migrated via HEAD request (req 7.3)
        try
        {
            await s3.GetObjectMetadataAsync(bucketName, key);
            Console.WriteLine($"  SKIP {id} — already in MinIO");
            skipped++;
            continue;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Not found — proceed with upload
        }

        // Upload to MinIO (req 7.2)
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName  = bucketName,
            Key         = key,
            InputStream = new MemoryStream(audioData),
            ContentType = "application/octet-stream",
        });

        Console.WriteLine($"  OK   {id} — uploaded {audioData.Length} bytes");
        succeeded++;
    }
    catch (Exception ex)
    {
        // Log and continue on per-song failure (req 7.5)
        Console.Error.WriteLine($"  FAIL {id} — {ex.Message}");
        failed++;
    }
}

Console.WriteLine($"Migration complete: {succeeded} uploaded, {skipped} skipped, {failed} failed.");

// Drop audio_data column only when all uploads succeeded (req 7.4)
if (failed == 0)
{
    await using var conn = await dataSource.OpenConnectionAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "ALTER TABLE songs DROP COLUMN IF EXISTS audio_data";
    await cmd.ExecuteNonQueryAsync();
    Console.WriteLine("Dropped audio_data column.");
}
else
{
    Console.WriteLine($"Skipping column drop — {failed} upload(s) failed. Re-run after fixing failures.");
}
