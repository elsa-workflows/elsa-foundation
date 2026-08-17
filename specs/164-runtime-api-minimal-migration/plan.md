# Implementation Plan: Runtime API Minimal API migration

## Sequence

1. Freeze baseline-first evidence from the historical FastEndpoints source, including HTTP/OpenAPI provenance and meaningful handler, binding, status, and error cases.
2. Add owner-local permissions, source-generated JSON context, public non-sealed feature seams, and one explicit 24-route mapper.
3. Exercise Minimal API and retained FastEndpoints authorization parity and compatibility evidence, including route/body precedence and errors.
4. Add composition, collectibility, transition-ratchet, map, and affected Runtime E2E evidence.
5. Review the report and explicit differences, then run owner, Architecture, build, maps, formatting, and diff gates.

## Design constraints

- Foundation Identity is the sole permission evaluator; endpoint metadata names only the catalog action.
- A feature maps routes exactly once through standard `IEndpointRouteBuilder` endpoints.
- Source-generated JSON is owner-local and typed; no framework-global configuration is added.
- The historical baseline commits precede production migration and remain reproducible after rebasing onto the migration parent.

## Verification

See [contracts/runtime-route-manifest.md](contracts/runtime-route-manifest.md), [contracts/compatibility-evidence.md](contracts/compatibility-evidence.md), and [checklists/requirements.md](checklists/requirements.md).
