namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;

/// <summary>Provider-safe sizes for the P02 publication-policy and P03 projection-intent tables.</summary>
public static class PublishingPolicyProjectionEfModule
{
    public const string PolicyTableName = "elsa_publication_policies";
    public const string ProjectionIntentTableName = "elsa_publication_projection_intents";
    public const string PolicyByIdentityIndexName = "IX_elsa_publication_policies_scope_policyKey";
    public const string ProjectionIntentByIdentityIndexName = "IX_elsa_publication_projection_intents_scope_intentId";
    public const string ProjectionIntentByPublicationIndexName = "IX_elsa_publication_projection_intents_scope_publication_order";

    // The Publishing source contracts permit opaque IDs up to the shared 450-code-unit relational bound.
    public const int IdentityMaximumLength = 450;
    public const int HashMaximumLength = 64;
    public const int PolicyKeyMaximumLength = IdentityMaximumLength + 16;
    // EfRelationalIdentity.CreateOrderKey is a fixed-width two-byte-per-code-unit binary key plus a length suffix.
    public const int IntentIdOrderKeyMaximumLength = (IdentityMaximumLength + 1) * sizeof(char);
    public const int SchemaVersionMaximumLength = 32;
    public const int EnumMaximumLength = 32;
    public const int FailureCodeMaximumLength = 128;
    public const int FailureMessageMaximumLength = 512;
    public const int MaximumMaterializedListEntries = 512;
    public const string SchemaVersion = "1.0.0";
}
