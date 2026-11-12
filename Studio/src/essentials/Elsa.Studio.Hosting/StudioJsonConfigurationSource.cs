using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace Elsa.Studio.Hosting;

// Absolute AddJsonFile paths synthesize a PhysicalFileProvider which the standard
// JsonConfigurationProvider does not dispose. Keep that resource in the provider's
// lifetime, including ConfigurationManager source rebuilds and failed initial loads.
internal sealed class StudioJsonConfigurationSource : JsonConfigurationSource
{
    private readonly string _absolutePath;

    public StudioJsonConfigurationSource(string absolutePath, bool optional)
    {
        _absolutePath = absolutePath;
        Path = absolutePath;
        Optional = optional;
        ReloadOnChange = true;
    }

    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        // ConfigurationManager may rebuild the same source before disposing its old
        // provider. Each generation owns a distinct file provider.
        var source = new StudioJsonConfigurationSource(_absolutePath, Optional)
        {
            ReloadOnChange = ReloadOnChange,
            ReloadDelay = ReloadDelay,
            OnLoadException = OnLoadException
        };
        source.ResolveFileProvider();
        var files = (PhysicalFileProvider)source.FileProvider!;
        try
        {
            source.EnsureDefaults(builder);
            return new StudioJsonConfigurationProvider(source, files);
        }
        catch
        {
            files.Dispose();
            throw;
        }
    }
}

internal sealed class StudioJsonConfigurationProvider(StudioJsonConfigurationSource source, PhysicalFileProvider files)
    : JsonConfigurationProvider(source)
{
    private bool _initialLoadCompleted;

    public override void Load()
    {
        try
        {
            base.Load();
            _initialLoadCompleted = true;
        }
        catch
        {
            // ConfigurationManager cannot retain/dispose a provider whose first Load
            // failed. Later reload failures must preserve its existing watch/recovery.
            if (!_initialLoadCompleted) Dispose();
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        try { base.Dispose(disposing); }
        finally { if (disposing) files.Dispose(); }
    }
}
