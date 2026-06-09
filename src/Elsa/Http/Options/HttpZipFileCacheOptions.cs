namespace Elsa.Http.Options
{
    public sealed class HttpZipFileCacheOptions
    {
        /// <summary>
        /// The time to live for cached files.
        /// </summary>
        public TimeSpan TimeToLive { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// The local cache directory. Defaults to the system's temp directory.
        /// </summary>
        public string LocalCacheDirectory { get; set; } = Path.GetTempPath();
    }
}
