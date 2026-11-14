# Owned external authentication Probe

Qualification is recorded against exact clean source commits, toolchain identities and output hashes.
The original source-only checkpoint did not execute these runners; its limits are historical. Local execution
requires the owner's current authorization and owned disposable resources. A successful build is supporting
evidence, not provider, deployment or visual acceptance.

The Probe composes the prepared normal Identity/API/OIDC/IAM/session adapters, shared controlled-provider
fixture and explicit OpenIddict test wiring. It does not implement an alternative authentication authority.
It uses an explicitly owned root/marker, loopback development listeners and disposable SQLite state, or an
explicit loopback PostgreSQL database/user in its owned namespace. Do not use personal/shared stores or
production credentials. Provider/configuration/keys and private control material stay outside repository source.
Controlled fixture credentials in test source are public fixture values, not production configuration.

## Source and evidence controls

The three control-test sources define thirteen cases: five build-provenance cases, six evidence cases and
both deadline/cancellation process-cleanup cases. Actual executions belong to their source-bound receipts.

- `ui-build-provenance.mjs` binds ordered producer commands, successful acknowledgements, source commit/tree,
  actual tool paths/versions/hashes and output bytes. Clean-source checks disable the shared Git filesystem
  monitor. Exact build recipes use the ordinary PATH build-slot launcher; queue time stays inside the recorded
  operational deadline. Failed/missing/stale/malformed/wrong-producer or changed-output receipts are refused.
- `ui-toolchain.mjs` observes the actual Node, Python, dotnet launcher/host/resolved SDK and hashes rather than
  trusting version labels. Its observation commands execute only when the future producer is invoked.
- `ui-owned-command.mjs` bounds an owned POSIX process group and drains its children after cancellation,
  deadline, failure or leader exit. It never signals a shared listener or an unowned process group.
- `ui-evidence.mjs` permits only named owned application routes and public fixture input values. It withholds
  unsafe callback/credential material and keeps private capture custody separate. It is not an arbitrary-page
  sanitizer or permission to publish raw logs, traces, configuration, cookies or authentication responses.
- `ui-recovery.mjs` and `ProbeSessionFaults.cs` retain failed/uncertain-write and recovery oracles for the owned
  fixture only. Failed metadata testing remains distinct from structural configuration readiness; the preserved
  c575 correction is carried without claiming it has passed here.

The owned probe serializes ordinary interactive token exchanges with `__probe/state` snapshots. Each snapshot
includes the completed exchange counter for the requesting canonical tenant/user, paired with the persisted
token count. Preview checks still require exactly two rows per observed ordinary exchange and no extra
credentials; exchanges by another subject, failed exchanges and direct preview writes receive no allowance.
Browser response checks separately require the existing administrator subject. This avoids counting a normal
exchange in both a database baseline and a later independently sampled browser delta.

## Browser and owning dependencies

The module's `Client/scripts/browser.mjs` now imports these actual helpers and its `test:browser` package entry
is present. It mounts the real components with an owned composed Probe; `ui-modes-browser.mjs` supplies the
permission/ownership/availability profiles. Invoking those scripts can start processes and write owned stores;
adding them here does not launch or authorize them. Full Studio mode requires current successful owned build
provenance and served-module byte verification. Isolated components do not establish full Studio acceptance.

The Probe project and linked fixture/project paths use solver-generated dependency locks. The runner sources
below produce current-source compiled/runtime/package/visual evidence. Independent review,
protected merge and resulting-main qualification remain required by
[spec1212](../../../specs/1212-external-authentication/spec.md). Historical results on
[closed PR3806](https://github.com/elsa-workflows/elsa-foundation/pull/3806) remain bound to their recorded source.

## Prepared qualification runners

These entry points do not themselves establish a passing result or authorize execution. Use the current
owner-authorized local resource scope, current locks, and successful exact-source producer
receipts through the ordinary build-slot launcher. Do not import helpers just to inspect them: imports can
capture tools, start processes or create state. Preserve failures and UNKNOWN results as such.

| Entry point | Intended scope and options |
|---|---|
| `qualify-keycloak.mjs` | Two independently owned, digest-pinned Keycloak instances, exact callback registration and same-subject/different-issuer journeys. Default uses the component driver; `--studio` uses the full Studio runner. The image must already exist; no implicit pull. |
| `qualify-nodes.mjs` | Separate backend processes sharing the owned provider/store/key scope. Default is PostgreSQL managed mode; `--sqlite` selects SQLite and `--baseline` selects configuration-only mode. Record each of the four combinations separately. |
| `qualify-studio.mjs` | Composed Studio, verified served module bytes and source-bound browser evidence. No argument selects the managed journey; supported profiles are `empty`, `configuration`, `provider-reader`, `denied`, `external-owned`, `hybrid`, `missing-api`, `missing-sessions`. |
| `qualify-workbench.mjs` | Source-built Workbench with an owned temporary shell configuration, local sign-in and administration smoke. Repository assembly discovery does not activate external sign-in in the default deployment. |
| `qualify-packages.py` | Archive exact clean HEAD, derive the backend production reference closure, pack it and run a separate public NuGet consumer without project references. Optional `--output` must name a new owned directory. |

The package consumer uses current `TokenSubject.ForProviderSubject` factories and verifies issuer isolation.
The local version override follows the existing `PackageVersioning.props` development-pack exception;
compilation and NuGet provenance still require actual qualification. Studio packages additionally use the
current `tools/ci/studio-package-identities.json` inventory and `verify_studio_nuget_packages.py` pipeline.
Neither closure has a fixed historical package count.

The CI additions retain existing job selection, required check names, unrelated journeys and time limits.
They prepare one standard Studio producer, the thirteen controls, the Probe producer and all nine Studio
profiles. Downloadable outputs are restricted to screenshots and bounded source-bound receipts; private
credentials, callback material, raw logs and capture custody are excluded. No CI has been dispatched here.
