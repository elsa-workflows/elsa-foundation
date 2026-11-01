# Elsa.Agent.Specialists.Editorial

The finite M4 editorial example. Copywriter and Proofreader are ordinary published specialists. Native
Sequence/Flowchart or BPMN control flow runs them through `EditorialRevisionGate`:
- at most three rounds, including the first;
- the shared deadline is checked before each call;
- a third rejection takes `Exhausted`;
- an invalid verdict faults.

An approved article reaches `ReviewEditorialContent`. Its exact native wait is decided through
`IEditorialReviews` (HTTP: `Elsa.Agent.Specialists.Editorial.Api`). Deny takes `Rejected`, and the
example performs no publication effect. See [extension points](EXTENSION_POINTS.md).

Enable `SpecialistsEditorial` together with a current-subject identity provider, such as Foundation
ASP.NET Core Identity, and a distributed lock provider (FileSystem locking is qualified for a single node only). `DecisionLifetime` (default 15 minutes) bounds how long an issued decision may wait
for admission.

The feature runs inside a native workflow runtime host (it consumes the bookmark dispatcher, scheduler queue and
runtime stores) and registers one startup task, `EditorialTypesStartupTask`, which registers the two activity type
aliases.
