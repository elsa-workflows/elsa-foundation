# Validate reusable authentication and bridge

Use the pinned SDK from the repository root.

1. Run `dotnet test Studio/tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj` as a whole project and compare
   discovery/execution counts, including existing theory expansion.
2. Verify public HTTP cases for disabled/enabled authentication,401/403, manage/read implication, missing
   handlers, trusted/sibling query paths, transient backend failures, cache expiry/revocation and bounded cap.
3. Inspect recording-backend headers/counts and browser JSON/runtime bodies. Incomplete privileged config
   makes zero privileged calls; session introspection never carries the server management key.
4. Through public shell scopes and IAuthenticationService, verify the same root cache/factory/clock across
   reload. Drain does not dispose root ownership; root shutdown disposes its owned clock once.
5. Establish meaningful causal controls for permission checks, query-path isolation, key exclusion/fail-closed
   behavior and borrowed lifetime, then restore exact source and run the whole suite.
6. Run the packed Api dependency consumer, isolated actual Web smoke and repository architecture/maps/inventory/
   locked-restore gates. Final browser/optional/full package-only matrices remain downstream.
7. Review the exact head through three independent lenses, complete required CI, then verify actual resulting main.
