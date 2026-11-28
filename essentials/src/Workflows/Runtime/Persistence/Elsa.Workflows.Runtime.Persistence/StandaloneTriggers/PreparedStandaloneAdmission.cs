using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class PreparedStandaloneAdmission
{
    private const string ProfileProperty = "admissionProfile";
    private const string Profile = "elsa.kernel.prepared-standalone.v1";

    internal static JsonElement Capture(JsonElement payload)
    {
        var captured = JsonNode.Parse(payload.GetRawText())!.AsObject();
        captured.Add(ProfileProperty, Profile);
        return JsonSerializer.SerializeToElement(captured);
    }

    internal static bool IsMarked(RuntimeStandaloneIngressSource source) =>
        source.CanonicalPayload.ValueKind == JsonValueKind.Object &&
        source.CanonicalPayload.TryGetProperty(ProfileProperty, out var profile) &&
        profile.ValueKind == JsonValueKind.String && profile.GetString() == Profile;

    internal static void RequireSource(RuntimePostCommitOutboxItem item)
    {
        if (item.StandaloneIngress is not { } source || !IsMarked(source) ||
            item.SubscriptionDelivery is not { } selection ||
            selection.WaitDelivery is not null && !PreparedStandaloneHttp.HasVerifiedEmptyWaits(item) ||
            selection.Recipients.Any(recipient => recipient.ModelKey != "native" || recipient.ModelProtocol is not null) ||
            selection.Progress.Any(progress => progress.Seal is { } seal && seal.Version != RuntimeDispatchSeal.PreparedStartQualificationVersion))
            throw new NotSupportedException("This Runtime owner requires standalone sources accepted for prepared Actor Foundation delivery.");
    }
}
