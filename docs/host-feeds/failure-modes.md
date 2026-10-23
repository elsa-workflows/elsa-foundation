# When feeds and shells fail

**Part of:** [Elsa.Foundation.Host package feeds](../foundation-host-feeds.md).

Most feed problems fail loudly and name what is wrong. A few do not. Start with the table, then read the section it points to.

| Symptom | Likely cause | Where to look |
|---|---|---|
| Startup fails with `OptionsValidationException` naming `Nuplane:Setup:Feeds:N` | A malformed feed entry | [What fails loudly](#what-fails-loudly) |
| Cycle reports `IsDegraded=True`, package in `FailedPackages` | The package is missing from every feed | [What fails loudly](#what-fails-loudly) |
| Warning event 1034, stage `host-version-unsatisfied` | A package needs a newer host | [What fails loudly](#what-fails-loudly) |
| "carries shared assembly … the host has no copy of it" | A shared-assembly entry for something the host does not carry | [What fails loudly](#what-fails-loudly) |
| `IsDegraded=False`, `PackageCount=0`, shell missing features | Wildcard include pattern on a remote feed (the quiet failure) | [Include patterns](feeds.md#include-patterns-behave-differently-on-the-two-shapes) |
| `FileNotFoundException` / `FeatureNotFoundException` at shell load, cycle healthy | A declared dependency was skipped | [Why a declared dependency can go missing](pinning-and-closures.md#why-a-declared-dependency-can-go-missing) |
| `/health/ready` 503 with `activation-failed` or `activation-refused` | A shell failed to activate at boot | [A shell that fails to activate at boot](#a-shell-that-fails-to-activate-at-boot) |
| Shell fails with an `OptionsValidationException` during activation | A startup check refused the shell | [A shell whose startup checks refuse it](#a-shell-whose-startup-checks-refuse-it) |
| `POST /_module-management/reload` answers 409 or 500 | A shell's reload was refused or failed | [Hot reload](updating-packages.md#hot-reload-after-a-package-change) |

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

A package that needs a **newer host than the one running** is refused at reconciliation rather than
loaded (since Nuplane `0.0.11-preview.93`, [valence-works/nuplane#100](https://github.com/valence-works/nuplane/pull/100)).
It happens when the package, or a package in its closure, depends on a package the host declares under
`Nuplane:HostProvidedPackages`, and the host's own `*.deps.json` carries that package at a version
outside the range the dependency requires — a module built against `Elsa.Workflows.Core [4.1.0, 5.0.0)`
on a host whose deps file carries `4.0.0`, with `Elsa.` declared. A declared package is never acquired,
so there is no newer copy for Nuplane to fetch; loading the module anyway would bind it to the older
assembly, and the failure would surface much later as a missing member at the point of use. Instead the
module, and every root whose closure reaches it, is refused under stage `host-version-unsatisfied`, and
every other root still applies. The host's log carries one Warning per refused package (event 1034,
category `Nuplane.Observability.ReconciliationLogger`):

```
Host-provided dependency version refused [CorrelationId=…, PackageId=Acme.Widgets]:
  Package 'Acme.Widgets@1.4.2' requires 'Elsa.Workflows.Core [4.1.0, 5.0.0)', but the host provides
  'Elsa.Workflows.Core 4.0.0'. …
```

The cycle is degraded like any other failed package, the state file records the stage and message under
`lastFailureById`, and `Reconciliation:StartupFailurePolicy` decides what that means for startup.
`dotnet elsa persistence --restore` refuses the same case by name (exit 3,
`restore-host-version-unsatisfied`), carrying Nuplane's message verbatim; when the same cycle also refused
a module for an unselected provider engine, that refusal is listed beside it under its own `capability-*`
stage rather than left for the next run to find. The fix is one of two, and never a configuration key: run
a host at or above the version the module requires, or pin the module to a version whose requirement this
host satisfies.

The check needs a version to compare against. A declared package the host's deps file does not carry at
all — a prefix entry such as `Elsa.` on a host that compiles in no Elsa feature — is still trusted as
supplied, and each such dependency is logged as a Warning (event 1035) naming the dependent package, the
dependency and the range, so it is visible rather than silent. An *undeclared* package the host carries at
an unsatisfying version is not refused either: it is acquired from the feed like any other dependency.

A package that carries a **shared assembly the host cannot supply** is refused when it loads (since Nuplane
`0.0.11-preview.94`, [valence-works/nuplane#101](https://github.com/valence-works/nuplane/pull/101)). Nuplane
never loads a package's own copy of an assembly a `Nuplane:Loading:SharedAssemblies` entry matches, so when the
host has no copy of it at the entry's major version, the package's whole graph fails to load, and every package in
it is reported with the reason (category `Nuplane.Loading.PackageAutoLoadingObserver`):

```
Package Acme.Widgets failed to load: Package 'Acme.Contracts@1.0.0'
  carries shared assembly 'Acme.Contracts' (public key token: unsigned, major version: 1),
  which the shared-assembly policy leaves to the host, but the host has no copy of it with that major version.
```

The cycle is degraded, and at startup, under the default `Reconciliation:StartupFailurePolicy`, the host exits
with a `NuplaneStartupReconciliationException` naming every failed package. So an entry for an assembly a host
does not carry is not harmless: share only what the host itself references. `FoundationHostBootTests` pins the
refusal, and `SharedAssemblyMajorVersionGuardTests` fails the build when either host declares a share its build output
does not carry.

What is not loud is the wildcard case above, because a pattern that matches nothing is
indistinguishable from a feed that was asked for nothing.

### A shell that fails to activate at boot

With eager activation on (the default, `Elsa:Boot:EagerShellActivation:Enabled`), the host makes one attempt per
configured shell as it starts. A shell that fails is **retried in the background**, not left for its first request, because
a readiness-gated load balancer never sends one: the node would stay live, not ready and idle until restarted.

- **Backoff.** The delay starts at `Elsa:Boot:EagerShellActivation:Retry:InitialDelay` (default `00:00:01`), doubles after each
  failed attempt and stops at `Retry:MaxDelay` (default `00:01:00`), where it stays for as long as the shell is down: there is
  no attempt count after which the host gives up, because the cap is the bound. Each wait is shortened by up to 20% at
  random, so replicas that failed together against a shared database do not retry together; the cap still holds. Both
  settings are `TimeSpan`s; a value that is absent, unparseable or not positive is the default.
- **A fault and a refusal differ.** A fault (a database that is not up yet, a seeder that raced a peer, a feed hiccup) is
  retried on the doubling delay. An EF module's refusal (a pending migration under `Migrate:Policy=Validate`, a contracting
  migration that may not be applied yet, the finalization gate) waits for an operator, and retrying sooner cannot change
  it, so the host checks again only at `MaxDelay`. The node then comes up by itself within that interval of the operator
  running `dotnet elsa persistence apply`, without a request and without a restart.
- **Each failure is reported.** The host log has one warning per failed attempt, with the attempt number and the delay to the
  next, with the exception. `/health/ready` keeps answering 503 and gives every shell that is not active a `reason`, which is
  a stable code and, for a failed activation, the `attempts` so far and `nextAttemptAt`: `activation-failed` (a fault, and the
  host retries), `activation-refused` (an EF module refused; an operator resolves it), `shell-not-serving` (between states)
  or `not-activated` (no attempt has failed yet). The codes are `ShellNotActiveReasonCodes`. That is all the probe says,
  because it is public and unauthenticated: it never names the exception, the EF module or its migrations.
- **Attention carries the detail.** While a shell is not active, the `shell-activation` Attention contributor lists it: a
  warning for a fault (with the exception *type*), critical for a refusal (with the EF module, its code and the pending
  migrations). It needs the `module-management:read` permission, the one the Modularity contributor asks for. It goes when
  the shell is active by any path: the retry, a request or a reload. Messages are in the host log only, because a driver's
  message can echo a connection string, and the persistence command is in the log and in the reload endpoint's 409.
- **When the only shell is down, nothing serves the Attention item.** It is served by whichever shell is active, so it helps
  a host with more than one shell; a host whose only shell is the one that failed has `/health/ready` and the host log, and
  that is where its operators look.

The probe itself still activates nothing. Workbench has its own `EagerShellActivationHostedService` and is unchanged.

### A shell whose startup checks refuse it

Both hosts compose `AddShellStartupValidation()` on the host container. A shell then runs, as the first step of its
activation, every options validator registered with `ValidateOnStart` in its own container: the ones its features
registered, and the host's own, which CShells copies into every shell and which run there against the options the shell's
container resolves. A failing check fails the activation with the `OptionsValidationException` it raised. It runs first in
the `Prepare` phase, below the order of every initializer the shipped features register, so before a provider binding is
checked or a migration runs.

With eager activation on, the host records that as a fault, not a refusal: it is retried on the doubling delay and
`/health/ready` reports `activation-failed`. A retry cannot change a composition error, so the exception in the host log
is what to act on.

Without that call nothing runs a shell's startup checks. The generic host runs them for its own container alone, and
CShells runs only shell initializers when a shell activates. A check a shell feature registers with `ValidateOnStart`
would then run at the first use of its options, often a request, or never, when it validates a marker options type
that nothing resolves (#2331). Two consequences:

- **A host of your own** that builds shells from Elsa features composes `AddShellStartupValidation()` too.
- **A feature whose check must hold in any host**, whether or not that host composes the call, registers a shell
  initializer for it instead, as `EfProviderBindingValidator` does.

---

All pages: [Overview](../foundation-host-feeds.md) · [Point the host at a feed](feeds.md) · [Pinning and closures](pinning-and-closures.md) · [Updating packages](updating-packages.md) · [When things fail](failure-modes.md) · [Clustering](clustering.md)
