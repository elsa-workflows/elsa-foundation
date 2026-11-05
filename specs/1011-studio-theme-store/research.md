# Research: Reusable Studio Theme Store

## Package boundary and public surface

The accepted [hosting contract](../../docs/reports/studio-hosting-contract.md) fixes the name Elsa.Studio.Hosting.Themes and public StudioThemeStoreOptions. It explicitly permits private atomic-writer duplication and requires existing Api authorization plus the Hosting mutation gate. JSON endpoint models remain internal; tests use public HTTP with their own wire shapes. The three shell features are the supported discovery/composition types, kept nonsealed with virtual methods. No new persistence interface or optional dependency in base Hosting is needed.

## Persistence defect and compatibility

Existing Web ElsaThemeStoreApi.ReadStoreAsync merges directory definitions into the same ThemeStoreResponse later serialized by WriteStoreAsync. Visibility and default writes therefore persist directory definitions and overwrite shadowed managed values. #3380 deliberately retained this behavior while completing source-policy correctness; [the accepted #3360 follow-up](https://github.com/elsa-workflows/elsa-foundation/issues/3360#issuecomment-6406684983) requires correcting the persistence boundary here. Preserve all existing managed records; do not guess which old records were accidental copies. Explicit save/import/duplicate creates or replaces only intended managed IDs. Reads continue to prefer directory definitions.

## Installed configuration and feature ownership

Current Program binds a mutable app-only ThemeConfiguration and checks its mode manually. Replace it with normal typed options startup validation and copied applied settings. Configuration-section keys remain Themes, with separate writable paths. Existing source-mode parsing is trimmed/case-insensitive and rejects blank/unknown values. Existing shell features use the host's coarse Studio policy; do not invent module-management permission requirements for appearance.

## UI and lifetime

ThemeBuilder's delete handler selects nextStore.defaultThemeId directly, although a projected default may be empty. Select a real inventory entry or remain empty; management inventory and selectable picker themes are different. Existing effect fallback already chooses the first inventory entry; make the delete transition explicit and qualify it through real rendering. Existing directory service catches all exceptions, including cancellation: preserve cancellation rather than representing it as successful empty loading. Fixture hosts must stop before disposal and avoid unused process-console logging because the suite contains process-global console-stream tests.

No new upstream package or owner design decision is required. All selected behavior follows #3355/#3368/#3360 and the existing Studio Theme Store ownership ADR; no new theme ownership domain is introduced.
