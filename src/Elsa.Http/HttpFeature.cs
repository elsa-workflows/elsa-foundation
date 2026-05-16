using CShells.Features;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Options;
using Elsa.Http.Services;
using Elsa.Primitives.Extensions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Http
{
    public class HttpFeature : IShellFeature
    {
        public string ContentTypeProviderType { get; set; } = typeof(FileExtensionContentTypeProvider).FullName!;

        public string ZipFileCacheProviderType { get; set; } = typeof(FileSystemZipFileCacheStorageProvider).FullName!;

        public string ZipArchiveManagerType { get; set; } = typeof(ZipArchiveManager).FullName!;

        public string FileDownloaderType { get; set; } = typeof(HttpClientFileDownloader).FullName!;

        public TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(7);
        
        public string LocalCacheDirectory { get; set; } = Path.GetTempPath();

        public void ConfigureServices(IServiceCollection services)
        {
            RegisterType<IContentTypeProvider>(services, ContentTypeProviderType);
            RegisterType<IZipFileCacheStorageProvider>(services, ZipFileCacheProviderType);
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

        void RegisterFileDownloader(IServiceCollection services)
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

        static void RegisterType<TService>(IServiceCollection services, string typeName)
        {
            var type = typeName.GetLoadedType();
            services.AddScoped(typeof(TService), type);
        }
    }
}
