# Jobs publication source tests

This project exercises the registered `IExecutableCompilationSource` through Publishing's real event/enricher pipeline, with no Jobs authority or handler installation. It checks fixed descriptor bytes and digest, bounded literal selections, secret/withheld refusals, nested contributions, immutable input bindings and publication ownership conflicts.

These are source composition tests. They do not publish a persisted authored workflow, execute a native activity, consume a Jobs result or qualify a package/engine. The initial source assignment has not been built or run locally; restore/lock generation and execution belong to the assigned qualification lane.
