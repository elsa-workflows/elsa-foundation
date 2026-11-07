using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Persist the checked family header without introducing a provider-specific Runtime consumer codec.</summary>
internal static class StimulusIngressJson
{
    public static string Write(StimulusIngressSource source) => StimulusIngressSourceCodec.Encode(source).GetRawText();
    public static StimulusIngressSource Read(string content)
    {
        using var document = JsonDocument.Parse(content);
        return StimulusIngressSourceCodec.Decode(document.RootElement);
    }
}
