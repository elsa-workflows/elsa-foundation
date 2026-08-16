using CShells;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Options;
using Elsa.Http.Services;
using Elsa.Primitives.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    private readonly ShellFeatureContext _context;

    public HttpFeature(ShellFeatureContext context)
    {
        _context = context;
    }

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
        // Register in the child shell, after CShells has composed root endpoint sources into that shell. This
        // keeps Foundation Host independent of Elsa.Http while allowing the adapter to see the real manifest.
        services.TryAddScoped<IHttpRouteManifestProvider>(serviceProvider =>
        {
            var shellSettings = serviceProvider.GetService<ShellSettings>();
            return shellSettings is null
                ? new EmptyHttpRouteManifestProvider()
                : new AspNetCoreHttpRouteManifestProvider(
                    serviceProvider.GetServices<EndpointDataSource>(),
                    shellSettings);
        });

        RegisterType<IContentTypeProvider>(services, ContentTypeProviderType);
        RegisterType<IZipFileCacheStorageProviders>(services, ZipFileCacheProviderType);
        RegisterType<IZipArchiveManager>(services, ZipArchiveManagerType);

        RegisterFileDownloader(services);

        services.Configure<HttpZipFileCacheOptions>(o =>
        {
            o.TimeToLive = CacheTtl;
            o.LocalCacheDirectory = LocalCacheDirectory;
        });

        // Namespace the per-shell route table's cache key with the shell settings id (issue #592 item 5) so its
        // isolation survives a future root-promoted, shell-shared IMemoryCache.
        var shellDiscriminator = _context.Settings.Id.ToString();
        services.Configure<RouteTableOptions>(o => o.ShellDiscriminator = shellDiscriminator);

        services
            .AddHttpContextAccessor()
            .AddScoped<IRouteTable, RouteTable>()
            .AddSingleton<IRouteMatcher, RouteMatcher>()

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

        // Request-side body parser (spec 089 sub-unit C, research D6): stateless content-type dispatch
        // yielding wire-safe JsonElement. Replaceable via TryAdd — register a custom impl before this feature.
        services.TryAddSingleton<IHttpRequestBodyParser, HttpRequestBodyParser>();
    }

    private void RegisterFileDownloader(IServiceCollection services)
    {
        RegisterType<IFileDownloader>(services, FileDownloaderType);

        // Add HttpClient specifically to the FileDownloader implementation. AddHttpClient has many overloads, so a
        // plain GetMethod(name) is ambiguous — select the two-generic-argument overload that takes just the service
        // collection: AddHttpClient<TClient, TImplementation>(IServiceCollection).
        var type = FileDownloaderType.GetLoadedType();
        var methodName = nameof(HttpClientFactoryServiceCollectionExtensions.AddHttpClient);
        var method = typeof(HttpClientFactoryServiceCollectionExtensions)
                         .GetMethods(BindingFlags.Static | BindingFlags.Public)
                         .SingleOrDefault(m =>
                             m.Name == methodName &&
                             m.IsGenericMethodDefinition &&
                             m.GetGenericArguments().Length == 2 &&
                             m.GetParameters() is [{ ParameterType.IsGenericType: false } p] && p.ParameterType == typeof(IServiceCollection))
                     ?? throw new InvalidOperationException($"Could not find method '{methodName}'");

        var genericMethod = method.MakeGenericMethod(typeof(IFileDownloader), type);
        genericMethod.Invoke(null, [services]);
    }

    private static void RegisterType<TService>(IServiceCollection services, string typeName)
    {
        var type = typeName.GetLoadedType();
        services.AddScoped(typeof(TService), type);
    }

    private sealed class EmptyHttpRouteManifestProvider : IHttpRouteManifestProvider
    {
        public IEnumerable<HttpRouteData> GetRoutes() => [];
    }
}
