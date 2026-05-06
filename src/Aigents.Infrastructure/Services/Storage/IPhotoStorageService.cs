namespace Aigents.Infrastructure.Services.Storage;

public interface IPhotoStorageService
{
    /// <summary>
    /// Stores a photo and returns the object key (not a full URL — callers build URLs from key + public base).
    /// </summary>
    Task<string> StoreAsync(string listingId, string fileName, Stream data, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Returns a public URL for a stored photo key.
    /// </summary>
    string GetPublicUrl(string objectKey);

    Task DeleteAsync(string objectKey, CancellationToken ct = default);
}
