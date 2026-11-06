using Elsa.Persistence.EntityFramework;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities
{
    // Persistent selection gates ordinary writers in every current context; it never certifies complete coverage.
    internal sealed class MessageActiveInterestOwnerEntity
    {
        public string Id { get; set; } = null!;
        public string ScopeKey { get; set; } = null!;
        public string ScopeKeyHash { get; set; } = null!;
        public string Incarnation { get; set; } = null!;
        public string ContentJson { get; set; } = null!;
        public string SchemaVersion { get; set; } = null!;
        public long Revision { get; set; }
    }
}
namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration
{
    internal sealed class MessageActiveInterestOwnerEntityConfiguration : IEntityTypeConfiguration<MessageActiveInterestOwnerEntity>
    {
        public void Configure(EntityTypeBuilder<MessageActiveInterestOwnerEntity> b)
        {
            RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeMessageInterestEfModule.ActiveOwnerTable);
            b.Property(row => row.ScopeKey).HasMaxLength(null); b.HasKey(row => row.Id); b.Property(row => row.Id).HasMaxLength(64);
            b.Property(row => row.Incarnation).HasMaxLength(32).IsRequired();
        }
    }
}
namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores
{
    internal static class MessageActiveInterestOwnerDocument
    {
        internal sealed record Document(int Version, string Scope, string Incarnation);
        internal static string Id(string scope) => EfRelationalIdentity.HashLengthFramed("message-active-owner/v1", scope);
        internal static Document Read(MessageActiveInterestOwnerEntity row, string scope)
        {
            EfSchemaVersion.EnsureCurrent(RuntimeMessageInterestEfModule.Chain, row.SchemaVersion);
            try
            {
                using var json = JsonDocument.Parse(row.ContentJson);
                if (!json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(new[] { "version", "scope", "incarnation" }.Order(StringComparer.Ordinal))) throw Invalid();
                var value = RuntimeArtifactJson.Deserialize<Document>(row.ContentJson);
                if (RuntimeArtifactJson.Serialize(value) != row.ContentJson || value.Version != 1 || value.Scope != scope || !Guid.TryParseExact(value.Incarnation, "N", out _) ||
                    row.Id != Id(scope) || row.ScopeKey != EfRelationalIdentity.Encode(scope) || row.ScopeKeyHash != EfRelationalIdentity.Hash(scope) ||
                    row.Incarnation != value.Incarnation || row.Revision != 1) throw Invalid();
                return value;
            }
            catch (Exception error) when (error is JsonException or ArgumentException or NullReferenceException or InvalidOperationException)
            { throw Invalid(); }
        }
        internal static MessageActiveInterestOwnerEntity New(string scope)
        {
            var value = new Document(1, scope, Guid.NewGuid().ToString("N"));
            return new() { Id = Id(scope), ScopeKey = EfRelationalIdentity.Encode(scope), ScopeKeyHash = EfRelationalIdentity.Hash(scope),
                Incarnation = value.Incarnation, ContentJson = RuntimeArtifactJson.Serialize(value), SchemaVersion = RuntimeMessageInterestEfModule.SchemaVersion, Revision = 1 };
        }
        private static InvalidDataException Invalid() => new("The persisted native interest owner selection is corrupt.");
    }
}
