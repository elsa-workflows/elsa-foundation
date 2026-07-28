# Quickstart: Validate Expression Code Intelligence Foundation

## Prerequisites

- .NET 10 SDK and restored solution dependencies.
- A host composition containing Workflows Design, Expressions API, and JavaScript/Liquid expression features.
- An authorized workflow-design user; use a separate user/session for denial tests.

## Focused validation sequence

1. Run the new Expressions Core/provider conformance tests. They must prove duplicate provider rejection, contract-version negotiation, all outcome states, cancellation propagation, and no evaluator/runtime context is reachable.
2. Run JavaScript and Liquid provider tests. Each must return its own language-specific completions/diagnostics using the same safe context; neither may evaluate source.
3. Run Workflows Design context-service and API endpoint tests. Verify lexical visibility, expected type, post-policy paging, bounded inline value shapes, permissions, host-policy denial and replacement, policy-only revision changes, stale revisions, and `no-store` responses.
4. Run draft validation tests. Verify ad-hoc validation is non-persistent; a malformed expression remains editable/readable and appears through the shielded validation read.
5. Run Publishing test-run/publication tests. Verify known expression errors reject both paths; unavailable validation requires an explicit test-run acknowledgement and makes publication/promotion fail closed.
6. Run architecture and existing expression descriptor/API tests to prove clients that do not discover `expressions.tooling.v1` retain ordinary descriptor/editing behavior.

## Expected end-to-end scenarios

| Scenario | Expected result |
|---|---|
| Authorized JavaScript context | Metadata-only visible symbols, expected type, revisions, and JavaScript provider result. |
| Authorized Liquid context | Same Design facts, projected into Liquid variables/filters/tags without JavaScript assumptions. |
| Unauthorized symbol | Omitted before provider invocation; absent from response and provider test fixture. |
| Empty supported catalog | `supported-empty`, not `unavailable`. |
| Stale revision | `stale` with opaque evaluated/current revisions and no stale completion payload. |
| Provider disabled | Capability absent from host or endpoint reports `unavailable`; basic expression editing still works. |
| Invalid full draft | Save/read diagnostics work; test run and publication reject. |
| Validation outage | Test run requires explicit acknowledgement and no error diagnostics; publication/promotion reject. |

## Commands

Run from the repository root:

```bash
dotnet test tests/Elsa/Expressions/Tests/Elsa.Expressions.Tests.csproj --no-restore
dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj --no-restore
dotnet test tests/Elsa/Workflows/Design/Api/Tests/Elsa.Workflows.Design.Api.Tests.csproj --no-restore
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --no-restore
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
dotnet build Elsa.Server.slnx --no-restore --verbosity minimal
```

Do not treat a build alone as proof: the tests above must execute the API authorization and consequential-operation paths.
