# Validate base hosting

From the repository root, use the pinned SDK and ordinary build-slot wrapper where installed.

1. Restore/build the new Hosting project and run the entire Studio test project:
   `dotnet test Studio/tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj`.
2. Compare executed tests with discovery, accounting for theory rows. Public host tests must cover an external
   feature assembly, selected non-default shell, cold/warm policy enforcement, async manifest/reload retention,
   invalid options/mandatory API validation, configuration precedence, cancellation and mutation coordination.
3. Confirm real HTTP file/runtime assertions: bytes/MIME, no-cache/conditional304, distinct custom fallback,
   secret-free runtime allowlist and unknown API404. Nuplane-specific package assets remain separately qualified.
4. Establish causal controls: break pre-routing activation, selected fallback or scope retention; each affected
   behavior test must fail, then pass after byte-exact restoration. No unconditional full frontend rebuild is
   needed until browser source ownership changes.
5. Run locked solution restore, architecture graph restore/guards and generated-map checks as listed in the
   [repository gates](../../docs/contributing/merging.md#pr-evidence). Commit changed solution filters only.
6. Review the exact candidate through independent lenses and CI; verify the actual resulting main after merge.

This leaf uses public server composition and test-owned browser files. Final packed production/browser default,
custom frontend and optional capability journeys remain #3363/#3364; do not claim those from TestServer results.
