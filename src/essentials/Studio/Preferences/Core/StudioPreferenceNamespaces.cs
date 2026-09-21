using System.Text.Json;
using Elsa.Studio.Preferences.Core.Contracts;

namespace Elsa.Studio.Preferences.Core;

public static class StudioPreferenceNamespaces
{
    public const string Dashboard = "dashboard";
    public const string Attention = "attention";
}

public abstract class ObjectPreferenceNamespace : IStudioPreferenceNamespace
{
    public abstract string Name { get; }
    public virtual int CurrentSchemaVersion => 1;
    public abstract int MaxBytes { get; }

    public virtual bool Validate(JsonElement value) => value.ValueKind == JsonValueKind.Object;

    public virtual JsonElement? Migrate(JsonElement value, int fromSchemaVersion) =>
        fromSchemaVersion == CurrentSchemaVersion ? value.Clone() : null;
}

public sealed class DashboardPreferenceNamespace : ObjectPreferenceNamespace
{
    public const int DefaultMaxBytes = 64 * 1024;
    public override string Name => StudioPreferenceNamespaces.Dashboard;
    public override int MaxBytes => DefaultMaxBytes;
}

public sealed class AttentionPreferenceNamespace : ObjectPreferenceNamespace
{
    public const int DefaultMaxBytes = 32 * 1024;
    public override string Name => StudioPreferenceNamespaces.Attention;
    public override int MaxBytes => DefaultMaxBytes;
}
