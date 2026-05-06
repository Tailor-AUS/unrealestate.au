using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aigents.Infrastructure.Services.Storage;

public static class PhotoStorageServiceExtensions
{
    public static IServiceCollection AddPhotoStorage(this IServiceCollection services, IConfiguration config)
    {
        var endpoint = config["Storage:Endpoint"] ?? "http://minio:9000";
        var accessKey = config["Storage:AccessKey"] ?? "minioadmin";
        var secretKey = config["Storage:SecretKey"] ?? "minioadmin";
        var forcePathStyle = bool.Parse(config["Storage:ForcePathStyle"] ?? "true");

        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            accessKey,
            secretKey,
            new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = forcePathStyle,
            }));

        services.AddScoped<IPhotoStorageService, MinioPhotoStorageService>();
        return services;
    }
}
