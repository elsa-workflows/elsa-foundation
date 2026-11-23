# S-9 Studio acceptance

User goal: read and enter workflow times in a chosen time zone without silently changing the instant or guessing the server's zone.

Reference: the existing Settings › Appearance cards, standard Run workflow dialog, instance From/To controls and timestamp typography. Preserve their keyboard, validation, busy, read-only and responsive behavior.

The [composed timezone journey](../../Studio/tests/acceptance/timeZoneJourney.ts) uses real publication/runtime APIs, retained consumer input evidence and a transparent request gate for the busy state. Its 15 required captures run in all four viewport/theme projects. It has not yet executed; both backend hosts and independent visual inspection remain required.

Required states and journeys, all on a composed host from the final reviewed commit:

| Journey | Required evidence | State |
|---|---|---|
| Settings | Browser default; UTC and Europe/Amsterdam selection; saved preference after reload; Reset restores browser; invalid stored zone falls back visibly | Not run |
| All date surfaces | Shared formatter; explicit offsets in workflow/activity/instance/BPMN, module, notification and dashboard dates; valid missing/sentinel rendering | Not run |
| Run workflow | Amsterdam winter and summer wall times send +01:00 and +02:00; UTC sends +00:00; visible selected zone and offset; actual API request and retained/run result prove the instant | Not run |
| Filters | Instance From/To use the same preference and preserve their selected instants | Not run |
| Invalid input | Impossible calendar dates and DST gaps remain editable with an associated error; no run request is sent | Not run |
| Presentation | Light, dark, narrow and keyboard/focus/error/busy/read-only states inspected against the reference | Not run |

Retain exact-head screenshots, traces, request/response evidence and the journey result ledger as downloadable PR artifacts. The independent acceptance reviewer inspects the composed screenshots and complete affected journeys; unit/fixture results alone do not complete this record. Keep each failed/interrupted/not-run state until that exact journey is repaired and rerun. CI is deferred per coordinator dispatch; these local acceptance states are not waived.
