using Elsa.Http.Core.Contracts;
using Elsa.Http.Options;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Options;

namespace Elsa.Http.Services;

public sealed class FileSystemZipFileCacheStorageProvider : IZipFileCacheStorageProviders
{
    // Use Lazy<T> (thread-safe by default) instead of a non-atomic '??=' check-then-act: the storage
    // instance owns the mutual-exclusion locks, so two concurrently constructed instances would each
    // hold independent locks and defeat the locking.
    private readonly Lazy<FileSystemZipFileCacheStorage> storage;

    /// <summary>
    /// Initializes the provider, constructing the backing storage from the supplied options and serializer.
    /// </summary>
    public FileSystemZipFileCacheStorageProvider(IOptions<HttpZipFileCacheOptions> options, IPayloadSerializer payloadSerializer)
        : this(() => new FileSystemZipFileCacheStorage(options, payloadSerializer))
    {
    }

    /// <summary>
    /// Initializes the provider with an explicit storage factory. The factory is invoked at most once,
    /// even under concurrent <see cref="GetStorage"/> calls. Primarily useful for testing the single-
    /// construction guarantee.
    /// </summary>
    public FileSystemZipFileCacheStorageProvider(Func<FileSystemZipFileCacheStorage> storageFactory)
    {
        storage = new(storageFactory);
    }

    public IZipFileCacheStorage GetStorage() => storage.Value;
}
