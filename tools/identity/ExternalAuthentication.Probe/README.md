# Owned external authentication Probe

**Source-preparation status: unqualified and unrun.** This prefix introduces the Probe, shared controls and
Studio browser driver. It has not restored dependencies, built, run tests, started servers/providers/browsers,
created databases, captured tools or produced qualification receipts. Current execution remains on hold.

The Probe composes the prepared normal Identity/API/OIDC/IAM/session adapters, shared controlled-provider
fixture and explicit OpenIddict test wiring. It does not implement an alternative authentication authority.
It uses an explicitly owned root/marker, loopback development listeners and disposable SQLite state, or an
explicit loopback PostgreSQL database/user in its owned namespace. Do not use personal/shared stores or
production credentials. Provider/configuration/keys and private control material stay outside repository source.
Controlled fixture credentials in test source are public fixture values, not production configuration.

## Source and evidence controls

The three control-test sources define thirteen cases: five build-provenance cases, six evidence cases and
both deadline/cancellation process-cleanup cases. No case has been executed on this stack.

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

## Browser and owning dependencies

The module's `Client/scripts/browser.mjs` now imports these actual helpers and its `test:browser` package entry
is present. It mounts the real components with an owned composed Probe; `ui-modes-browser.mjs` supplies the
permission/ownership/availability profiles. Invoking those scripts can start processes and write owned stores;
adding them here does not launch or authorize them. Full Studio mode requires current successful owned build
provenance and served-module byte verification. Isolated components do not establish full Studio acceptance.

The Probe project and linked fixture/project paths are source-staged. Its lock file is deliberately not copied
from the obsolete carrier; authorized tooling must regenerate affected metadata before a locked producer can
succeed. Real-provider, store/process, packed-public-consumer, full Studio and Workbench runners are introduced
by the next source slice. Current-source compiled/runtime/package/visual and independent review evidence,
protected merge and resulting-main qualification remain required by
[spec1212](../../../specs/1212-external-authentication/spec.md). Historical results on
[closed PR3806](https://github.com/elsa-workflows/elsa-foundation/pull/3806) remain bound to their recorded source.
