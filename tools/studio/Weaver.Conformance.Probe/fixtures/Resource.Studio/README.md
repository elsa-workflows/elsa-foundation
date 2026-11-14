# External Studio module author

This fixture is materialized outside the repository. It imports only React and the actual packaged public Studio SDK. It has no Dashboard/private/source aliases or product ProjectReference. Its RCL consumes prebuilt `wwwroot/studio/modules/acme-home/module.js` and `module.css`; ordinary host restore/build/publish never invokes Node.

The module's JSX build emits `React.createElement` through its existing React import. Its only external runtime imports are the host-provided `react` and `@elsa-workflows/studio-sdk` entries; it does not require an additional `react/jsx-runtime` import-map entry.

Replace these tokens in the copied templates, then remove the `.in` suffixes:

| Token | Required input |
| --- | --- |
| `__PRODUCT_VERSION__` | Exact compatible local `Elsa.Studio.Core` NuGet version from the product receipt |
| `__CSHELLS_VERSION__` | Exact `CShells.Abstractions` version supplied by the producer |
| `__FIXTURE_VERSION__` | This independently authored `Acme.Home.Studio` package version |
| `__SDK_TGZ__` | Absolute path to the real retained Studio SDK tarball; dependency is `file:<path>` |
| `__REQUIRED_SDK_VERSION__` | `*` for the compatible fixture; the qualifier selects a genuine unsupported range for its incompatible variant |

Provide the copied author's `NuGet.Config` with deliberate mappings to the retained product feed and public dependency sources. The author needs Node/pnpm, TypeScript/Vite and .NET 10; the consuming host needs only the packed RCL and its public dependency closure.

From the copied external author's directory:

```sh
pnpm --dir Client install --lockfile-only
pnpm --dir Client install --frozen-lockfile
pnpm --dir Client typecheck
pnpm --dir Client build
dotnet restore Acme.Home.Studio.csproj --configfile NuGet.Config
dotnet restore Acme.Home.Studio.csproj --configfile NuGet.Config --locked-mode
dotnet pack Acme.Home.Studio.csproj -c Release --no-restore -o /absolute/owned/feed
```

Retain the SDK tarball hash, author's lockfile/resolved SDK declaration entry, typecheck/build logs, packed RCL hash and packaged/served JS/CSS hashes. No command here publishes a package or starts a host.

The settings route `/qualification/acme-home` stays registered regardless of the native widget's saved visibility. It launches the generic protocol fixture's JSON tool requests (`toolName`, `arguments`) with module-owned labels. Its registered context provider supplies the exact current resource reference; the prompt does not add a second reference to that same resource. Both read and reviewed visibility use the normal Weaver path; the frontend never writes the preference document. Result presentation refreshes the authorized owning endpoint and exposes an ordinary native Home reload link only after matching server receipt/readback. The browser driver must click that link and observe the native Home layout independently.

## Leaf-0 effects author

`Client/src/effects.tsx`, registered from the existing entry point, contributes a sibling route `/qualification/acme-home-effects`. Its `acme.home.effects-context` provider is selected only on that surface, with the distinct `acme.home.effects` schema-1 kind and the backend owner's closed nine-code set. It supplies a current owner reference, not installed guidance or self-asserted authority. The backend source rereads and admits it and supplies the module's installed guidance. No explicit duplicate attachment is added to a dispatched prompt. Legacy settings/Home requests do not collect this declaring context; the original read, visibility, size and unsaved size-choice registrations and their lifetimes are unchanged.

The five classed visibility server contracts have the exact backend ids, descriptor risks, `{visible:boolean}` input and `acme.home.widget-visibility` result shape: administrative, administrative+irrecoverable, Admin with omitted Administrative, preparation-raised Admin and Activation. They reuse the backend-declared `acme.home.widget-result` renderer and its independent saved-resource readback. Required/effective classes, configured policy, approval, cancellation, busy state and recovery remain in shared Weaver; the author does not compute or override them. An irrecoverable-class profile exercises that policy requirement using the same real personal visibility change, not a physically irreversible operation.

