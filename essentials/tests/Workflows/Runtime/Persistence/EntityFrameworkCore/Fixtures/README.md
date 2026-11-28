# Archived classic Runtime EF host fixtures

Use the pinned source for the classic Runtime history, timer history, and Worker OIDC
process tests. Each test builds its matching executable hosts through project references.

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" 7e175dd40f20adfa7f9f645c6aa82a5bd2d0b032
(
  cd "$archive/source"
  dotnet test essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
    -c Release -p:RestoreLockedMode=true \
    --filter 'FullyQualifiedName~RuntimeHistoryProcessRestartTests|FullyQualifiedName~TimerHistoryProcessRestartTests|FullyQualifiedName~WorkerOidcHostTests'
)
```

`TimerHistoryRestartHost` publishes a Delay and reopens an owned SQLite store across seed, fire and confirm processes. Durable, Balanced and Ephemeral each require separate process identities and identical binary/version receipts, original timer identity and time, the expected schema, bookmark consumption, activity completion and duplicate-free repeat pumps.

`RuntimeHistoryRestartHost` publishes a real bookmark-wait workflow, resumes it after restart, then replays and purges its retained prefix and confirms the result in a fourth process. Durable and Balanced runs retain exact event/occurrence identities and invalidate old windows after retention.

`WorkerOidcHost` uses a local issuer and the classic HTTP Runtime host to verify persisted authentication and authorization evidence across child-process restart. The candidate lifecycle test requires a Unix PTY.

The archived tests create and remove owned temporary directories, databases and loopback listeners. Current Runtime EF tests retain the physical schema, provider, scope and history witnesses.
