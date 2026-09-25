# CLI contract v1

Command: `dotnet elsa composition plan --catalog <path> --composition <path> [--workspace-profile <path> ...] [--inventory <path>] [--persistence-evidence <path>] [--format text|json]`.

The [command decision](../../../docs/reports/runtime-composition/developer-plan-command-contract.md) defines the input shapes and example evidence. The command needs no host flag, worker, database, or write permission. Text is default. JSON is one complete schema-v1 object on stdout; errors go to stderr with no partial JSON. The output is a redacted projection of the same shared plan used by the text view and future builder.

| Result | Exit | Output |
|---|---:|---|
| Valid plan, even with unresolved findings | 0 | Text or JSON plan on stdout; empty stderr. |
| Malformed, unsupported, duplicate, digest-invalid, or unsafe input/format | 2 | Stable safe refusal code on stderr; empty stdout. |
| Missing/unreadable input or failed file resolution | 3 | Stable safe resolution code on stderr; empty stdout. |
| Cancellation | Existing CLI refusal convention | No partial JSON. |

Output must expose exact candidate/accepted IDs, selection/removal sources, source-tagged required/optional dependency evidence, unresolved/advisory findings, observed package locks, supplied inventory identity/time, and unchecked resource-reference names. It must not expose arbitrary input rationale, opaque settings/resources, connection values, secret-bearing paths, parser excerpts, or a readiness claim. Equivalent semantic inputs and identical evidence identities/timestamps must produce byte-identical JSON.
