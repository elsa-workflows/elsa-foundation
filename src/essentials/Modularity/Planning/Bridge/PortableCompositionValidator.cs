using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Bridge;

/// <summary>Correlates portable artifacts and enforces the public authored-content boundary.</summary>
public static class PortableCompositionValidator
{
    public static PortableInputReceipt CreateInputReceipt(
        PortableComposition envelope,
        ReadOnlySpan<byte> exactEnvelopeBytes,
        SourceSnapshot frozenInput)
    {
        _ = ReadAndMatchEnvelope(envelope, exactEnvelopeBytes);
        var context = ValidateSnapshotContext(frozenInput);
        var files = CaptureInventory(frozenInput);
        ValidateCompleteInventory(context, files);
        var receipt = new PortableInputReceipt(
            PortableCompositionJson.SchemaVersion,
            PortableCompositionJson.InputReceiptKind,
            envelope.RequiredInput.Id,
            envelope.RequiredInput.Revision,
            Hash(exactEnvelopeBytes),
            context,
            files);
        PortableCompositionJson.ValidateInputReceipt(receipt);
        return receipt;
    }

    public static PortableCandidateReceipt CreateCandidateReceipt(
        PortableComposition envelope,
        ReadOnlySpan<byte> exactEnvelopeBytes,
        SourceSnapshot generatedSnapshot,
        string candidateId)
    {
        _ = ReadAndMatchEnvelope(envelope, exactEnvelopeBytes);
        _ = PortableCompositionJson.RequireVersion4Guid(candidateId);
        var context = ValidateSnapshotContext(generatedSnapshot);
        var files = CaptureInventory(generatedSnapshot);
        ValidateCompleteInventory(context, files);
        var receipt = new PortableCandidateReceipt(
            PortableCompositionJson.SchemaVersion,
            PortableCompositionJson.CandidateReceiptKind,
            candidateId,
            envelope.RequiredInput.Id,
            envelope.RequiredInput.Revision,
            Hash(exactEnvelopeBytes),
            context,
            envelope.InputDisposition,
            files);
        PortableCompositionJson.ValidateCandidateReceipt(receipt);
        return receipt;
    }

    /// <summary>Checks public bytes against a receipt when the original private bundle is unavailable.</summary>
    public static void ValidateInputAssociation(
        PortableComposition envelope,
        ReadOnlySpan<byte> exactEnvelopeBytes,
        PortableInputReceipt receipt)
    {
        _ = ReadAndMatchEnvelope(envelope, exactEnvelopeBytes);
        PortableCompositionJson.ValidateInputReceipt(receipt);
        ValidateCompleteInventory(receipt.Context, receipt.Files);
        if (receipt.InputId != envelope.RequiredInput.Id ||
            receipt.InputRevision != envelope.RequiredInput.Revision ||
            receipt.PublicEnvelopeSha256 != Hash(exactEnvelopeBytes))
            throw Mismatch();
    }

    /// <summary>Checks a complete frozen private bundle and its reviewed public projection.</summary>
    public static void ValidateInput(
        PortableComposition envelope,
        ReadOnlySpan<byte> exactEnvelopeBytes,
        PortableInputReceipt receipt,
        SourceSnapshot frozenInput,
        SettingReviewDocument? settingReview = null)
    {
        var authored = ReadAndMatchEnvelope(envelope, exactEnvelopeBytes);
        PortableCompositionJson.ValidateInputReceipt(receipt);
        if (receipt.InputId != envelope.RequiredInput.Id ||
            receipt.InputRevision != envelope.RequiredInput.Revision ||
            receipt.PublicEnvelopeSha256 != Hash(exactEnvelopeBytes))
            throw Mismatch();
        ValidateSnapshot(receipt.Context, receipt.Files, frozenInput);
        ValidatePublicContent(authored, settingReview);
    }

    /// <summary>Checks public safety and the old association before an explicit complete-input rebind.</summary>
    public static void ValidateInputForRebind(
        PortableComposition envelope,
        ReadOnlySpan<byte> exactEnvelopeBytes,
        PortableInputReceipt previousReceipt,
        SettingReviewDocument? settingReview = null)
    {
        ValidateInputAssociation(envelope, exactEnvelopeBytes, previousReceipt);
        ValidatePublicContent(ReadAndMatchEnvelope(envelope, exactEnvelopeBytes), settingReview);
    }

