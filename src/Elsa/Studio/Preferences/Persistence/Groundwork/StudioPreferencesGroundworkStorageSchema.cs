using Groundwork.Kernel;

namespace Elsa.Studio.Preferences.Persistence.Groundwork;

/// <summary>The ordinary Groundwork v2 row owned by Studio Preferences.</summary>
public static class StudioPreferencesGroundworkStorageSchema
{
    public const string UnitId = "elsa-studio-preferences";
    public const string UnitName = "elsa_studio_preferences";
    public const string IdField = "id";
    public const string PayloadField = "payload";

    public static StorageUnit CreateUnit() =>
        StorageUnit.Declare(UnitId, UnitName)
            .String(IdField, 64, column => column.Required())
            .Json(PayloadField, column => column.Required())
            .Key(IdField)
            .OptimisticConcurrency()
            .Build();
}
