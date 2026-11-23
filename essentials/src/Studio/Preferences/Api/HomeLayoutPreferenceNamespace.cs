using System.Text.Json;
using Elsa.Studio.Preferences.Core;

namespace Elsa.Studio.Preferences.Api;

public sealed class HomeLayoutPreferenceNamespace : ObjectPreferenceNamespace
{
    public const int DefaultMaxBytes = 64 * 1024;
    public override string Name => StudioPreferenceNamespaces.HomeLayout;
    public override int MaxBytes => DefaultMaxBytes;

    public override bool Validate(JsonElement value) => StudioHomeLayout.IsValid(value);
}
