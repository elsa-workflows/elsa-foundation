using System.Text;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Workflows.Publishing.Releases.Capture;

/// <summary>Source-only member expansion. All immutable graph facts still belong to the final owner verification.</summary>
internal sealed class ReleaseCaptureContributors
{
    private const int MaximumContributors = 32;
    private sealed record Registration(IReleaseSourceMemberContributor Contributor, ReleaseSemanticAdapterIdentity Identity, HashSet<ReleaseCodecKey> Codecs);
    private readonly Registration[] _registrations;

    internal ReleaseCaptureContributors(IEnumerable<IReleaseSourceMemberContributor> contributors)
    {
        var registrations = new List<Registration>(); var identities = new HashSet<ReleaseSemanticAdapterIdentity>();
        foreach (var contributor in contributors)
        {
            if (registrations.Count >= MaximumContributors || contributor is null || contributor.Identity is not { } identity ||
                !Text(identity.Name) || !Text(identity.Version) || !identities.Add(identity) || contributor.SupportedContainerCodecs is not { Count: > 0 and <= 10_000 })
                throw new InvalidOperationException("Invalid or duplicate release source member contributor identity.");
            var codecs = new HashSet<ReleaseCodecKey>(); var count = 0;
            foreach (var key in contributor.SupportedContainerCodecs)
                if (++count > 10_000 || key is null || !Text(key.Kind) || !Text(key.Codec) || key.CodecVersion <= 0 || !codecs.Add(key))
                    throw new InvalidOperationException("Invalid release source container-codec claim.");
            if (count != contributor.SupportedContainerCodecs.Count || codecs.Count == 0)
                throw new InvalidOperationException("Invalid release source container-codec claim.");
            registrations.Add(new(contributor, identity, codecs));
        }
        _registrations = registrations.OrderBy(value => value.Identity.Name, StringComparer.Ordinal).ThenBy(value => value.Identity.Version, StringComparer.Ordinal).ToArray();
    }

    internal async ValueTask ExecuteAsync(IReadOnlyList<ReleaseSourceCapture> roots, ReleaseSemanticVerificationOptions options,
        Func<IReleaseContentReader, CancellationToken, ValueTask> complete, CancellationToken token)
    {
        var captures = roots.ToList(); var limits = options.TransportLimits ?? new();
        var processed = new HashSet<(ReleaseSemanticAdapterIdentity Adapter, ReleaseMemberKey Member, string Digest)>();
        long inspected = 0; var held = roots.Count;
        await RoundAsync(0, token);

        async ValueTask RoundAsync(int round, CancellationToken currentToken)
        {
            currentToken.ThrowIfCancellationRequested();
            var content = new ReleaseCaptureService.CombinedContent(captures, limits, options.MaxInspectionBytes);
            var batches = new List<(Registration Registration, IReadOnlyList<ReleaseContentMember> Members)>();
            foreach (var registration in _registrations)
            {
                var members = new List<ReleaseContentMember>();
                foreach (var member in content.Members.OrderBy(value => value.Key.Kind, StringComparer.Ordinal)
                             .ThenBy(value => value.Key.Identity, StringComparer.Ordinal))
                {
                    if (!registration.Codecs.Contains(member.Codec) || !processed.Add((registration.Identity, member.Key, member.Descriptor.PayloadDigest))) continue;
                    if (member.Descriptor.ByteLength > options.MaxInspectionBytes - inspected)
                        throw new ReleaseCaptureException("capture_source_inspection_limit_exceeded");
                    inspected += member.Descriptor.ByteLength; members.Add(member);
                }
                if (members.Count > 0) batches.Add((registration, members.AsReadOnly()));
            }
            if (batches.Count == 0) { await complete(content, currentToken); return; }
            if (round >= limits.MaxDependencyDepth) throw new ReleaseCaptureException("capture_source_round_limit_exceeded");
            await BatchAsync(0, currentToken);

            async ValueTask BatchAsync(int index, CancellationToken batchToken)
            {
                batchToken.ThrowIfCancellationRequested();
                if (index == batches.Count) { await RoundAsync(round + 1, batchToken); return; }
                if (held >= MaximumContributors) throw new ReleaseCaptureException("capture_source_retention_limit_exceeded");
                var batch = batches[index]; held++;
                try
                {
                    await RetainedCaptureCallback.ExecuteAsync<IReleaseContentReader>(
                        (capture, cancellation) => batch.Registration.Contributor.ExecuteAsync(batch.Members, content, limits, capture, cancellation),
                        async (additions, cancellation) =>
                        {
                            captures.Add(new(additions, []));
                            try
                            {
                                // A synchronous contributor must not turn a bounded dependency chain into CLR stack recursion.
                                await Task.Yield();
                                await BatchAsync(index + 1, cancellation);
                            }
                            finally { captures.RemoveAt(captures.Count - 1); }
                        }, batchToken);
                }
                finally { held--; }
            }
        }
    }

    private static bool Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl)) return false;
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); return true; }
        catch (EncoderFallbackException) { return false; }
    }
}
