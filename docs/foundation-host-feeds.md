# Elsa.Foundation.Host package feeds

`Elsa.Foundation.Host` compiles in no Elsa feature. Every feature — activities, HTTP, persistence,
the Tasks feature — arrives as a NuGet package through a Nuplane feed and is discovered by CShells
via `NuplaneAssemblyProvider`. This page is the worked reference for pointing the host at a feed.

A feed can be a **directory** of `.nupkg` files or a **remote NuGet V3 service index**. Both are
declared the same way, under `Nuplane:Setup:Feeds`, and both work in the shipped host. A deployment
that wants a feed URL and a pinned version does not need a mounted folder of package binaries.

## The two feed shapes

Exactly one of `DirectoryPath` or `ServiceIndex` per entry.

```json
{
  "Nuplane": {
    "Setup": {
      "AutomaticReconciliation": true,
      "PollInterval": "00:01:00",
      "Feeds": [
        {
          "Name": "local-packages",
          "DirectoryPath": "packages",
          "IncludePatterns": [ "*" ],
          "Directory": { "Watch": true, "DebounceWindow": "00:00:01" }
        },
        {
          "Name": "elsa-4",
          "ServiceIndex": "https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json",
          "IncludePatterns": [
            "Elsa.Tasks [4.0.0-preview.793]",
            "Elsa.Tasks.Schedules [4.0.0-preview.793]"
          ]
        }
      ]
    }
  }
}
```

Both kinds are registered by `AddNuplane` itself, which reads `Nuplane:Setup:Feeds` and calls
`AddFeed(...).FromUri(...)` for every entry carrying a `ServiceIndex`. The host's extra
`AddDirectoryFeedsFromConfiguration` call exists only because the *directory* source ships in a
separate package (`Nuplane.Sources.Directory`) that `AddNuplane` cannot reach. There is no
`AddRemoteFeedsFromConfiguration`, and none is needed.

Add `Credentials` for a private feed. `Nuplane:FeedResolution` controls multi-feed behavior —
`FeedPriorities`, `StopOnFirstSuccessfulFeed`, `OfflineMode`, `PackageInstallRoot`.

## Include patterns behave differently on the two shapes

This is the sharp edge, and in the silent direction it is silent.

An include pattern is `<package-id-glob> [<version-range>]`. The two feed kinds read it almost
inversely, because only a directory feed has a package list to match against, and only a remote feed
parses the version range. Measured against Nuplane 0.0.9-preview.61:

| Pattern | Directory feed | Remote feed |
|---|---|---|
| `*` | every id in the folder, highest version of each | **nothing, silently** |
| `Elsa.*` | every matching id, highest version | **nothing, silently** |
| `Elsa.Tasks` | that id, at the highest version in the folder | **fails the cycle** — no version to resolve |
| `Elsa.Tasks [4.0.0-preview.793]` | **nothing** — the range is not parsed | that package, at exactly that version |

On a **directory** feed the version always comes from the filename and the highest wins;
`PackagePatternMatcher` compares your raw pattern string against the package id, so a ` [range]`
suffix simply fails to match. Use bare globs or bare ids.

On a **remote** feed `FeedRuleDesiredSource` runs in *direct mode*: with no catalog to enumerate it
discards every pattern containing `*` or `?` and keeps only literal ids, and the version range is
required. Use exact ids with an exact range.

A remote feed declared with wildcards is registered and reachable and contributes **zero** desired
packages, so reconciliation completes with nothing to do:

```
Reconciliation cycle completed [IsDegraded=False, FailedCount=0]
Active package catalog read [PackageCount=0, IssueCount=0]
warn: Shell 'default' requested 6 feature(s) that are not available in the runtime feature catalog
```

`IsDegraded=False` with `PackageCount=0` and a shell missing its features is the signature of this
mistake. It looks identical to a healthy host that was asked for nothing, because that is what it is.

## Dependencies resolve across feeds

List roots, not the closure. Transitive dependencies are resolved automatically, and a feed with no
`IncludePatterns` at all still serves as a resolution source. Pinning one root against two feeds:

```json
{ "Name": "elsa-4", "ServiceIndex": "https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json",
  "IncludePatterns": [ "Elsa.Tasks.Schedules [4.0.0-preview.793]" ] },
{ "Name": "nuget", "ServiceIndex": "https://api.nuget.org/v3/index.json" }
```

produces:

```
Elsa.Tasks.Schedules  4.0.0-preview.793  feed=elsa-4  feed-rule:elsa-4
Elsa.Primitives       4.0.0-preview.793  feed=elsa-4  dependency-of:Elsa.Tasks.Schedules
Elsa.Tasks.Core       4.0.0-preview.793  feed=elsa-4  dependency-of:Elsa.Tasks.Schedules
Cronos                0.13.0             feed=nuget   dependency-of:Elsa.Tasks.Schedules
```

Third-party dependencies are fetched from whichever feed has them. Dependencies the runtime already
provides are skipped rather than downloaded: the same root's `Microsoft.Extensions.*` dependencies
and `CShells.Abstractions` do not appear, because the shared framework and
`Nuplane:Loading:SharedAssemblies` already satisfy them.

## Pinning and integrity

A single-point range (`[4.0.0-preview.793]`) pins exactly. For integrity, turn on the lock file
rather than hand-maintaining hashes:

```json
"Nuplane": {
  "LockFile": { "Mode": "Enforce", "Path": "nuplane.lock.json", "FailOnHashMismatch": true }
}
```

`Generate` writes the lock on first reconcile; `Enforce` and `Strict` then refuse a package whose
hash does not match.

Nuplane also models a desired manifest (`Nuplane:Convergence:Manifest` — a file of
`{ Id, Version, SourceHint, Sha512 }` entries). The options bind, but nothing registers
`DesiredManifestPackageSource` as an `IDesiredPackageSource`, in Nuplane or in this host, so the
file is read by nobody. It is not a prerequisite for feed-based deployment: exact-id include
patterns plus the lock file cover pinning and integrity. Treat the manifest as unavailable today.

## What fails loudly

A malformed feed entry fails at startup through options validation (`ValidateOnStart`), before the
app serves traffic:

```
OptionsValidationException: Nuplane setup feed 'x' at 'Nuplane:Setup:Feeds:0'
  must set exactly one of DirectoryPath or ServiceIndex.
OptionsValidationException: Nuplane setup feed 'x' at 'Nuplane:Setup:Feeds:0'
  has an invalid absolute ServiceIndex URI 'not-a-uri'.
```

A package that is declared but missing from every feed fails the *cycle*, not startup — the run
reports `IsDegraded=True` with the package in `FailedPackages`, and the state file records the
reason under `lastFailureById`. That is the loud failure to look for; a degraded cycle means the
feed was reached and the package was not there.

What is not loud is the wildcard case above, because a pattern that matches nothing is
indistinguishable from a feed that was asked for nothing.

## Related

- [Docker & compose](docker.md) — `/app/packages` as the mounted directory feed.
- [Docker Hub quickstart](docker-hub-quickstart.md) — running the prebuilt images.
