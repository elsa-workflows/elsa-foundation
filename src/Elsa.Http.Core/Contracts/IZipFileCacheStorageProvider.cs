using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Http.Core.Contracts
{
    /// <summary>
    /// Represents a provider of a file cache storage.
    /// </summary>
    public interface IZipFileCacheStorageProvider
    {
        /// <summary>
        /// Gets the storage.
        /// </summary>
        /// <returns></returns>
        IZipFileCacheStorage GetStorage();
    }
}