`acme_home_check_widget` is server/Admin, declares `canPerformWithoutPersistence:true`, accepts exactly `{expectedVisible:boolean}`, and uses the owner's `acme.home.check-result` schema1 and `acme.home.check-result-renderer`. The module presents the frozen check plan without treating it as a run. A terminal candidate must correlate the original module/tool/contract, invocation/tool-call ids, exact widget/unchanged revision, server/no-client location, expected visibility, no-persisted-effect receipt, typed module-code field, complete result and identical evidence. Shared normalization may replace an unsupported typed code without discarding a genuine receipt; this renderer derives the business verdict from its bounded payload and native observations, and never displays raw result content or codes. Business false is still a check that ran; malformed, foreign or ambiguous results stay unconfirmed.

The check renderer independently reads both `GET /_qualification/acme/home-widget` and `GET /_qualification/acme/home-widget/checks`. It requires the same native widget/revision and actual visibility, plus a matching journal invocation/tool-call/tool/revision/business verdict/call number. It displays the actual owner count. Multiple recent physical rows for an invocation are reported as duplicate evidence rather than deduplicated. Only the owner's last 16 rows are returned; an absent historical row remains unconfirmed and the renderer makes no lifetime exactly-once claim. A changed revision is historical/stale evidence. Unavailable or incompatible reads display fixed safe text, never raw backend errors. An explicit read refresh repeats only GETs. It never applies, saves, changes an outcome status, retries a tool or reconstructs local working state.

Owner reads are cancelled on component/route teardown and bound to the current invocation/result; late reads cannot confirm another card. The new page's unsaved desired visibility/check expectation survives its own read refresh. The legacy page's unsaved size choice retains its existing mount-generation lifetime and explicit normal resize/save path. No new server operation writes or automatically saves that local choice.

## Required composed acceptance — not executed by authoring

User goal: from the effects page, request each classed change or a check, inspect the shared server-owned requirements/review, and independently distinguish saved visibility, a check that ran, a failed business condition, and missing confirmation. Reference: the existing Acme settings/results and shared Weaver review; the new page reuses their CSS/ordinary controls and checkbox geometry.

The existing external author has SDK typecheck/build scripts and no component-test runner. This change adds no test framework or source-SDK test shortcut. Its typecheck/build and every native journey remain **UNRUN** until the authorized qualification lane executes the packed author against the retained SDK archive. The backend fixture has233 source-authored cases, including16 preparation-observation controls; this is no execution claim. T037/T038/T039 retain ownership of driver, restart and final roster changes.

Required native cases on both actual opaque provider adapters:

- Preserve every original read/visibility/size/unsaved-choice journey and the held legacy paths; new declaring context is absent from legacy requests. Selecting/deselecting the module removes its page, context, contracts and renderer without replacing other support.
- Exercise all five classed profiles under unconfigured, administrative-only, all-enabled and policy-changed-at-approval/execution/recovery states. Inspect actual frozen/effective classes, raised risk, approval and genuine native saved visibility readback.
- Check both business pass and fail with unchanged complete native widget/revision and no preference write. Confirm the same receipt invocation/tool-call/verdict/call number in owner GETs, truthful generic Performed status, no Apply control and no persisted/applied label for the check.
- Reject confirmation for wrong module/tool/contract/target/revision/scope/host/correlation, mismatched or incomplete evidence/payload, malformed journal shape/count/order, wrong verdict and no matching row. A valid HTTP result or shared envelope alone cannot confirm a run.
- Record actual duplicate physical rows without hiding them; verify normal duplicate/late/reopened/recovered delivery does not create extra owner calls. Keep generic uncertain/busy/status meaning intact. Closing/reopening or changing result identity cancels old reads; explicit read refresh causes no action.
- Cover denied/unavailable owner reads, stale widget revision, old receipt outside the 16-row window, pending read/loading, missing module/backend, safe error text and recovery. No action may be dispatched after leaving the page during its availability read.
- Inspect keyboard/focus, checkbox pointer stability, narrow and light/dark layouts, module removal while nested, and local unsaved choices during refresh and normal save. Retain current-source downloadable screenshots/traces and obtain independent UX acceptance. Source review/typecheck alone cannot close this gate.
