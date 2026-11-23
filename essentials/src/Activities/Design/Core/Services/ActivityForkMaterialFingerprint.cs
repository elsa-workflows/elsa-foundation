using System.Security.Cryptography;
using System.Text.Json;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>Computes the canonical hash used to bind reviewed reusable-activity fork material.</summary>
public static class ActivityForkMaterialFingerprint
{
    public static string Compute(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "sha256:" + Elsa.Serialization.Core.JsonHashInput.Hash(value);
    }
}
