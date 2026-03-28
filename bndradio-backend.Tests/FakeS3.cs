using Amazon.S3;
using Amazon.S3.Model;
using System.Collections.Concurrent;

namespace BndRadio.Tests;

/// <summary>
/// In-memory fake IAmazonS3 for tests that don't need a real MinIO instance.
/// Supports PutObject, GetObject, DeleteObject, ListBuckets, PutBucket.
/// </summary>
public class FakeS3 : AmazonS3Client
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();
    private readonly HashSet<string> _buckets = new();

    // Use a dummy config so the base class doesn't try to connect
    public FakeS3() : base("fake", "fake", new AmazonS3Config
    {
        ServiceURL = "http://localhost:19999",
        ForcePathStyle = true,
    })
    { }

    public override Task<ListBucketsResponse> ListBucketsAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListBucketsResponse
        {
            Buckets = _buckets.Select(b => new S3Bucket { BucketName = b }).ToList()
        };
        return Task.FromResult(response);
    }

    public override Task<PutBucketResponse> PutBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        _buckets.Add(bucketName);
        return Task.FromResult(new PutBucketResponse());
    }

    public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        request.InputStream.CopyTo(ms);
        _store[$"{request.BucketName}/{request.Key}"] = ms.ToArray();
        return Task.FromResult(new PutObjectResponse());
    }

    public override Task<GetObjectResponse> GetObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        var storeKey = $"{bucketName}/{key}";
        if (!_store.TryGetValue(storeKey, out var bytes))
        {
            throw new AmazonS3Exception("NoSuchKey") { ErrorCode = "NoSuchKey" };
        }
        var response = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(bytes),
            BucketName = bucketName,
            Key = key,
        };
        return Task.FromResult(response);
    }

    public override Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove($"{bucketName}/{key}", out _);
        return Task.FromResult(new DeleteObjectResponse());
    }
}
