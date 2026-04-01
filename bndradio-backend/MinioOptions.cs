// Configuration POCO for MinIO (S3-compatible object storage).
// Bound from the "MinIO" section in appsettings.json.
// Environment variable overrides: MINIO_ENDPOINT, MINIO_ACCESS_KEY, MINIO_SECRET_KEY, MINIO_BUCKET_NAME.
namespace BndRadio;

public class MinioOptions
{
    public string Endpoint   { get; set; } = "localhost:23900"; // host:port of the MinIO API
    public string AccessKey  { get; set; } = "minioadmin";
    public string SecretKey  { get; set; } = "minioadmin";
    public string BucketName { get; set; } = "audio";           // bucket that holds all audio objects
}
