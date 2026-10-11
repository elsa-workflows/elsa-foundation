using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells;
using CShells.Configuration;
using CShells.Features;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.Extensions.Configuration;

namespace Elsa.Modularity.EntityFramework;

/// <summary>Refuses legacy feature-editor writes that touch resource-managed EF consumers.</summary>
/// <remarks>Composes both graphs from metadata; no feature instance or database is opened.</remarks>
public sealed class EfPersistenceActivationContextPreparer(
    IConfiguration rootConfiguration,
    IRuntimeFeatureCatalog featureCatalog,
    IEfToolingShellDefaults hostDefaults) : IFeatureActivationContextPreparer
{
    public async Task<FeatureActivationContext> PrepareAsync(
        FeatureActivationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var sourceToken = rootConfiguration.GetReloadToken();
            var catalog = await featureCatalog.GetSnapshotAsync(cancellationToken);
            var current = Compose(context.Shell.ShellId, context.Shell.Configuration,
                context.Shell.Features.Select(x => new FeatureApplyItem(x.Key, true, x.Value)), catalog);
            var candidate = Compose(context.Shell.ShellId, context.Shell.Configuration,
                context.Request.Features, catalog);
            var selected = current.Participants.Concat(candidate.Participants)
                .Where(x => x.SelectionKind != "Legacy")
                .Select(x => x.FeatureId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (sourceToken.HasChanged || featureCatalog.CurrentSnapshot.Generation != catalog.Generation)
                throw Refused("[resource-managed-configuration] Effective configuration changed during validation. Edit authored configuration and reload the shell.");

            if (selected.Length > 0)
                throw new FeatureActivationRefusedException(selected.Select(featureId =>
                    new FeatureActivationRefusal(featureId,
                        $"[resource-managed-configuration] Feature '{JsonEncodedText.Encode(featureId)}' uses resource-managed persistence. Edit authored configuration and reload the shell."))
                    .ToArray());

            return context;
        }
        catch (FeatureActivationRefusedException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Composer, graph and source errors can contain secret-bearing authored data. The editor
            // must fail closed without reflecting the underlying exception into its 409 response.
            throw Refused("[resource-managed-configuration] Effective persistence configuration could not be verified. Edit authored configuration and reload the shell.");
        }
    }

    private EfPersistencePreparationResult Compose(
        string shellId,
        JsonElement shellConfiguration,
        IEnumerable<FeatureApplyItem> features,
        RuntimeFeatureCatalogSnapshot catalog)
    {
        var shell = new JsonObject
        {
            ["Configuration"] = shellConfiguration.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(shellConfiguration.GetRawText())
                : new JsonObject()
        };
        var entries = new JsonObject();
        foreach (var feature in features)
            entries[feature.Id] = feature.Enabled
                ? JsonNode.Parse(feature.Configuration.GetRawText())
                : JsonValue.Create(false);
        shell["Features"] = entries;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new JsonObject { ["Shell"] = shell }.ToJsonString()));
        var authored = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var builder = new ShellBuilder(shellId);
        hostDefaults.Configure(builder, rootConfiguration);
        builder.FromConfiguration(authored.GetSection("Shell"));
        var settings = builder.Build();
        return EfPersistencePreparation.Prepare(settings, catalog.FeatureMap, rootConfiguration);
    }

    private static FeatureActivationRefusedException Refused(string reason) =>
        new([new FeatureActivationRefusal("Persistence", reason)]);
}
