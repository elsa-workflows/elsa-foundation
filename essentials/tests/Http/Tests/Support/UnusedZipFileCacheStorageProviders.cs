using Elsa.Http.Core.Contracts;

namespace Elsa.Http.Tests.Support;

/// <summary>
/// An <see cref="IZipFileCacheStorageProviders"/> that fails the test if the code under test
/// touches cache storage — for scenarios where the cache path must not be exercised.
/// </summary>
public sealed class UnusedZipFileCacheStorageProviders : IZipFileCacheStorageProviders
{
    public IZipFileCacheStorage GetStorage() => throw new NotSupportedException("Cache storage should not be used in this scenario.");
}
