using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Publishing-owned typed compiler for one stable Design provider key and manifest schema.</summary>
public interface IActivityTemplateProviderCompiler
{
    string ProviderKey { get; }
    /// <summary>
    /// Stable identity of the compiler implementation whose behavior contributes to the executable
    /// template. Publishing, rather than an API caller, supplies this authoritative value to compilation.
    /// </summary>
    string CompilerFingerprint { get; }
    IReadOnlySet<string> SupportedManifestSchemas { get; }

    ValueTask<ActivityTemplateCompilation> CompileAsync(
        ActivityTemplateCompilationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IActivityTemplateProviderCompilerRegistry
{
    void Add(IActivityTemplateProviderCompiler compiler);
    IActivityTemplateProviderCompiler Resolve(string providerKey, string manifestSchemaVersion);
    bool TryResolve(string providerKey, string manifestSchemaVersion, out IActivityTemplateProviderCompiler? compiler);
}

public interface IActivityTemplateDependencyDiscoverer
{
    string ProviderKey { get; }
    IReadOnlySet<string> SupportedManifestSchemas { get; }

    ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(
        ActivityTemplateDependencyDiscoveryRequest request,
        CancellationToken cancellationToken = default);
}

public interface IActivityTemplateDependencyDiscovererRegistry
{
    void Add(IActivityTemplateDependencyDiscoverer discoverer);
    IActivityTemplateDependencyDiscoverer Resolve(string providerKey, string manifestSchemaVersion);
}
