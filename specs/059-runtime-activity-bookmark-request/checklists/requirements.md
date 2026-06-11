# Requirements Checklist: Runtime Activity Bookmark Request

- [x] Activity-facing request type is independent of Workflows.Runtime.Core.
- [x] Scope excludes final high-level wait API.
- [x] Scope excludes volatile wait.
- [x] Bookmark persistence remains delegated to `CreateBookmark` scheduler work.
- [x] Callback method names remain excluded.
