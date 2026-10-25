# Research: integrated Studio UX qualification

## Decisions

- Use the existing #3075 finite matrix and owner decisions. All 24 prerequisites are accepted; leaf receipts remain historical evidence, not composed acceptance.
- Retain [Studio ADR0022 navigation placement](../../Studio/docs/adr/0022-let-the-user-choose-where-the-navigation-sits.md) and the current module composition. Correct only reproduced in-scope defects; do not activate optional modules or invent backend contracts.
- Use real normal hosts for composed journeys and label synthetic negative controls separately. Bind Test Run state/layout to the reviewed publication and preserve transient/durable identity distinctions.
- Keep credentials/configuration private and temporary. Record bounded request metadata, hashes, screenshots and cleanup; never raw credentials, secret values or arbitrary provider payloads.
- Reconcile existing design spec/UI README/polish-plan guidance instead of creating a new design-system contract. Independently bundled modules use the public UI package, stable SDK contracts and tokens; `DialogHost` remains host-only.
- Root owns integration and final QA. Bounded delegated source preparation is reviewed by root before use. Delivery retains all gates and a separate non-author application approval/merge.

## Baseline findings

Preliminary authoring attempts at reviewed Resources head3bd4c59 exposed harness assumptions: duplicate Create buttons, visually hidden native radio inputs, incorrect inspector label, asynchronous focus restoration, and the distinction between inline insertion and the side palette. Their failed receipts and cleanup remain external; none is a product pass or an established layout defect.

A source review also replaced the undefined `headers()` expression with an ordinary JavaScript object authored through the real dictionary syntax picker. The final HTTP200 remains to be observed.

## Reproduced correction: clearing a Connection

At candidate `d6fb3bfb54e9a375bb8e81231ce5b362c02c69de`, the composed normal-host run selected an HTTP Connection, exercised a controlled picker failure and actual Retry recovery, then selected the blank option and saved. Test Run reported `compile.failed`: the saved Connection expression carried no object reference payload. This is a product failure after a valid UI clear, distinct from the controlled picker outage and earlier harness failures.

The picker emits `null` for its blank option. The properties panel preserves the Connection syntax around that value, while the publishing compiler requires an object payload for an authored Connection binding. The focused correction must remove the cleared binding, preserve unrelated inputs and allow selecting a connection again. Add a panel-to-wire behavioral regression and a revert/mutation control, then rerun the actual Test Run/publication journey. No backend validation relaxation or provider activation is required. The failed run remains unaccepted; runtime success is pending.
