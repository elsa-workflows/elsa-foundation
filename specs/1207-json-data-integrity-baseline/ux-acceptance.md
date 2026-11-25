# S-9 Studio acceptance

**Owner direction, 24 November 2026:** Studio acceptance is removed from this implementation stream. The active
`345f78c146bda1321994ee710621537dfd8afad9` native-dashboard rerun was stopped through its owned process tree and has an
`owner-stopped-studio-acceptance.json` receipt. Existing failed, interrupted and not-run states below remain incomplete.
Further composed journeys, Foundation feed acceptance and visual artifact delivery are outside this stream's remaining
delivery work. Code implementation, affected local backend checks, conflict resolution and independent local code review
continue. This direction does not turn earlier acceptance results into passes.

User goal: read and enter workflow times in a chosen time zone without silently changing the instant or guessing the server's zone.

Reference: the existing Settings › Appearance cards, standard Run workflow dialog, instance From/To controls and timestamp typography. Preserve their keyboard, validation, busy, read-only and responsive behavior.

The [composed timezone journey](../../Studio/tests/acceptance/timeZoneJourney.ts) uses real publication/runtime APIs, retained consumer input evidence and a transparent request gate for the busy state. The initial inventory had 16 captures in four viewport/theme projects; the current affected inventory has six desktop/360/390 light/dark projects. At `c97ad9007b17207b61a16ff1513c2ce8325d13e8`, the first producer failed before browser launch (missing pinned executable). The isolated pinned-browser retry exercised all four original projects through the DST-gap error, then all four failed opener focus restoration after Escape. Both failures are retained; the focus repair, full journeys in both backend hosts and independent visual inspection remain required.

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

At `872ce205319ebf87ba47d166abe0ff643a17b307`, all six timer cases passed. Independent inspection opened all 36
distinct images and verified their 36 attachment copies; two narrow images omitted the relevant date below the viewport.
Target-aware waiting-summary and dispatched-time captures repair those evidence gaps, pending inspected rerun images.
The `91636db342f6313150621514fb376dcdca487f0c` rerun passed desktop timer/playback and retained three dashboard/filter navigation failures; it was interrupted before repeating known failures. Exact `2f3e107db8d3765e1bb7914843d7e9189b71a8e8` completed 24 passed / 6 native-dashboard failures. Exact `845a69a3a2` was interrupted before journeys after review identified a required narrow Expand capture repair. Exact `23a64714ca3351d4ca809fe93f030e308382b9f5` completed 20 passed / 10 failed: native provider/current-label navigation and narrow Created viewport failures were retained. Independent inspection found the normal narrow inspector allocated only its header, rather than content; the product layout and date captures were repaired.

Exact clean `15558189646d9c8140fdf9fcade5fbf9fda64a52` completed 24 passed / 6 failed in 18.7 minutes. All timer, playback, comparison and timezone cases passed across desktop/360/390 light/dark. Independent inspection opened 42 PNGs, including all 30 targeted timezone captures: normal expanded inspector Created, Created/Started/Completed in Amsterdam and UTC, final UTC results and Reset previews were visible and correct. All six native-dashboard failures were the same missing Properties transition before the Name assertion; the assertions and later metadata-to-main transitions are repaired, pending a fresh exact-head rerun. Those earlier native narrow cases actually forced 320px; their retained images are additional partial 320px evidence, not 360/390 acceptance. The current journey now respects the requested viewport.

The independently reviewed date-era/range repair has 26 passing focused date tests and a focused typecheck. The actual-checkout whole Hosting suite/typecheck passed (1290 tests), whole Workflows passed (5353 tests), and whole Dashboard passed (225 tests). Earlier copied-checkout Hosting import-resolution failures remain unqualified preliminary evidence. These local unit/typecheck results do not complete composed acceptance.

Independent source audit also identifies composed gaps beyond these five journeys: workflow/activity management and
recovery/migration dates, Home/notification/module dates, the dedicated BPMN host, lifecycle/compensation/user-task dates
and enabled local-provider Weaver history. The provider-disabled composition cannot discover or create Weaver sessions;
that gate remains intact. Successful generic journeys do not close these surfaces.

| Journey | Required evidence | State |
|---|---|---|
| Settings | Browser default; UTC and Europe/Amsterdam selection; saved preference after reload; Reset restores browser; invalid stored zone falls back visibly | Exact155 Workbench passed all six; Foundation feed not run |
| All date surfaces | Shared formatter; explicit offsets in workflow/activity/instance/BPMN, module, notification and dashboard dates; valid missing/sentinel rendering | Incomplete: broader source-audit categories above remain not run |
| Run workflow | Amsterdam winter and summer wall times send +01:00 and +02:00; UTC sends +00:00; visible selected zone and offset; actual API request and retained/run result prove the instant | Exact155 Workbench passed all six; Foundation feed not run |
| Filters | Instance From/To use the same preference and preserve their selected instants | Exact155 passed all six with independent inspected date captures; Foundation feed not run |
| Invalid input | Impossible calendar dates and DST gaps remain editable with an associated error; no run request is sent | Exact155 passed all six with independent inspection; Foundation feed not run |
| Presentation | Light, dark, narrow and keyboard/focus/error/busy/read-only states inspected against the reference | Exact155 targeted dates inspected; native rerun and broader final-head journeys pending |

Retain exact-head screenshots, traces, request/response evidence and the journey result ledger as downloadable PR artifacts. The independent acceptance reviewer inspects the composed screenshots and complete affected journeys; unit/fixture results alone do not complete this record. Keep each failed/interrupted/not-run state until that exact journey is repaired and rerun. CI is deferred per coordinator dispatch; these local acceptance states are not waived.
