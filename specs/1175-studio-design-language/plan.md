# Implementation plan

1. Audit the running Studio and enumerate source registrations, including modules absent from the
   host. Separate rendered observations, source findings and intentional domain differences.
2. Add public page/header, collection toolbar/footer/table-container and result-state primitives.
   Keep fetching, selection, navigation and pagination in modules. Offer CSS recipes for native
   tables and existing ARIA tables, so adoption does not replace proven behavior.
3. Move existing shared primitive visual rules out of the host stylesheet into public component CSS.
   Package and export CSS alongside tokens, preserve CSS side effects, and use the same source in
   the host and standalone examples. No host context or new runtime dependency.
4. Migrate collection pages first, then administration, diagnostics, ETL, disabled/sample modules
   and specialist page frames. Delete replaced local presentation rules. Preserve graph geometry,
   stream virtualization, security controls and module-specific information layout.
5. Document the component composition, token ownership, accessibility and loading/error patterns.
   Update the sample module and external-host examples as executable adoption examples.
6. Validate affected unit suites, browser behavior/layout fixtures, theme modes, narrow layouts,
   prepared package consumption, lint/typecheck and repository gates. Independently review the
   exact head, integrate current main, and qualify the resulting main after delivery.

## Design review before implementation

- Reuses existing theme contract and public package; no parallel design system or host-only skin.
- Presentation is composable and opt-in, with no generic data-fetching or permission framework.
- Native table semantics and existing ARIA/keyboard contracts survive migration.
- Shared styling avoids private module selectors. Local CSS remains for domain layout only.
- The inventory, issue and PR carry progress and evidence; no new program or checkpoint issues.

## Verification boundaries

Live inspection uses the owner's existing local Studio read-only. ETL currently reports unavailable
API; enabled UI is not evidence of backend readiness. Fixture checks cover disabled/unavailable
surfaces. No user database reset or host feature change is needed for this presentation work.
