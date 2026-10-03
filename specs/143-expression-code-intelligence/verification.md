# Expression Code Intelligence Foundation — Verification

Last reconciled: 2026-10-03 for Program #2310 partial implementation evidence; normal-host acceptance pending

## Program #2310 status

The passing evidence below is historical baseline evidence. It does not prove the current normal host, effective runtime Liquid metadata, runtime-compatible JavaScript help, or the real persisted producer-consumer lifecycle requested by Program #2310. T026-T031 and the corresponding live Studio journey remain open.

## Passing evidence

### Program #2310 implementation checkpoint, 2026-10-03

On `claude/2351-expression-normal-host`, with draft PR #2373 stacked on the Program #2310 planning branch:

- The filtered real-Workbench run built successfully and executed two cases: the missing-Liquid-provider case passed; the persisted-draft JavaScript/Liquid case timed out at the existing ten-minute readiness limit before any test-body assertions.
- An isolated diagnostic rerun of the persisted-draft case also timed out before its test body. A direct readiness request returned HTTP 503 with `status: starting` and `code: shell_activation_pending`; shared-machine load remained above 700. This proves an unfinished warmup, not a passing expression journey or a diagnosed expression regression.
- The fixture resolves the WriteLine input reference from the live activity contract as well as the ReadLine output metadata. That version rebuilt successfully. Subsequent review refinements assert the exact EF design feature and `elsa.db`, plus consistent nonempty document/context revisions across all four tooling responses; those latest assertions still require rebuilt execution.
- The scoped `DomainApiCapabilityRegistrationTests` Architecture suite passed all 11 tests, including independent JavaScript/Liquid composition and zero-provider capability omission.
- Current-head manual CI exposed stale Architecture and two inherited Modularity test-project locks. The Architecture lock was restored locally; the two Modularity locks were reconciled to the already-restored Workbench dependency entries while locked-restore confirmation remained queued. This is not a locked-restore pass.
- The manual Maps run identified five stale generated files. Deliberate regeneration changed six map files, including the manifest; the generated findings reports were reviewed and unchanged. The final local freshness check passed: `Generated maps still describe the tree.` `git diff --check` passed.

T026-T031 remain open. The coordinated Studio browser suite collects its four normal-host scenarios but has not yet produced live passing evidence.

### Historical baseline

| Suite | Result |
|---|---:|
| `Elsa.Expressions.Tests` | 108 passed |
| `Elsa.Workflows.Design.Tests` | 344 passed |
| `Elsa.Workflows.Design.Api.Tests` | 89 passed |
| `Elsa.Workflows.Publishing.Api.Tests` | 459 passed |
| Expression-tooling and custom-host architecture filters | 4 passed |
| `dotnet build Elsa.Server.slnx --no-restore --verbosity minimal` | Passed with 0 errors |

The focused evidence covers exact per-expression-type provider routing, JavaScript runtime globals, Liquid symbols, dotted/nested value shapes, authoritative persisted context, policy filtering before paging, host-replaceable authorization and revision fingerprints, descriptor/capability composition, semantic validation state mapping (including validator faults and caller cancellation), publication fail-closed behavior, and Test Run acknowledgement/metadata.

## Full architecture suite

The complete `Elsa.Architecture.Tests` run passes all 320 tests.

The three custom-host composition failures initially exposed by this run were corrected by making the persisted authoring-context source safely optional when a host omits draft persistence. All affected composition tests now pass.

## Contract audit

- Context and symbols are server-authoritative, permission/host-policy filtered, metadata-only, bounded, and `no-store`; hosts can replace `IExpressionAuthoringAuthorizationPolicy`, and its opaque revision participates in stale-result protection.
- Catalog paging occurs after filtering.
- Value-shape members are inlined to depth four; lazy retrieval is explicitly unsupported by v1 descriptors.
- JavaScript, Liquid, and future providers own their own globals/functions/variables; there is no generic “Elsa globals” catalog.
- Known validation errors gate Test Run/publication; unavailable Test Run validation supports explicit acknowledgement only when the result contains no error diagnostics; publication remains fail-closed.