    /// <summary>Checks a complete replacement under the prior receipt's fixed context without matching its old hashes.</summary>
    public static void ValidateRebindReplacementContext(
        PortableInputReceipt previousReceipt,
        SourceSnapshot replacementSnapshot)
    {
        PortableCompositionJson.ValidateInputReceipt(previousReceipt);
        ValidateCompleteInventory(previousReceipt.Context, previousReceipt.Files);
        var replacementContext = ValidateSnapshotContext(replacementSnapshot);
        if (replacementContext != previousReceipt.Context)
            throw Mismatch();
        ValidateCompleteInventory(replacementContext, CaptureInventory(replacementSnapshot));
    }

    /// <summary>Validates an inspection candidate from its receipt and generated bytes alone.</summary>
    public static void ValidateCandidate(
        PortableComposition envelope,
        ReadOnlySpan<byte> exactEnvelopeBytes,
        PortableCandidateReceipt receipt,
        SourceSnapshot generatedSnapshot)
    {
        _ = ReadAndMatchEnvelope(envelope, exactEnvelopeBytes);
        PortableCompositionJson.ValidateCandidateReceipt(receipt);
        ValidateSnapshot(receipt.Context, receipt.Files, generatedSnapshot);
        if (receipt.InputId != envelope.RequiredInput.Id ||
            receipt.InputRevision != envelope.RequiredInput.Revision ||
            receipt.PublicEnvelopeSha256 != Hash(exactEnvelopeBytes) ||
            receipt.InputDisposition != envelope.InputDisposition)
            throw Mismatch();
    }

    /// <summary>Ensures authored settings and resources contain only reviewed shareable intent.</summary>
    public static void ValidatePublicContent(AuthoredComposition composition, SettingReviewDocument? settingReview)
    {
        ArgumentNullException.ThrowIfNull(composition);
        if (composition.Settings is { } settings)
            ValidateSettings(settings, settingReview);
        if (composition.Resources is { } resources)
            ValidateResources(resources);
    }

    private static AuthoredComposition ReadAndMatchEnvelope(
        PortableComposition envelope,
        ReadOnlySpan<byte> exactEnvelopeBytes)
    {
        PortableCompositionJson.ValidateComposition(envelope);
        var parsed = PortableCompositionJson.ParseComposition(exactEnvelopeBytes);
        if (parsed.RequiredInput != envelope.RequiredInput ||
            parsed.InputDisposition != envelope.InputDisposition ||
            !string.Equals(parsed.Composition.GetRawText(), envelope.Composition.GetRawText(), StringComparison.Ordinal))
            throw Invalid();

        try
        {
            return SelectionJsonReader.ParseComposition(parsed.Composition.GetRawText());
        }
        catch (SelectionDocumentException)
        {
            throw Invalid();
        }
    }

