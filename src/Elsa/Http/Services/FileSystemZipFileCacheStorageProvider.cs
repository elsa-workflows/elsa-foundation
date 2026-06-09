using Elsa.Http.Core.Contracts;
using Elsa.Http.Options;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Options;

namespace Elsa.Http.Services;

internal sealed class FileSystemZipFileCacheStorageProvider(IOptions<HttpZipFileCacheOptions> options, IPayloadSerializer payloadSerializer) : IZipFileCacheStorageProviders
{
    private FileSystemZipFileCacheStorage? storage;

    public IZipFileCacheStorage GetStorage()
    {
        storage ??= new FileSystemZipFileCacheStorage(options, payloadSerializer);
        return storage;
    }
}