# Research: integrated Studio UX qualification

## Decisions

- Use the existing #3075 finite matrix and owner decisions. All 24 prerequisites are accepted; leaf receipts remain historical evidence, not composed acceptance.
- Retain ADR0022 navigation and the current module composition. Correct only reproduced in-scope defects; do not activate optional modules or invent backend contracts.
- Use real normal hosts for composed journeys and label synthetic negative controls separately. Bind Test Run state/layout to the reviewed publication and preserve transient/durable identity distinctions.
- Keep credentials/configuration private and temporary. Record bounded request metadata, hashes, screenshots and cleanup; never raw credentials, secret values or arbitrary provider payloads.
- Reconcile existing design spec/UI README/polish-plan guidance instead of creating a new design-system contract. Independently bundled modules still use stable SDK contracts/tokens.
- Root owns integration and final QA. Bounded delegated source preparation is reviewed by root before use. Delivery retains all gates and a separate non-author application approval/merge.

## Baseline findings

Preliminary authoring attempts at reviewed Resources head3bd4c59 exposed harness assumptions: duplicate Create buttons, visually hidden native radio inputs, incorrect inspector label, asynchronous focus restoration, and the distinction between inline insertion and the side palette. Their failed receipts and cleanup remain external; none is a product pass or an established layout defect.

A source review also replaced the undefined `headers()` expression with an ordinary JavaScript object authored through the real dictionary syntax picker. The final HTTP200 remains to be observed.
