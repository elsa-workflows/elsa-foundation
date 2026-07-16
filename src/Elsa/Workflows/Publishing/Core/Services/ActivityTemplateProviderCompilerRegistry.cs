using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Services;

public sealed class ActivityTemplateProviderCompilerRegistry : IActivityTemplateProviderCompilerRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string ProviderKey, string Schema), IActivityTemplateProviderCompiler> _compilers = new();

    public ActivityTemplateProviderCompilerRegistry(IEnumerable<IActivityTemplateProviderCompiler>? compilers = null)
    {
        AddAll(compilers ?? []);
    }

    public void Add(IActivityTemplateProviderCompiler compiler) => AddAll([compiler]);

    private void AddAll(IEnumerable<IActivityTemplateProviderCompiler> compilers)
    {
        ArgumentNullException.ThrowIfNull(compilers);
        var additions = compilers.Select(compiler =>
        {
            ArgumentNullException.ThrowIfNull(compiler);
            var claims = compiler.SupportedManifestSchemas
                .Select(schema => (compiler.ProviderKey, Schema: schema))
                .ToArray();
            if (claims.Length == 0 || claims.Any(x => string.IsNullOrWhiteSpace(x.ProviderKey) || string.IsNullOrWhiteSpace(x.Schema)) ||
                string.IsNullOrWhiteSpace(compiler.CompilerFingerprint))
                throw new InvalidOperationException("An activity template compiler must claim a non-empty provider key, compiler fingerprint, and at least one non-empty schema.");
            if (claims.Distinct().Count() != claims.Length)
                throw new InvalidOperationException($"Activity template compiler '{compiler.ProviderKey}' contains duplicate schema claims.");
            return (Compiler: compiler, Claims: claims);
        }).ToArray();

        lock (_lock)
        {
            var claimed = _compilers.Keys.ToHashSet();
            foreach (var addition in additions)
                foreach (var claim in addition.Claims)
                    if (!claimed.Add(claim))
                        throw new InvalidOperationException($"An activity template compiler already owns provider '{claim.ProviderKey}' schema '{claim.Schema}'.");

            foreach (var addition in additions)
            {
                var guarded = new GuardedCompiler(addition.Compiler);
                foreach (var key in addition.Claims)
                    _compilers.Add(key, guarded);
            }
        }
    }

    public IActivityTemplateProviderCompiler Resolve(string providerKey, string manifestSchemaVersion) =>
        TryResolve(providerKey, manifestSchemaVersion, out var compiler)
            ? compiler!
            : throw new InvalidOperationException($"No activity template compiler owns provider '{providerKey}' schema '{manifestSchemaVersion}'.");

    public bool TryResolve(string providerKey, string manifestSchemaVersion, out IActivityTemplateProviderCompiler? compiler)
    {
        lock (_lock)
            return _compilers.TryGetValue((providerKey, manifestSchemaVersion), out compiler);
    }

    private sealed class GuardedCompiler(IActivityTemplateProviderCompiler inner) : IActivityTemplateProviderCompiler
    {
        public string ProviderKey => inner.ProviderKey;
        public string CompilerFingerprint => inner.CompilerFingerprint;
        public IReadOnlySet<string> SupportedManifestSchemas => inner.SupportedManifestSchemas;

        public async ValueTask<ActivityTemplateCompilation> CompileAsync(
            ActivityTemplateCompilationRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await inner.CompileAsync(request, cancellationToken);
                return result with { Diagnostics = ActivityProviderDiagnosticSanitizer.Sanitize(result.Diagnostics, ProviderKey) };
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new(
                    null,
                    new Dictionary<string, Elsa.Workflows.Runtime.Core.Models.WorkflowExecutableResumeTarget>(StringComparer.Ordinal),
                    [],
                    [],
                    [],
                    new(0, 0, 0, 0, 0, 0, 0),
                    request.ProviderFingerprint,
                    [],
                    [new(
                        "activity.provider.failure",
                        ActivityDiagnosticSeverity.Error,
                        $"Activity provider '{ProviderKey}' failed during 'compile'.",
                        new("ActivityDraft", request.DraftId, request.DefinitionId, Revision: request.Revision),
                        new(ProviderKey),
                        "Retry the operation or inspect provider logs using the request trace identifier.",
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["operation"] = "compile",
                            ["schemaVersion"] = request.Provider.SchemaVersion
                        })]);
            }
        }
    }
}
