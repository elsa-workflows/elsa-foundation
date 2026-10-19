# Research: Task-first Runs triage in Studio

**Source baseline**: `42226851b85385ea764658db8d816fcad8a3bea7`. Decisions reviewed for #3062 before implementation. No new backend or domain contract.

## Findings and decisions

### Preserve existing incident and list contracts

Spec 192 and its incident contract already define active health separately from lifecycle status: active means incidents are neither Resolved nor Suppressed; blocking is narrower. Health filtering is capability-aware and must be applied over the authorized result set before pagination. Reuse those meanings. Needs attention includes active nonblocking results; Running remains an independent lifecycle choice. Do not add or infer a recovery action.

The current Runs client already carries status, incident health, definition and exact run identifiers, date bounds, page size and cursor in its query model. Its runtime request accepts these separately and uses the existing paged/health-filter relations. No new backend endpoint or runtime name filter is required. Filtering must remain server-side: a search for workflow names chooses a definition identity and then filters by that identity.

The existing empty-page cursor recovery applies only when the cursor request succeeds with an empty page: it clears the cursor and retains filters and page size. A failed/expired cursor request is an error and has no equivalent recovery contract. Preserve this distinction.

### Keep the name chooser local and exact

The Workflows client already has definition search and exact-definition lookup helpers, plus run-row name hydration. Add only a small Runs-specific async chooser using those helpers. Search text is draft until a definition is selected; selection uses the stable workflow identity. Duplicate names need a visible identity/version distinction. If identity resolution fails, keep the selected raw identity available and distinguish the lookup error from no search matches. There is no runtime name-filter support to extend. The chooser replaces the separate definition-ID field, keeping one control for this predicate while preserving URL-provided IDs and copyable exact identities.

### Make advanced disclosure truthful and reversible

The current public `AdvancedSection` combines its applied count with force-open behavior. Root accepted a narrow additive opt-in `allowCollapseWhenSet` prop with its default remaining false. In Runs, pass the real count of applied advanced criteria; the outside chips provide the persistent summary, while draft fields remain mounted through collapse. Explicit force-open and error behavior keep their priority. Do not import the private low-level disclosure component, use a technical-details presentation for editable filters, or lie by passing a zero count.

The disclosure regression tests live under the Studio Web Client app, not in the `studio-ui` package. Run the focused `src/app/ui/disclosure/__tests__/disclosure.test.tsx` test and the full Studio Web Client suite on the final candidate; the `studio-ui` test script has `passWithNoTests` and does not prove disclosure behavior. Prove the opt-in and unchanged default/force-open/error behavior. Runs tests should prove that collapse/reopen preserves unsubmitted values and that Apply updates query, chips, and count together.

### Preserve the existing row and deletion interactions

The current run table leads with execution/artifact identifiers and places the workflow name later. Reorder the identity content so the human name leads, with exact run, artifact, definition and version identifiers still visible and explicitly copyable. Preserve row Enter/Space inspection, checkbox selection, per-run controls and deletion safeguards. Hide only the bulk toolbar until selection; retain its existing capability/permission gate and deletion semantics.

### Return context belongs to navigation state

The current list serializes query/page state in the URL; opening a run currently discards it and the detail Back action returns to an unfiltered Runs path. Carry the complete local list destination through the detail route and validate it before navigation. Direct detail links fall back to the base Runs path. Test both a real detail route round-trip from page two and ordinary browser Back.

### Keep the architectural change bounded

The shared disclosure remains an existing Studio UI component; the single optional prop is the only new shared UI contract. The picker remains local to the Workflows client and consumes existing public helpers. There is no new domain, backend API, runtime behavior, persistence, dependency, module, or generalized list framework.

The repository Feature Map documents C# shell features. It does not enumerate TypeScript Studio packages and does not need a new feature entry for this client-only change. No generated architecture-map change is expected from the described source scope.

## Alternatives rejected

- **Treat Needs attention as blocking**: rejected because that excludes active nonblocking evidence and disagrees with the Home view and Spec 192.
- **Filter run rows by typed name in the browser**: rejected because only one page may be loaded and the runtime list has no name predicate. Search definitions and select the exact identity.
- **Add a backend name-filter contract**: rejected because existing definition search plus identity filtering serves this journey.
- **Use private disclosure source or a read-only details fold**: rejected because the module boundary has a public advanced-form component; the narrow opt-in preserves its accurate count and form styling.
- **Force the filter section open or suppress its count**: rejected because applied chips make collapse understandable and the count must remain truthful.
- **Build a reusable table/list framework**: rejected because the issue uses Runs as a reference convention and does not justify broad abstraction.
- **Treat a cursor request error like an empty page**: rejected because only successful empty-page recovery is established.
- **Add performance measurements or timing targets**: rejected because performance is explicitly outside this program slice.

## Constitution and review-checklist notes

Reviewed the Gate indexes in framework constitution v6.1.0 and Elsa constitution v6.0.2, plus `docs/contributing/review-checklist.md`.

- Framework §2.6 / §2.23.4: keep module coupling visible and consume shared UI through its public entry; no private cross-module import.
- Framework §2.23.1 / §2.23.2: exercise risky user behavior and preserve valid behavior; tests should prove state/query/navigation effects rather than asserting prose alone.
- Framework §2.23.3: the optional prop is a deliberate in-repository TypeScript API for another Studio module. Preserve the existing default. No C# public API file or NuGet contract is involved.
- Framework §4.2 and Elsa §E5: no C# package version line changes are expected. Verify workspace package compilation for the shared UI type and the Workflows consumer.
- Elsa §E2.2.3 / §E2.6: this does not touch Design/Runtime composition, runtime artifact behavior or the runtime API.
- No new C# feature-composition pattern is introduced; draft framework §2.24 is not an extra approval gate. The repository-required Architecture guard suite still runs as a delivery gate.
- Creating the numbered spec changes the generated spec-status map. Run the maps freshness check after the spec is added; refresh the affected generated map if stale and include changed generated paths (and the manifest if changed). A client-only source change does not waive that documentation-map gate.

## Risks and bounded verification

1. **Shared disclosure regression**: every existing consumer keeps force-open-on-count by default. Test the default unchanged and force/error precedence under opt-in.
2. **Definition search races or identity ambiguity**: disregard stale results; test duplicate names, exact selection, selected-ID hydration, and empty/error states.
3. **Unsafe or stale return location**: accept only a same-origin Runs-list destination; direct detail routes use the base list. A returned cursor that errors remains an error rather than becoming Empty.
4. **ID copyability and row semantics**: test actual copied values, name-first ordering, keyboard inspection, checkbox behavior, and the existing deletion guards.
5. **Pagination/filter composition**: use a deterministic server fixture with eleven active matches over two pages plus healthy, resolved-only and suppressed-only controls; filter before slicing. Exercise the actual detail page and return action rather than a navigation placeholder.
6. **Maps and shared gates**: source changes do not alter C# feature registration or dependency maps, but the newly numbered spec/status changes the generated spec-status map. Run the maps check and refresh that map if stale. Run the repository-required Architecture guard suite and other epic delivery gates; the client-only scope does not waive them.
