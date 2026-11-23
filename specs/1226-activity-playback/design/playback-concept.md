# Activity playback interaction reference

The owner requested activity-by-activity playback after reviewing an interactive five-step approval example. The controls are Previous, Play/Pause, Next, a 0.5×/1×/2×/4× pace selector and a recorded-position scrubber. The selected activity and traversed supported path update together; a branch with no recorded visit stays distinct.

[Preserved interactive concept source](activity-playback.html) is copied unchanged from the owning chat. It is a conversation visualization fragment that requires its host's styles/icons, not a standalone Studio page. Its sample times, outcomes, branch coverage and compressed wait are invented illustration data. They do not establish existing runtime capture, causal evidence, production appearance or accessibility acceptance.

The concept's position scale is activity ordinal; displayed timestamps remain the original recorded times. A 52-minute approval wait is compressed by presentation cadence. The five illustrative occurrences are Receive request, Check policy, Manager review, Send confirmation and Complete request; Automatic approval is the unused example branch.

The specification governs behavior beyond this example: exact occurrence identity, partial/gap handling, parallelism, nested scope, bounded seeking, live bounds, permissions and retained Timeline focus. Preserve the difference between the playback cursor and the run's latest state. Do not infer that an unvisited-looking branch was not taken when recording coverage is incomplete.

Reference this concept during planning alongside the real run inspector and [Studio module UI guidance](../../../Studio/docs/module-ui.md). Future implementation must earn the repository's composed-host UX acceptance; this reference is not that evidence.
