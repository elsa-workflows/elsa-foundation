# Studio UI and UX assessment, 18 October 2026

Owner request: assess the product holistically, record issues and repair them. Tracking: [#2956](https://github.com/elsa-workflows/elsa-foundation/issues/2956).

The assessment started from main `75bb7b1993b2a274ed58ae2bc9dcf3cb137b11ab`. It used a separate Studio and Workbench with disposable SQLite data, leaving the existing user services and data intact. Manual browser checks used the real authenticated application; automated browser checks used the repository's controlled fixtures. Source audits supplied hypotheses, not evidence of a live defect.

## Journeys and scope

| User goal | Assessment |
| --- | --- |
| Understand Home and customize it | Checked empty and populated widgets, getting-started completion, hide, reorder, resize, save server default and reset personal layout. Reproduced two defects and verified persistence of the repaired default. |
| Create and run a workflow | Created a Flowchart, inserted/configured Write Line, published 1.0.0, ran its draft, opened the linked run and inspected timeline and incident empty state. The normal path worked. Delayed/failed catalog cases were reproduced with focused component tests. |
| Design and reuse an activity | Created a Sequence activity, inserted Write Line, added a public String input, reviewed/published 1.0.0, placed it into a Sequence workflow and followed its exact source-definition link. With an explicit input, the run completed and inspection showed the evaluated value and Done outcome. Omitted-input testing exposed a late compiler/materialization failure. |
| Find executable artifacts | Checked the named workflow's published artifact and inspect/run navigation. Existing executable and publication browser coverage was retained. |
| Manage a secret | Created a disposable Text secret, toggled visibility of the entered value, opened its separate editor and inspected Rotate. Persisted secret material was never retrieved. |
| Configure the application | Checked Appearance, Studio modules and package-feed controls. Feed Add focus/validation/Escape and Edit submission with Enter were checked without installing packages or changing feed values; the QA checkout’s serialization-only configuration write was restored. |
| Work with keyboard or a narrow screen | Reproduced focus escaping dialogs, unavailable Escape dismissal and narrow navigation height expansion. Checked 390px live layout, keyboard focus scrolling the Secrets link fully into view, and automated 390/768/1024px navigation stability/reachability, plus existing responsive activity-definition coverage. |

## Findings and repairs

| Finding | Impact and reproduction | Repair and evidence |
| --- | --- | --- |
| Home movement counts hidden entries | Hide the middle widget, then move the first visible widget later. It stays in place and announces position 1. | Move between visible, available positions; regression and live check announce position 2 of 2. Hidden/unavailable entries remain stored. |
| Existing Home default cannot be updated | Save a server default, edit a widget, save again: generic failure. | Read the default's own revision and use its compare-and-swap precondition. Personal-layout revision remains separate. Tests cover repeated saves and conflicts; live reset recovered the new wide widget setting. |
| Modal keyboard behavior differs across dialogs | Create Workflow allows Tab into Console; Secrets Create/Rotate and Feed Add/Edit leave focus behind; Escape does nothing. | Shared SDK focus behavior places/traps/restores focus and handles Escape. It preserves autofocus, StrictMode replay, editor-handled keys and Secrets row-refresh fallback. Feed forms support Enter. |
| Secrets can close during submission | Escape is protected but Cancel still closes the dialog while a request is pending. Found by independent review. | Disable Cancel and guard duplicate submission; regression holds an unresolved request and checks both close paths. |
| Workflow creation races catalog loading | A forced submit while the catalog is pending sends no root activity. | Loading/failure feedback and Retry; dialog and parent submission guards; unavailable or template-less roots cannot be submitted. Default root selection is shared with the radio cards and Activity Definition creation. |
| Runtime says `published vdraft` | Publish a workflow, test its unchanged draft, then inspect Runtime. | Explain behavioral equivalence without treating an executable artifact's label as a publication version. |
| Designed-activity interface is too technical by default | A new input immediately shows identity, CLR, durability and storage-driver details. | Keep name, type, collection, requiredness, nullability and defaults visible. Move identity, schema, storage and presentation controls under Advanced; retain unavailable-type warnings and stored values. |
| First activity publication is overly dense | Initial publication fills the dialog with compatibility groups and successful technical checks, pushing the action down. | First-version contents and successful technical checks are collapsed; version/action stay prominent. Actual diagnostics, blocked checks and changes to an existing version remain visible. |
| Omitted boundary input faults after dispatch | Optional, non-nullable String with no default is omitted. Designer reports valid; Test Run is accepted and later faults because a contract key has no binding. | Compile omissions through the existing input policy: unsupported omissions fail before dispatch, nullable omissions receive an absent binding, supported value types receive their pinned default. Required/default/secret policies are preserved. Five focused compiler/materialization regressions reproduced the failure and verify the repair. A fresh unbound placement was also rejected during live Test Run setup before dispatch; an explicitly bound run completed. |
| More tools expands narrow navigation height | At 390px, opening the disclosure adds about 190px because its panel keeps desktop grid layout inside the horizontal strip. | Keep the panel in the strip's horizontal flow. Browser regression fails before the CSS fix and passes at 390, 768 and 1024px. |

## Review and verification

Independent correctness, standards/accessibility and acceptance reviews found and drove fixes for pending-save cancellation, unavailable roots, root-card/default-state disagreement and post-publication wording. Fresh independent correctness, standards/accessibility and acceptance reviews followed the fixes. The PR evidence records the final reviewed commit and checks.

Regression evidence is specific to behavior: focus/keyboard boundaries, catalog loading and retry, default concurrency, visible movement, omission compilation/materialization and viewport geometry. Build and test results are recorded in the PR rather than treated as implied by this report.

## Limits and decisions

- This is a broad, bounded assessment, not proof of every activity type, expression language, authentication policy or third-party package.
- Public input creation, reuse, evaluated input evidence and the default Done outcome were exercised live. Arbitrary custom output mappings and every outcome combination were not exhaustively authored in this pass; existing dedicated tests remain the evidence for those combinations.
- No production data, real credentials, user feed values or package installations were changed. Server module management remains configuration-dependent in the isolated environment.
- Rebuilding modules during the assessment invalidated already-loaded lazy chunks. The existing reload recovery worked; those deliberate development-time interruptions were not reported as product defects.
- Pointer drag-and-drop was not re-proven manually here; insertion from the palette and the repository's activity placement regressions supplied the placement coverage.
- No complete WCAG compliance claim is made. Keyboard regressions and the selected browser suites' automated accessibility checks are narrower evidence.
