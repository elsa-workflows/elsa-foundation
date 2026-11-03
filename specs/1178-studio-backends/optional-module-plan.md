# Optional module implementation plan — #3596

## Boundaries

- New optional RCL `Studio/src/essentials/Elsa.Studio.Backends`; public, non-sealed `BackendsStudioFeature`, virtual registration, normal StudioModule manifest, standard module frontend bundling. Only this implementation references Preferences.Core. Profile API maps through the active Studio shell's IWebShellFeature; no bespoke endpoint router.
- Studio.Core defines portable authority and runtime-configuration contracts. Authority is a replacement contract with one immutable root-host snapshot; zero custom sources uses configured single-backend settings, duplicate registrations are refused. An explicit optional Backends host-registration adapter provides profile-default compatibility at startup only when that feature is enabled in the effective composed selected-shell blueprint (including replacement registry providers), before shell activation or dependent client resolution. Core authentication, server bridge and runtime projection consume the same snapshot, preserving explicit browser/server URL precedence.
- Runtime extensions use a named collection event and one aggregating handler/source fan-in. Hosting borrows the selected Studio shell, materializes public settings, validates unique namespaces, serializes before scope disposal, and returns503 when activation is unavailable. Core fields cannot be overwritten. Backends projects only allowlisted public metadata and its actual profile API path under its namespace.
- Move verified tenant claim identity to neutral Studio.Api authorization. Profile services/namespace stay feature-local. Default metadata store is weakly associated with a fresh host-owned authority snapshot shared by reference with shells inside the optional module; external stores override it. Verify reload and two-host isolation rather than relying on registration assumptions.

## Client lifecycle

- Extract immutable backend lifetime/transport/discovery into public SDK support; keep host auth context outside management.
- Split module import/instance initialization from registry registration. Legacy register(api) remains supported; an optional initialization factory gets stable host context/auth and namespaced configuration and returns a document-owned module instance. Register normal modules against the final session only.
- A single owned connection slot selects zero custom providers => configured core connection, one => custom provider, multiple => explicit error. Instances capture controllers; no module globals or module-ID switches in core. Dispose/cancel failed initialization and retire lifetimes on document exit.
- Generic composed shell-action/notice contributions replace hardcoded switcher/notices/recovery. Backends registers its own routes/navigation/actions and holds its own controller closure. Failure recovery uses ordinary host settings plus a generic safe Home reset.
- Module-manifest lifecycle changes use guarded full-document navigation, retaining existing beforeunload protection, so disable/re-enable cannot retain a foreign context or module cache. Capability refresh alone rebuilds contribution admission without creating a new controller.

## Qualification and artifacts

Update the reference host to explicitly compose the package/feature/authority adapter. Update package identities, solution/workspace/build/lint maps, Docker and package producer closures; restore minimal Hosting absence of Preferences. Add behavior tests for generic contracts and lifecycle, rendered disabled/absent scenarios and real composed enabled/disabled/re-enabled journeys. Run affected whole suites, generated maps/architecture and package consumers; prove new controls red before their fixes. Record exact-head and resulting-main evidence externally. No constitution exception, EF provider or new persistence schema is needed.

Review refinements: root authority values are copied into a fresh host-owned snapshot; a cancelled module-change reset stays queued until the draft/write guard releases, with recovery visible. Client-only DisabledModuleIds suppression does not claim server-feature removal. Prefixed profile API URLs are projected explicitly. No source objects or shell providers escape runtime serialization.
