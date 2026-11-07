# Jobs Studio source preparation

Status: source preparation only. No build, component execution, composed host, browser journey or
independent visual acceptance is claimed. The full [UX acceptance record](../../../../specs/1218-standalone-jobs/ux-acceptance.md)
and [acceptance matrix](../../../../specs/1218-standalone-jobs/acceptance-matrix.md) remain authoritative.

The operator starts in Jobs, finds a delayed, held or failed report, inspects its attempts and saved
result, releases one addressed hold or requests permitted recovery, and follows the linked run or
parent. Collections follow Connections and Workflow Definitions; shared controls and behavior follow
[module UI guidance](../../../../Studio/docs/module-ui.md). Current access comes from Jobs API, which
remains the authority for tenant, resource and action decisions.

The finite source surface is Jobs/Queues/Schedules/Holds/Capacity collections, job details with paged
attempts/physical receipts and gate holds, explicit protected input/result reads, four pause scopes,
revisioned release/capacity, cancellation, linked Retry as new run, independent submission and a
separate recurring schedule editor. Catch-up defaults to one run. No schedule deletion or occurrence
count is invented: the current API supplies neither operation nor occurrence-history projection.

Observable states are empty/populated/no matches, paging, initial loading, unavailable/denied/missing,
stale revision conflict, unconfirmed operation with exact replay, delay/backoff, Starting/Running,
physical exit unobserved, overlapping holds, saved result with owed/settled/unknown continuation,
unavailable payload, validation, and long content. Source uses semantic controls, focused headings,
host dialogs, workspace resource identities, shared theme tokens and bounded scrolling regions.

The current API advertises no handler catalog: submit/schedule editors require the explicit registered
handler/version and schema aliases. Parent links use current native and BPMN Studio routes only for
recognized model kinds. Unknown/missing parents remain readable identities without fabricated links.

Later composed proof must execute every journey in the canonical record with real tenants/providers,
workers, native and BPMN models, then inspect keyboard/focus, light/dark and narrow states. Component
controls are supporting evidence only. Root integration must add this new Client location to the
Studio workspace, build selection and host composition; existing globs target `Extensions/*/studio/*`.
