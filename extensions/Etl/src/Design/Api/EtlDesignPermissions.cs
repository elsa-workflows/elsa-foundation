using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Etl.Design.Api;

/// <summary>Permissions required by the finite ETL authoring API.</summary>
public static class EtlDesignPermissions
{
    public const string ReadDefinitions = "etl/definitions:read";
    public const string ManageDefinitions = "etl/definitions:manage";
    public const string PublishDefinitions = "etl/definitions:publish";
    public const string ReadBindings = "etl/bindings:read";
}

internal sealed class EtlDesignPermissionContributor : IPermissionContributor
{
    public string OwnerId => EtlDesignApi.OwnerId;
    public IEnumerable<Permission> Contribute() =>
    [
        new(EtlDesignPermissions.ReadDefinitions, "Read ETL definitions", "ETL", "Read ETL drafts and published versions."),
        new(EtlDesignPermissions.ManageDefinitions, "Manage ETL definitions", "ETL", "Create, update and validate ETL drafts."),
        new(EtlDesignPermissions.PublishDefinitions, "Publish ETL definitions", "ETL", "Publish immutable ETL versions."),
        new(EtlDesignPermissions.ReadBindings, "Read ETL bindings", "ETL", "Read host-provided binding aliases and roles.")
    ];
}
