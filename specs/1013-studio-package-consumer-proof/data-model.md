# Qualification Report Model

Existing normal report fields retain their meanings: owned root, status, stage, candidate version/packages and archive/DLL hashes, package library types, runtime payload and hidden inputs. A normal run is passed only after all publish/runtime checks complete.

An omission control records its kind, required omitted dependency, original archive directory/hash, mutated archive hash, removed requirements, unchanged non-nuspec payload evidence, expected stage and whether the specific expected failure was observed. The consumed candidate hashes describe the private mutated archive; original hashes remain separately available.

Stages progress through candidate selection, optional mutation, restore, locked restore, publish and runtime. A failed run retains its actual failed stage, error, full log and bounded diagnostic tail. An expected failed product run remains status failed/exit1; its control observation is separate. An unrelated failure has no successful control observation. Each restored positive is a new run with an empty cache and original inputs.

These are internal tool report records, not new product/domain types or persistence contracts.
