# Activity Definition member editor UX

Issue: #3860. Program: Elsa 4 Simplification (#2559). Owner request: annotated editor screenshot, 5 November 2026.

Authors edit activity inputs/outputs in focused General, Presentation and Advanced tabs. General owns name, type, description, required and default. Presentation owns display name, category, order and UI hint. Advanced owns nullability, collection, storage and stable identity. Tab switches preserve local default text; diagnostics reveal the correct tab and exact field. Outcomes retain their simpler editor.

Remove duplicate type alias/durability cards and implementation commentary. Storage selection exposes only compatible active drivers, preserves unavailable saved values and explains a sole choice. Category suggests existing contract categories and accepts new values. Reference-key application is a compact accessible icon beside its input and retains published-key review.

Acceptance reference: [module UI](../../Studio/docs/module-ui.md), existing Activity Definition authoring tabs. Inspect normal/empty, read-only, unavailable, invalid default, persistence, diagnostic focus, keyboard, narrow, light/dark states on the composed host. Fixture tests establish component behavior only. Current-commit artifacts and independent visual review are required by [UX acceptance](../../docs/contributing/studio-ux-acceptance.md).

No backend/schema/runtime or provider-contract changes. Fixed durability remains required.
