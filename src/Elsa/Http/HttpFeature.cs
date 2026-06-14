using CShells.Features;
using Elsa.Features.Abstractions;
using Elsa.Features.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Options;
using Elsa.Http.Services;
using Elsa.Primitives.Extensions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Http;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("HTTP")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "Http",
    DisplayName = "HTTP",
    Description = "Provides HTTP content parsing, routing, downloading, and cache services."
)]
public class HttpFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Content type provider type", Description = "CLR type name of the content type provider implementation.", Category = "Services", Advanced = true)]
    public string ContentTypeProviderType { get; set; } = typeof(FileExtensionContentTypeProvider).FullName!;

    [ManifestSetting(DisplayName = "Zip file cache provider type", Description = "CLR type name of the ZIP file cache storage provider implementation.", Category = "Services", Advanced = true)]

    public string ZipFileCacheProviderType { get; set; } = typeof(FileSystemZipFileCacheStorageProvider).FullName!;

    [ManifestSetting(DisplayName = "ZIP archive manager type", Description = "CLR type name of the ZIP archive manager implementation.", Category = "Services", Advanced = true)]

    public string ZipArchiveManagerType { get; set; } = typeof(ZipArchiveManager).FullName!;

    [ManifestSetting(DisplayName = "File downloader type", Description = "CLR type name of the file downloader implementation.", Category = "Services", Advanced = true)]

    public string FileDownloaderType { get; set; } = typeof(HttpClientFileDownloader).FullName!;

    [ManifestSetting(DisplayName = "ZIP cache time to live", Description = "Duration downloaded ZIP files remain in the local cache.", Category = "Caching", UIHint = "duration")]

    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(7);

    [ManifestSetting(DisplayName = "Local cache directory", Description = "Directory used for local HTTP ZIP file caching.", Category = "Caching")]

    public string LocalCacheDirectory { get; set; } = Path.GetTempPath();

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaCapability(
            ElsaCapabilities.Http,
            "HTTP infrastructure",
            "Http",
            "http",
            "infrastructure");

        RegisterType<IContentTypeProvider>(services, ContentTypeProviderType);
        RegisterType<IZipFileCacheStorageProviders>(services, ZipFileCacheProviderType);
        RegisterType<IZipArchiveManager>(services, ZipArchiveManagerType);

        RegisterFileDownloader(services);

        services.Configure<HttpZipFileCacheOptions>(o =>
        {
            o.TimeToLive = CacheTtl;
            o.LocalCacheDirectory = LocalCacheDirectory;
        });

        services
            .AddHttpContextAccessor()
            .AddScoped<IRouteTable, RouteTable>()

            .AddSingleton<IHttpContentParser, JsonHttpContentParser>()
            .AddSingleton<IHttpContentParser, XmlHttpContentParser>()
            .AddSingleton<IHttpContentParser, PlainTextHttpContentParser>()
            .AddSingleton<IHttpContentParser, TextHtmlHttpContentParser>()
            .AddSingleton<IHttpContentParser, FileHttpContentParser>()

            .AddScoped<IHttpContentFactory, TextContentFactory>()
            .AddScoped<IHttpContentFactory, JsonContentFactory>()
            .AddScoped<IHttpContentFactory, XmlContentFactory>()
            .AddScoped<IHttpContentFactory, FormUrlEncodedHttpContentFactory>()

            .AddScoped<IDownloadableContentHandler, BinaryDownloadableContentHandler>()
            .AddScoped<IDownloadableContentHandler, StreamDownloadableContentHandler>()
            .AddScoped<IDownloadableContentHandler, FormFileDownloadableContentHandler>()
            .AddScoped<IDownloadableContentHandler, DownloadableDownloadableContentHandler>()
            .AddScoped<IDownloadableContentHandler, UrlDownloadableContentHandler>()
            .AddScoped<IDownloadableContentHandler, StringDownloadableContentHandler>()
            .AddScoped<IDownloadableContentHandler, HttpFileDownloadableContentHandler>()
            ;

    }

    private void RegisterFileDownloader(IServiceCollection services)
    {
        RegisterType<IFileDownloader>(services, FileDownloaderType);

        // Add HttpClient specifically to the FileDownloader implementation
        var type = FileDownloaderType.GetLoadedType();
        var methodName = nameof(HttpClientFactoryServiceCollectionExtensions.AddHttpClient);
        var method = typeof(HttpClientFactoryServiceCollectionExtensions).GetMethod(methodName, BindingFlags.Static | BindingFlags.Public)
                     ?? throw new InvalidOperationException($"Could not find method '{nameof(HttpClientFactoryServiceCollectionExtensions.AddHttpClient)}'");

        var genericMethod = method.MakeGenericMethod(typeof(IFileDownloader), type);
        genericMethod.Invoke(null, [services]);
    }

    private static void RegisterType<TService>(IServiceCollection services, string typeName)
    {
        var type = typeName.GetLoadedType();
        services.AddScoped(typeof(TService), type);
    }
}
