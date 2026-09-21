using Elsa.Primitives.Contracts;

namespace Elsa.Primitives.Identity;

/// <summary>
/// Generates random 128-bit GUID identifiers rendered as 32 lowercase hex characters.
/// </summary>
/// <remarks>
/// These identifiers are globally unique with zero coordination but are <b>not</b> time-ordered, so they are a poor fit
/// for indexed primary keys (random inserts fragment the index B-tree). Provided for parity with callers that do not
/// require sortable identifiers; prefer a time-ordered generator otherwise.
/// </remarks>
public sealed class GuidIdentityGenerator : IIdentityGenerator
{
    public string Generate() => Guid.NewGuid().ToString("N");
}
