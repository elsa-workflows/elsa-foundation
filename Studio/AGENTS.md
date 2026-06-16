# Global Codex Instructions

## Code style

- **DRY by default.** After implementing something, proactively check for repetition and extract helpers, shared setup, or abstractions without waiting to be asked.
- **Clean tests.** Prefer instance fields and constructor setup over repeating arrange blocks. Extract `ActivateAsync()`-style helpers. Use `IAsyncDisposable` for test class teardown where appropriate.
- **No unnecessary boilerplate.** Inline trivially small helpers rather than wrapping them in extra methods.

<!-- SPECKIT START -->
For additional context about the current UI-system specification, implementation
plan, task backlog, and design contracts, read:

- `specs/001-ui-system/spec.md`
- `specs/001-ui-system/plan.md`
- `specs/001-ui-system/tasks.md`
- `specs/001-ui-system/contracts/studio-ui-components.md`
- `specs/001-ui-system/contracts/module-management-api.md`
<!-- SPECKIT END -->
