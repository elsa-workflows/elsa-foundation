# Global Codex Instructions

## Code style

- **DRY by default.** After implementing something, proactively check for repetition and extract helpers, shared setup, or abstractions without waiting to be asked.
- **Clean tests.** Prefer instance fields and constructor setup over repeating arrange blocks. Extract `ActivateAsync()`-style helpers. Use `IAsyncDisposable` for test class teardown where appropriate.
- **No unnecessary boilerplate.** Inline trivially small helpers rather than wrapping them in extra methods.

<!-- SPECKIT START -->
For additional context about the current Extension Builder Studio UI specification,
implementation plan, task backlog, and design contracts, read:

- `specs/003-extension-builder-studio-ui/spec.md`
- `specs/003-extension-builder-studio-ui/plan.md`
- `specs/003-extension-builder-studio-ui/tasks.md`
- `specs/003-extension-builder-studio-ui/contracts/extension-builder-ui-api.md`
<!-- SPECKIT END -->
