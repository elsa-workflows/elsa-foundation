# Direct BPMN qualification

Prepare the frozen project/lock templates as described in the [manual fixture entrypoint](../README.md) before building.

A bounded, one-off Runtime Efficiency #2531 profile of the available direct BPMN model in an operator-owned Workbench.
The operator selects its real built-in single-node hosting profile and a fresh SQLite runtime database, starts only
that owned process on a verified free loopback port, and retains source/binary/configuration records. This driver
never starts or stops a server. Its login is the fixed development demo account; never target an existing service.

The graph is explicitly two elements: Start Event -> End Event, with no business task or native activity. It publishes
through the authenticated model API, measures repeated authenticated starts, then checks every execution is
TerminalSettled/Completed with empty work. A repeated original start request must replay the exact execution without
changing its final view. Actual model-row count must match all cold/warm/measured starts; native workflow, activity,
bookmark and scheduler rows must remain zero. These assertions distinguish the direct model from a native wrapper.

Each process receives one JSON configuration with an owned loopback BaseUrl, fresh absolute Output, absolute
RuntimeDatabase and actual HostProcessId. Bounds are 1–60 measured seconds, 1–20 warmup seconds and 1–16 callers.
Successful completions and all errors stay in the population. First request, warmup, timed window and final
validation are separate. The timed rate includes the actual HTTP/authentication path and tail completion; publication,
final reads and replay checks are excluded. Latency and host CPU/peak are recorded. The allocation field measures
only the HTTP client, explicitly not the engine's allocation. Host allocation requires separate observation.

The shape, storage and entry point differ from the native activity probe, so its rate is not an apples-to-apples
engine comparison. This does not qualify clustered model placement, service tasks, callbacks, or multi-host BPMN.
Those have separate implementation/qualification owners. No timing CI gate, benchmark service or SLO is introduced.
The local one-second/two-caller smoke passes 88 total real model starts, exact replay and physical-row assertions;
shared-machine timing is excluded.
