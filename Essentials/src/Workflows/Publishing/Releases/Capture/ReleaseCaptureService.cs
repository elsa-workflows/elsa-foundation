using System.Text;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.Core;

namespace Elsa.Workflows.Publishing.Releases.Capture;

internal sealed class ReleaseCaptureService(IReleaseBundleBuilder builder, IReleaseBundleCodec codec, IReleaseArchiveWriter archives,
    IReleaseArchiveStoreCapabilities archiveCapabilities, IEnumerable<IReleaseCaptureSource> sources,
    IEnumerable<IReleaseSourceMemberContributor>? contributors = null) : IReleaseCaptureService
{
    private const int MaximumSources = 32;
    private readonly ReleaseCaptureContributors _contributors = new(contributors ?? []);
    private readonly IReadOnlyDictionary<string, IReleaseCaptureSource> _sources = Registry(sources);

    public async ValueTask<ReleaseCaptureReceipt> CreateAsync(string captureId, IReadOnlyList<ReleaseCaptureSelection> selections,
        ReleaseSemanticVerificationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections);
        var settings = options ?? new(); var limits = settings.TransportLimits ?? new();
        if (settings.MaxInspectionBytes <= 0 || settings.MaxSites <= 0 || settings.MaxDiagnostics <= 0 ||
            limits.MaxManifestBytes <= 0 || limits.MaxBlobBytes <= 0 || limits.MaxExpandedBytes <= 0 || limits.MaxArchiveBytes <= 0 ||
            limits.MaxEntries <= 0 || limits.MaxMembers <= 0 || limits.MaxEdges <= 0 ||
            limits.MaxJsonDepth is <= 0 or > 256 || limits.MaxDependencyDepth is <= 0 or > 256)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (!Text(captureId) || selections.Count is 0 || selections.Count > limits.MaxMembers)
            throw Refused("invalid_capture_request");
        cancellationToken.ThrowIfCancellationRequested();
        var store = await archiveCapabilities.GetCapabilitiesAsync(cancellationToken);
        if (!store.DurableCapture || !store.SchemaReady) throw Refused("capture_archive_not_durable_or_ready");
        var groups = new Dictionary<IReleaseCaptureSource, List<ReleaseCaptureSelection>>(ReferenceEqualityComparer.Instance);
        var unique = new HashSet<(string, string, ReleaseOwnerVersion?)>(); var selectionCount = 0;
        foreach (var selection in selections)
        {
            if (++selectionCount > limits.MaxMembers || selection is null || !Text(selection.Kind) || !Text(selection.Identity) ||
                selection.Action is not (ReleaseRootAction.ActivateServing or ReleaseRootAction.MakeAvailable) || !Version(selection.Version) ||
                !unique.Add((selection.Kind, selection.Identity, selection.Version))) throw Refused("invalid_capture_request");
            if (!_sources.TryGetValue(selection.Kind, out var source)) throw Refused("unsupported_capture_selection_kind");
            if (!groups.TryGetValue(source, out var group)) groups.Add(source, group = []);
            group.Add(selection);
        }
        if (groups.Count > MaximumSources) throw Refused("capture_source_limit_exceeded");
        var ordered = groups.OrderBy(group => group.Value.Select(item => item.Kind).Order(StringComparer.Ordinal).First(), StringComparer.Ordinal).ToArray();
        var captures = new List<ReleaseSourceCapture>(); ReleaseCaptureReceipt? receipt = null;
        await ExecuteOwnerAsync(0, cancellationToken);
        return receipt ?? throw Refused("capture_archive_not_confirmed");

        async ValueTask ExecuteOwnerAsync(int index, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (index == ordered.Length)
            {
                await _contributors.ExecuteAsync(captures, settings, async (content, captureToken) =>
                {
                    var roots = captures.SelectMany(capture => capture.Roots).ToArray();
                    var built = await builder.BuildAsync(content, roots, settings, captureToken);
                    if (built.Bundle is not { } bundle || built.Verification.Status != ReleaseSemanticStatus.Verified ||
                        built.Verification.Digest != bundle.Digest || built.Verification.Diagnostics.Count != 0)
                        throw new ReleaseCaptureException("capture_content_not_verified", built.Verification);
                    using var zip = new MemoryStream();
                    await codec.WriteZipAsync(bundle, zip, limits, captureToken); zip.Position = 0;
                    var archived = await archives.CreateAsync(captureId, zip, captureToken);
                    if (archived.CaptureId != captureId || archived.ReleaseDigest != bundle.Digest ||
                        archived.Outcome is not (ReleaseArchiveCreateOutcome.Created or ReleaseArchiveCreateOutcome.Existing))
                        throw Refused("capture_archive_not_confirmed");
                    receipt = new(captureId, bundle.Digest, archived.Outcome == ReleaseArchiveCreateOutcome.Created
                        ? ReleaseCaptureOutcome.Created : ReleaseCaptureOutcome.Existing);
                }, token);
                return;
            }
            var group = ordered[index];
            await RetainedCaptureCallback.ExecuteAsync<ReleaseSourceCapture>(
                (capture, cancellation) => group.Key.ExecuteAsync(group.Value.AsReadOnly(), limits, capture, cancellation),
                async (captured, cancellation) =>
                {
                    if (captured.Content is null || captured.Roots.Count != group.Value.Count)
                        throw Refused("capture_source_roots_incomplete");
                    for (var rootIndex = 0; rootIndex < captured.Roots.Count; rootIndex++)
                        if (captured.Roots[rootIndex] is null || captured.Roots[rootIndex].Action != group.Value[rootIndex].Action)
                            throw Refused("capture_source_root_action_mismatch");
                    captures.Add(captured);
                    try { await ExecuteOwnerAsync(index + 1, cancellation); }
                    finally { captures.RemoveAt(captures.Count - 1); }
                }, token);
        }
    }

    internal sealed class CombinedContent : IReleaseContentReader
    {
        private readonly Dictionary<ReleaseMemberKey, List<(IReleaseContentReader Reader, ReleaseContentMember Member)>> _members = [];
        public IReadOnlyList<ReleaseContentMember> Members { get; }
        public CombinedContent(IReadOnlyList<ReleaseSourceCapture> captures, ReleaseBundleLimits limits, long maximumInspectionBytes)
        {
            long inspected = 0; long expanded = 0; var count = 0;
            var blobs = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var capture in captures)
            {
                if (capture.Content.Members.Count > limits.MaxMembers - count) throw Refused("capture_member_limit_exceeded");
                foreach (var member in capture.Content.Members)
                {
                    if (++count > limits.MaxMembers || member is null || member.Descriptor.ByteLength <= 0 ||
                        member.Descriptor.ByteLength > limits.MaxBlobBytes || member.Descriptor.ByteLength > maximumInspectionBytes - inspected)
                        throw Refused("capture_payload_work_limit_exceeded");
                    inspected += member.Descriptor.ByteLength;
                    if (blobs.TryGetValue(member.Descriptor.PayloadDigest, out var blobLength))
                    {
                        if (blobLength != member.Descriptor.ByteLength) throw Refused("capture_member_conflict");
                    }
                    else
                    {
                        if (member.Descriptor.ByteLength > limits.MaxExpandedBytes - expanded)
                            throw Refused("capture_payload_work_limit_exceeded");
                        expanded += member.Descriptor.ByteLength;
                        blobs.Add(member.Descriptor.PayloadDigest, member.Descriptor.ByteLength);
                    }
                    if (!_members.TryGetValue(member.Key, out var entries)) _members.Add(member.Key, entries = []);
                    if (entries.Count > 0 && !Same(entries[0].Member, member)) throw Refused("capture_member_conflict");
                    entries.Add((capture.Content, member));
                }
            }
            Members = Array.AsReadOnly(_members.Values.Select(items => items[0].Member).ToArray());
        }
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => _members[key][0].Member;
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key)
        {
            var entries = _members[key]; var payload = entries[0].Reader.GetPayload(key);
            if (payload.Length != entries[0].Member.Descriptor.ByteLength) throw Refused("capture_member_conflict");
            var frozen = payload.ToArray();
            foreach (var entry in entries.Skip(1))
            {
                var other = entry.Reader.GetPayload(key);
                if (!other.Span.SequenceEqual(frozen)) throw Refused("capture_member_conflict");
            }
            return frozen;
        }
        private static bool Same(ReleaseContentMember left, ReleaseContentMember right) => left.Key == right.Key && left.Codec == right.Codec &&
            left.OwnerVersion == right.OwnerVersion && left.Descriptor.ByteLength == right.Descriptor.ByteLength && left.Descriptor.PayloadDigest == right.Descriptor.PayloadDigest;
    }

    private static IReadOnlyDictionary<string, IReleaseCaptureSource> Registry(IEnumerable<IReleaseCaptureSource> sources)
    {
        var registry = new Dictionary<string, IReleaseCaptureSource>(StringComparer.Ordinal); var count = 0;
        foreach (var source in sources)
        {
            if (++count > MaximumSources || source is null || source.Identity is not { } identity || !Text(identity.Name) || !Text(identity.Version) ||
                source.SupportedSelectionKinds is not { Count: > 0 and <= 32 }) throw new InvalidOperationException("Invalid release capture source registration.");
            var kinds = source.SupportedSelectionKinds.Take(33).ToArray();
            if (kinds.Length is 0 or > 32) throw new InvalidOperationException("Invalid release capture source registration.");
            foreach (var kind in kinds)
                if (!Text(kind) || !registry.TryAdd(kind, source)) throw new InvalidOperationException("Duplicate or invalid release capture selection-kind claim.");
        }
        return registry;
    }

    private static bool Text(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1024 || text.Any(char.IsControl)) return false;
        try { _ = new UTF8Encoding(false, true).GetByteCount(text); return true; }
        catch (EncoderFallbackException) { return false; }
    }
    private static bool Version(ReleaseOwnerVersion? version) => version is null || (version.Scheme switch
    {
        "integer" => version.IntegerValue is > 0 && version.TextValue is null,
        "semver" or "opaque" => Text(version.TextValue) && version.IntegerValue is null,
        _ => false
    });
    private static ReleaseCaptureException Refused(string code) => new(code);
}
