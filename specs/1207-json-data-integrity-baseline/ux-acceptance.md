# S-9 Studio acceptance

User goal: read and enter workflow times in a chosen time zone without silently changing the instant or guessing the server's zone.

Reference: the existing Settings › Appearance cards, standard Run workflow dialog, instance From/To controls and timestamp typography. Preserve their keyboard, validation, busy, read-only and responsive behavior.

The [composed timezone journey](../../Studio/tests/acceptance/timeZoneJourney.ts) uses real publication/runtime APIs, retained consumer input evidence and a transparent request gate for the busy state. Its 16 required captures run in all four viewport/theme projects. At `c97ad9007b17207b61a16ff1513c2ce8325d13e8`, the first producer failed before browser launch (missing pinned executable). The isolated pinned-browser retry exercised all four projects through the DST-gap error, then all four failed opener focus restoration after Escape. Both failures are retained; the focus repair, full journeys in both backend hosts and independent visual inspection remain required.

Required states and journeys, all on a composed host from the final reviewed commit:

The focus repair is verified in the composed retry at `2d85fef264b3dc1450a4e0aff167e1662b85af90`; all four projects then reached the first real run request. That attempt failed because its oracle required added `:00` seconds while the valid explicit-offset request preserved the entered minute precision. The oracle is corrected to require the exact minute-precision wire value and offset; retained input evidence remains an exact equality check. This failed attempt is retained alongside the earlier failures.

The exact `87ae0a2dd58e0c4ca4b898e6671d2e88bbb0d187` Workbench retry built both hosts and reached all four browser
projects, then failed an ambiguous Appearance preview locator because the header also contains a timestamp. That failed
run remains retained. The prepared repair scopes the preview, checks exact civil date/time/offset against retained API
instants, uses a non-UTC browser default and changes Settings in a second real tab while the inspected view stays mounted.
Every owned context page participates in the uncaught-error ledger.

Prepared timer, playback, native-dashboard and comparison journeys now check their actual history/bookmark/cursor,
revision, generation, typed record and period timestamps. Authored query zones and retained raw instants remain independent
of display preference. Before this checkpoint, candidate typechecks passed and affected whole Dashboard 225/Workflow 5353
tests passed; both new date-consumer controls failed against the original presentation code and passed repaired. These
candidate results do not establish composed or visual acceptance. The table below stays open until exact-head runs and
independent image inspection complete.

| Journey | Required evidence | State |
|---|---|---|
| Settings | Browser default; UTC and Europe/Amsterdam selection; saved preference after reload; Reset restores browser; invalid stored zone falls back visibly | Not run |
| All date surfaces | Shared formatter; explicit offsets in workflow/activity/instance/BPMN, module, notification and dashboard dates; valid missing/sentinel rendering | Not run |
| Run workflow | Amsterdam winter and summer wall times send +01:00 and +02:00; UTC sends +00:00; visible selected zone and offset; actual API request and retained/run result prove the instant | Not run |
| Filters | Instance From/To use the same preference and preserve their selected instants | Not run |
| Invalid input | Impossible calendar dates and DST gaps remain editable with an associated error; no run request is sent | Not run |
| Presentation | Light, dark, narrow and keyboard/focus/error/busy/read-only states inspected against the reference | Not run |

Retain exact-head screenshots, traces, request/response evidence and the journey result ledger as downloadable PR artifacts. The independent acceptance reviewer inspects the composed screenshots and complete affected journeys; unit/fixture results alone do not complete this record. Keep each failed/interrupted/not-run state until that exact journey is repaired and rerun. CI is deferred per coordinator dispatch; these local acceptance states are not waived.
