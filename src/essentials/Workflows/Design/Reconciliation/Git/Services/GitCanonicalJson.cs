using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// Canonicalization helpers that make git's on-disk file a pure-whitespace rendering of the deterministic
/// payload serialization, so content identity (a hash) is stable across the compact ↔ indented forms
/// (ADR 0034 D3/D8; spec 085 R4).
///
/// The on-disk file is the <em>indented</em> canonical state; content identity is the SHA-256 of the
/// <em>compact</em> canonical form. "Strip whitespace" is done by re-parsing to a <see cref="JsonNode"/>
/// and re-emitting compact — a structural transform that preserves member order (the serializer is
/// deterministic) and, crucially, whitespace <em>inside</em> string values, which a naive byte-level
/// strip would corrupt.
/// </summary>
public static class GitCanonicalJson
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    /// <summary>The compact canonical serialization of <paramref name="state"/> — the hash preimage.</summary>
    public static string ToCompact(WorkflowDefinitionState state, IPayloadSerializer serializer) =>
        serializer.Serialize(state);

    /// <summary>The indented on-disk rendering of a compact canonical string (pure-whitespace transform).</summary>
    public static string Indent(string compact) =>
        JsonNode.Parse(compact)!.ToJsonString(IndentedOptions);

    /// <summary>The compact canonical form recovered from an on-disk (indented) file's text.</summary>
    public static string CompactFromFile(string fileText) =>
        JsonNode.Parse(fileText)!.ToJsonString();

    /// <summary>Lowercase hex SHA-256 of a canonical (compact) string.</summary>
    public static string Sha256Hex(string compact) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(compact))).ToLowerInvariant();

    /// <summary>The content hash of a file's canonical form (convenience for the source).</summary>
    public static string HashFile(string fileText) => Sha256Hex(CompactFromFile(fileText));
}
