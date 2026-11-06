using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Unregistered mechanics only; callers already own physical material and eligibility authority.</summary>
internal sealed class MessageMaterialInterestExtractor
{
    private readonly Registration[] registrations;

    internal MessageMaterialInterestExtractor(IEnumerable<IMessageMaterialInterestProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var snapshot = new List<Registration>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var utf8 = new UTF8Encoding(false, true);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var id = provider.ProviderId;
            try
            {
                if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || utf8.GetByteCount(id) > 256 || !identities.Add(id))
                    throw new ArgumentException("Message interest provider identities must be valid and distinct.", nameof(providers));
            }
            catch (EncoderFallbackException)
            {
                throw new ArgumentException("Message interest provider identities must be valid and distinct.", nameof(providers));
            }
            snapshot.Add(new(id, provider.ModelProtocol, provider));
        }
        registrations = snapshot.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    internal MessageMaterialInterestResult Extract(RuntimeExecutionMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        MessageMaterialInterestResult? winner = null;
        foreach (var registration in registrations)
        {
            if (registration.Protocol != material.Model?.Protocol) continue;
            MessageMaterialInterestResult result;
            try { result = registration.Provider.Describe(material) ?? throw Unavailable(); }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or NotSupportedException or JsonException or RuntimeSchedulerWorkUnavailableException)
            {
                // Provider validation details may contain model data. Retain no raw cause in this safe boundary.
                throw Unavailable();
            }
            if (!result.IsRecognized) continue;
            if (winner is not null) throw Unavailable();
            winner = result;
        }
        return winner ?? throw Unavailable();
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable() =>
        new("message.interest.unavailable", "Message material interest cannot be determined.");

    private sealed record Registration(string Id, RuntimeExecutionProtocol? Protocol, IMessageMaterialInterestProvider Provider);
}
