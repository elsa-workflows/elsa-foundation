namespace Elsa.Primitives.Models;

/// <summary>
/// The CLR activity construction descriptor: it identifies the activity's CLR type by a <b>stable
/// alias</b> — the shared <see cref="TypeAliasConvention"/> canonical alias (the dotted
/// <see cref="System.Type.FullName"/>, never an assembly name or version). This is the same value the
/// reconciliation model carries as its <c>ActivityTypeKey</c>.
/// </summary>
/// <remarks>
/// <para>
/// It replaces the brittle assembly-name + assembly-version identity that the former
/// <c>TypeInformation</c> descriptor carried: a package version bump (or assembly rename) no longer
/// breaks construction, because resolution goes through the alias, not <c>Assembly.Load(name, version)</c>.
/// </para>
/// <para>
/// The runtime CLR constructor resolves <see cref="TypeAlias"/> to a live <see cref="System.Type"/>
/// through the well-known type registry, which is seeded at startup from the loaded activity CLR types
/// under the very same convention — so the alias the reflection-only scanner emits always resolves back
/// to the real activity type.
/// </para>
/// </remarks>
public sealed record ClrActivityDescriptor(string TypeAlias);
