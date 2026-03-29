namespace BndRadio;

public class MinioOptions
{
    public string Endpoint   { get; set; } = "localhost:23900";
    public string AccessKey  { get; set; } = "minioadmin";
    public string SecretKey  { get; set; } = "minioadmin";
    public string BucketName { get; set; } = "audio";
}
