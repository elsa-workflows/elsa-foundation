namespace Groundwork.Core.Manifests;

public sealed record StorageManifestIdentity(string Value)
{
    public override string ToString() => Value;
}

public sealed record StorageManifestVersion(string Value)
{
    public override string ToString() => Value;
}

public sealed record StorageManifestOwner(string Value)
{
    public override string ToString() => Value;
}

public sealed record StorageUnitIdentity(string Value)
{
    public override string ToString() => Value;
}
