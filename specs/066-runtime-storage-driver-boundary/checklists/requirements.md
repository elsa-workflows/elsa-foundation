# Requirements Checklist: Runtime Storage Driver Boundary

- [x] Removes legacy storage-driver contracts from Runtime.Core.
- [x] Keeps durable value state/store as the runtime durability boundary.
- [x] Removes dead storage-driver implementation project from the solution.
- [x] Adds regression tests for contract and DI absence.
- [x] Keeps Elsa 3 live instance resume out of scope.
