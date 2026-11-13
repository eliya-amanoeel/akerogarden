using System.Collections.Concurrent;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Integration.Infrastructure;

/// <summary>
/// Test double for <see cref="IImageStorage"/>. Records uploads and — the point of it — every
/// <b>deletion</b>, so a test can assert that a superseded image is actually cleaned up
/// rather than orphaned in storage.
/// </summary>
public sealed class FakeImageStorage : IImageStorage
{
    /// <summary>Blob names currently "stored".</summary>
    public ConcurrentDictionary<string, byte[]> Stored { get; } = new();

    /// <summary>Blob names passed to <see cref="DeleteImageAsync"/>, in order.</summary>
    public ConcurrentQueue<string> Deleted { get; } = new();

    /// <summary>When set, deletion throws — for proving a storage failure cannot fail a saved edit.</summary>
    public Exception? DeleteThrows { get; set; }

    public Task<string> UploadImageAsync(
        Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        // Same shape the real implementation produces, so ImagePath accepts it.
        var name = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}".ToLowerInvariant();
        Stored[name] = [];
        return Task.FromResult(name);
    }

    public Task<StoredImage?> OpenImageAsync(
        string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Stored.ContainsKey(name)
            ? new StoredImage(new MemoryStream([]), "image/jpeg", "\"etag\"")
            : null);

    public Task<bool> DeleteImageAsync(
        string name, CancellationToken cancellationToken = default)
    {
        if (DeleteThrows is not null)
            throw DeleteThrows;

        Deleted.Enqueue(name);
        return Task.FromResult(Stored.TryRemove(name, out _));
    }
}
