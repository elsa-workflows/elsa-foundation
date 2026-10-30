using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Attributes;

/// <summary>Declares the authored-control protocol a structural parent accepts from explicitly opted-in children.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ActivityChildControlConsumerAttribute : Attribute
{
    public ActivityChildControlConsumerAttribute(string key, int version)
    {
        RuntimeChildControlValidation.Text(key, nameof(key), 128);
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));
        Key = key;
        Version = version;
    }

    public string Key { get; }
    public int Version { get; }
}
