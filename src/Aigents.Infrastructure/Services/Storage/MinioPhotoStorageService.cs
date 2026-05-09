using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aigents.Infrastructure.Services.Storage;

public class MinioPhotoStorageService : IPhotoStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly string _publicBase;
    private readonly ILogger<MinioPhotoStorageService> _log;

    public MinioPhotoStorageService(IAmazonS3 s3, IConfiguration config, ILogger<MinioPhotoStorageService> log)
    {
        _s3 = s3;
        _bucket = config["Storage:Bucket"] ?? "unrealestate-photos";
        // Public base is the MinIO endpoint + bucket — path-style, public-read bucket.
        var endpoint = (config["Storage:Endpoint"] ?? "http://minio:9000").TrimEnd('/');
        _publicBase = $"{endpoint}/{_bucket}";
        _log = log;
    }

    public async Task<string> StoreAsync(string listingId, string fileName, Stream data, string contentType, CancellationToken ct = default)
    {
        var key = $"listings/{listingId}/{Guid.NewGuid():N}_{fileName}";
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = data,
            ContentType = contentType,
            CannedACL = S3CannedACL.PublicRead,
        }, ct);
        _log.LogInformation("Stored photo {Key}", key);
        return key;
    }

    public string GetPublicUrl(string objectKey) => $"{_publicBase}/{objectKey}";

    public async Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        await _s3.DeleteObjectAsync(_bucket, objectKey, ct);
        _log.LogInformation("Deleted photo {Key}", objectKey);
    }
}
