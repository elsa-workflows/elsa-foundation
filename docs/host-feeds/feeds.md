# Point the host at a feed

**Part of:** [Elsa.Foundation.Host package feeds](../foundation-host-feeds.md).

`Elsa.Foundation.Host` ships with no Elsa features compiled in. Every feature arrives as a NuGet package from a **feed**. This page shows how to declare feeds, how to write include patterns that actually match, and how to give a private feed its credentials.

> **The one thing to remember:** a *directory* feed and a *remote* feed read include patterns almost oppositely. Use bare ids or globs on a directory feed, and exact ids with an exact version range on a remote feed. A wildcard on a remote feed matches **nothing, silently**.

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

Add `Credentials` for a private feed — a *reference* to its secret, never the secret; see
[Feed credentials](#feed-credentials). `Nuplane:FeedResolution` controls multi-feed behavior —
`FeedPriorities`, `StopOnFirstSuccessfulFeed`, `OfflineMode`, `PackageInstallRoot`.

A relative `DirectoryPath` is resolved against the host's **content root** — the same directory this
`appsettings.json` was read from — because the host passes
`nuplane.UseBasePath(builder.Environment.ContentRootPath)` where it composes Nuplane
(`Essentials/apps/Elsa.Foundation.Host/Program.cs`, and the same call in `Elsa.Workbench`). Without that call
Nuplane resolves such a path against the *process's current directory* instead, and the two are the same
place only when the host is started from its own directory. A deployment that sets the content root
independently of where the process is launched — `ASPNETCORE_CONTENTROOT`, a systemd unit, a container
`WORKDIR` — then reads this file out of one directory and looks for `packages` under another. That presents
as an empty feed, and per [What fails loudly](failure-modes.md#what-fails-loudly) an empty feed is the quiet failure. An
absolute `DirectoryPath` is unaffected either way.

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
## Feed credentials

A private feed is configured with a **reference** to its secret, never with the secret:

```json
{
  "Nuplane": {
    "Setup": {
      "Feeds": [
        {
          "Name": "private-feed",
          "ServiceIndex": "https://packages.example.com/v3/index.json",
          "Credentials": "secrets://elsa/feed-token",
          "IncludePatterns": [ "Acme.Plugins.Widgets [1.4.2]" ]
        }
      ]
    }
  }
}
```

The reference is `secrets://<provider>/<name>`. The provider segment selects a registered provider and is
matched case-insensitively; everything after the first slash is the name. A `Credentials` value that is not
of that shape fails options validation at startup, and the rejected value is never echoed — a host that
pasted the token itself into `Credentials` is exactly the case that rule catches. Credentials are forbidden
on a `file://` feed, and a credentialed feed has to be an HTTPS service index.

**The two providers, and where each one works.**

| Provider | Registered by | Reads | Works in |
|---|---|---|---|
| `env` | `AddNuplane`, always | That name in the process's environment | Anywhere Nuplane runs: a started host, and the host-free `dotnet elsa persistence --restore` pass |
| `elsa` | `AddSecretsFeedCredentials()`, from `Elsa.Secrets.Nuplane` | That name in the Secrets module, through `ISecretValueResolver` | The container that composed both `AddNuplane` and the Secrets read path — and nowhere else |

That last column is the whole of it, and it is a container rule rather than a preference. Nuplane resolves
`IEnumerable<ISecretReferenceProvider>` out of the container `AddNuplane` was called on, once, and shares
the instance across every feed and every cycle. So a host that composes Nuplane on its own container — which
is what `Elsa.Foundation.Host` and `Elsa.Workbench` both do — has to register the provider *there*:

```csharp
builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.UseBasePath(builder.Environment.ContentRootPath);
    nuplane.Services.AddSecretsFeedCredentials();   // and AddSecrets(...) on the same container
});
```

Enabling the `SecretsNuplane` **shell feature** registers the same provider, but into that shell's own
container, which is a different container: CShells builds each shell by copying the root's service
descriptors into a fresh collection and adding the shell's features to that. So the feature claims `elsa`
for a shell's own Nuplane composition, and does **not** claim it for the host's reconcile loop. Neither host
composes the provider on its own container today, so `secrets://elsa/…` in either of their
`appsettings.json` is refused by name — which is the reported outcome below, not a silent one.

There is also a bootstrapping limit worth knowing before reaching for `elsa` on a package-hosted host: the
Secrets module itself arrives as a package. A feed whose credential lives in Secrets cannot be the feed that
delivers Secrets. `secrets://env/NAME` has no such ordering problem, which is why it stays the right answer
for a host's first boot.

**Secret shape.** Whatever the provider returns is either `user:password`, split at the first colon so a
password may contain colons, or a bare token, which Nuplane sends as the password under a fixed placeholder
user name — the form Azure Artifacts, Feedz and other token-issuing feeds accept. A value whose colon leaves
either half empty is refused rather than sent half-formed. Both the version enumeration and the package
download authenticate, each with credentials attached to that one feed; no process-global NuGet credential
provider is installed, so one feed's secret is never offered to another.

**When a reference cannot be resolved** — no registered provider claims its segment, the provider holds no
value, or the referenced secret is expired, revoked or under another tenant — the feed is refused **by
name**. It is not contacted at all: not for version enumeration, not for a download, and not even to serve a
package an earlier authenticated run already installed from it. A running host fails the packages that could
only have come from that feed, naming the feed in the failure; the `--restore` pass reports the feed and
drops it before its first network call. With no provider able to resolve anything, behaviour is exactly what
it was before credentials could be resolved at all.

**What is never logged.** The reference is configuration and may appear in logs; the secret never does.
Resolved secrets are wrapped so that logging, interpolating or including one in an exception prints `***`,
and every refusal and failure message names the feed only — never the value, the reference, or the provider.
Nothing resolved is written to the state file, and a resolved secret is cached for the duration of one
reconciliation cycle and dropped with it. The `elsa` provider holds the same line on its own side: it
returns the raw string to Nuplane and does nothing else with it, and a resolution that failed contributes
its failure code at most.

---

All pages: [Overview](../foundation-host-feeds.md) · [Point the host at a feed](feeds.md) · [Pinning and closures](pinning-and-closures.md) · [Updating packages](updating-packages.md) · [When things fail](failure-modes.md) · [Clustering](clustering.md)
