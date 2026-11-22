# Actual 6e diagnostics stale failure — independent acceptance adjudication

Source: clean `6e86422a297f98a309e4b0ba1c0579b1cf62c707`.

Failed/partial campaign: `/Users/sipke/.codex/recoveries/3849-delivery-2026-11-21/studio-diagnostics-6e86422a-20261122-a1`.

Actual reviewed case directory: `results/journeys-timeline-durable-diagnostics-and-process-restart-desktop-light`.

This is failure adjudication only. It does not convert the failed campaign into acceptance, approve the preceding diagnostic captures, or establish final cleanup/export/privacy/download acceptance. No .NET/builds/product tests, backend/control/source edits or peer reports used. Read-only Python parsed the actual trace and the actual failed PNG was opened.

## Finding

The first missing ID in the stale comparison was a separate selected-observation shortcut, not a row lost from the loaded Timeline event list. The failure came from recording a broad region selector before a legitimate change of selection, then comparing it after that change and a network failure.

Actual trace `0-trace.trace`:

- `call@486`, method `evalOnSelectorAll`, selector `Run timeline >> [data-history-event-id]`, at46843.578: baseline31 IDs.
- Actual snapshot `before@call@486`, timestamp46845.718: the first ID `history:7645dbf05e17e1e1855f4a81a116ef4296845f9ddb6a3fe642a4016a7c640d2d` is on a BUTTON under `ASIDE.wf-playback-selected-observation[aria-label="Selected recorded observation"]`. It is outside `OL[aria-label="Run history events"]`.
- `call@488`, actual Enter at46859.369, selects `delivery-history:985c3c4aeabf451615d558eb37abed29a6880f355ce08e1ed533c707b86b2cb4`.
- `call@544`, same broad eval selector at49052.875: stale30 IDs.
- Independently extracted actual call results: `baseline[1:] == stale` exactly, in order. The removed shortcut is the sole difference; there is no changed/missing loaded-list row in this comparison.

This matches source `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/run-history/RunHistoryTimeline.tsx:31–33,78–81`: the currently selected recorded event gets a separate shortcut when it is outside visible Highlights; selecting a visible row removes that shortcut. Both shortcut and actual row buttons intentionally have data-history-event-id. `timelineDiagnosticsJourney.ts:194–203` incorrectly includes the shortcut in its pre-selection row baseline.

The opened actual `test-failed-1.png` shows Highlights,30 shown/51 loaded, readable retained Timeline rows, visible “Showing last confirmed history” and Retry, and selected Delivery dispatched details. It supports the actual trace/source classification; it is not evidence that a fresh corrected all-events stale/recovery case passed.

## Correct finite fixture change

Explicitly choose All events for this diagnostics scenario, scope the row selector to the actual `list[aria-label="Run history events"]`, choose/await the exact new selected event and settled details first, and only then record the pre-network-failure loaded-row baseline. Keep the strict ordered baseline/stale/recovered equality, actual native-ID membership, exact selected row/details and visible usable Retry assertions. Preserve the genuine renderer request abort and real Retry recovery. Do not weaken the assertions, fabricate response JSON, or modify production behavior to retain an obsolete selected-event shortcut.

## Artifact identity

- Actual trace.zip SHA256: `eb0eb25d16f6e924478d0232f1cda4afe09df67c704e473505c0a958c31a60d0`.
- Opened test-failed-1.png SHA256: `c7389362c1901578950d89cabcb0c0b4e2df798b84b20b26dd162da5714e81aa`.

No new runtime lost-row defect is established by this failure. The campaign remains failed/partial and must retain its original source/status. Fresh corrected-source diagnostics execution and actual all-capture review are still required.