    private static void ValidateSettings(JsonElement settings, SettingReviewDocument? settingReview)
    {
        if (settings.ValueKind != JsonValueKind.Object)
            throw Unsafe();

        var featureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in settings.EnumerateObject())
        {
            if (!SelectionValueRules.IsSafeReference(feature.Name) || !featureIds.Add(feature.Name) ||
                feature.Value.ValueKind != JsonValueKind.Object)
                throw Unsafe();

            ValidateSettingValue(feature.Name, [], feature.Value, settingReview, featureRoot: true);
        }
    }

    private static void ValidateSettingValue(
        string featureId,
        ImmutableArray<string> path,
        JsonElement value,
        SettingReviewDocument? settingReview,
        bool featureRoot = false)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            if (properties.Length > 0)
            {
                if (!featureRoot)
                {
                    try
                    {
                        CompositionImporter.RejectPortableParentReview(featureId, path, value, settingReview);
                    }
                    catch (CompositionImportException)
                    {
                        throw Unsafe();
                    }
                }

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in properties)
                {
                    if (!names.Add(property.Name))
                        throw Unsafe();
                    ValidateSettingValue(featureId, path.Add(property.Name), property.Value, settingReview);
                }
                return;
            }
        }

        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0)
            throw Unsafe();

        if (featureRoot)
            return;

        if (CompositionImporter.IsHostOwnedPersistenceField(path))
            throw Unsafe();

        var pointer = CompositionImporter.EncodePointer(path);
        try
        {
            if (settingReview is null)
                throw Unsafe();
            _ = settingReview.RequirePortableValue(featureId, pointer, value.ValueKind);
        }
        catch (SettingReviewException)
        {
            throw Unsafe();
        }
    }

    private static void ValidateResources(JsonElement resources)
    {
        if (resources.ValueKind != JsonValueKind.Object ||
            resources.EnumerateObject().Count() != 1 || !resources.TryGetProperty("persistence", out var persistence) ||
            persistence.ValueKind != JsonValueKind.Object)
            throw Unsafe();

        var properties = persistence.EnumerateObject().ToArray();
        if (properties.Length == 0 || properties.Any(property => property.Name is not ("defaultResource" or "bindings")))
            throw Unsafe();

        if (persistence.TryGetProperty("defaultResource", out var defaultResource) &&
            (defaultResource.ValueKind != JsonValueKind.String ||
             !CompositionImporter.IsLogicalResourceName(defaultResource.GetString()!)))
            throw Unsafe();

        if (persistence.TryGetProperty("bindings", out var bindings))
        {
            if (bindings.ValueKind != JsonValueKind.Object || bindings.EnumerateObject().Count() == 0)
                throw Unsafe();
            var featureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in bindings.EnumerateObject())
            {
                if (!SelectionValueRules.IsSafeReference(binding.Name) || !featureIds.Add(binding.Name) ||
                    binding.Value.ValueKind != JsonValueKind.String ||
                    !CompositionImporter.IsLogicalResourceName(binding.Value.GetString()!))
                    throw Unsafe();
            }
        }
    }

    private static PortableInputContext ValidateSnapshotContext(SourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var selection = snapshot.Selection;
        var context = new PortableInputContext(selection.ShellId, selection.Environment);
        if (!SelectionValueRules.IsSafeReference(context.Shell) ||
            string.IsNullOrWhiteSpace(context.Environment) || context.Environment.Length > 128 ||
            !context.Environment.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            throw Mismatch();
        return context;
    }

    private static void ValidateSnapshot(
        PortableInputContext receiptContext,
        ImmutableArray<PortableFileDigest> receiptFiles,
        SourceSnapshot snapshot)
    {
        var snapshotContext = ValidateSnapshotContext(snapshot);
        if (receiptContext != snapshotContext)
            throw Mismatch();

        var actualFiles = CaptureInventory(snapshot);
        if (!receiptFiles.Select(file => file.Name).SequenceEqual(actualFiles.Select(file => file.Name), StringComparer.Ordinal) ||
            !receiptFiles.SequenceEqual(actualFiles))
            throw Mismatch();

        ValidateCompleteInventory(snapshotContext, actualFiles);
    }

    private static void ValidateCompleteInventory(
        PortableInputContext context,
        ImmutableArray<PortableFileDigest> files)
    {
        var names = files.Select(file => file.Name).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains("shells.json") || !names.Contains("appsettings.json") ||
            !names.Contains($"shells.{context.Environment}.json"))
            throw Mismatch();
    }

    private static ImmutableArray<PortableFileDigest> CaptureInventory(SourceSnapshot snapshot)
    {
        var files = snapshot.FileNames
            .Select(name => new PortableFileDigest(name,
                Convert.ToHexString(SHA256.HashData(snapshot.CopyBytes(name))).ToLowerInvariant()))
            .ToImmutableArray();
        PortableCompositionJson.ValidateInventory(files);
        return files;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static CompositionImportException Invalid() =>
        new("portable-input-invalid", "The portable artifact is invalid.");

    private static CompositionImportException Mismatch() =>
        new("portable-input-mismatch", "The portable artifact does not match the supplied input.");

    private static CompositionImportException Unsafe() =>
        new("bridge-portable-unsafe", "The authored composition contains unsupported or unreviewed public content.");
}
