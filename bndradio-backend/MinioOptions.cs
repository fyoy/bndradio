namespace BndRadio;

public class MinioOptions
{
    public string Endpoint   { get; set; } = "localhost:9000";
    public string AccessKey  { get; set; } = "minioadmin";
    public string SecretKey  { get; set; } = "minioadmin";
    public string BucketName { get; set; } = "audio";
}
