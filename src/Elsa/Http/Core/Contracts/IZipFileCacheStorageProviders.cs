namespace Elsa.Http.Core.Contracts
{
    /// <summary>
    /// Creates a file cache storage instance.
    /// </summary>
    public interface IZipFileCacheStorageProviders
    {
        /// <summary>
        /// Gets (or creates and caches) the storage.
        /// </summary>
        IZipFileCacheStorage GetStorage();
    }
}
