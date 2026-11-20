# Activity Definition member editor UX

**Status**: Implemented — effective when PR #3861 merges; acceptance remains gated by its evidence.

Issue: #3860. Program: Elsa 4 Simplification (#2559). Owner request: annotated editor screenshot, 5 November 2026.

Authors edit activity inputs/outputs in focused General, Presentation and Advanced tabs. General owns name, type, description, required and default. Presentation owns display name, category, order and UI hint. Advanced owns nullability, collection, storage and stable identity. Tab switches preserve local default text; diagnostics reveal the correct tab and exact field. Outcomes retain their simpler editor.

Remove duplicate type alias/durability cards and implementation commentary. Storage selection exposes only compatible active drivers, preserves unavailable saved values and explains a sole choice. Category suggests existing contract categories and accepts new values. Reference-key application is a compact accessible icon beside its input and retains published-key review.

Acceptance reference: [module UI](../../Studio/docs/module-ui.md), existing Activity Definition authoring tabs. The composed host verifies normal/empty input/output authoring, persistence, staged-default retention, keyboard operation and desktop/narrow light/dark layouts. Focused tests additionally verify read-only and unavailable driver selection, invalid default recovery, published-key confirmation and exact diagnostic focus. These supporting tests do not independently establish product acceptance. Current-commit composed artifacts and independent visual review remain required by [UX acceptance](../../docs/contributing/studio-ux-acceptance.md).

Coverage clarification, 5 November 2026: acting on behalf of the owner under the delegated overnight delivery mandate, the agent selects this bounded matrix under ADR 0080 risk-based testing. Permission changes and server module/driver removal are outside this frontend layout change, so their full live-host permutations are excluded; the existing authority boundaries and relevant negative control states retain focused regression coverage. The initial wording required every listed state on a live host and was broader than the screenshot-derived change. Prior not-run states and failures stay visible in the PR evidence. This clarification does not waive a failing check, missing downloadable evidence or an independent review finding, and is not a claim of personal human inspection.

No backend/schema/runtime or provider-contract changes. Fixed durability remains required.
