# Qualification run guide

This is the acceptance plan, not evidence of completed runs. Use owned temporary hosts/stores/keys/accounts only.

1. Read the feature plan and current contribution validation/review rules. Check main health and build-slot/load conditions. Use this chat's worktree explicitly.
2. Run focused Identity tests: `dotnet test Essentials/tests/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj`. Run affected Identity EF tests/provider suites separately; never bypass build slots.
3. Compose a source-owned-only host, an opt-in database-managed host and an optional-module-absent host. Supply test Secrets references and durable keys/stores. Start two controlled real OIDC providers and local login. Record actual commands, ports and revisions in evidence; never record secrets.
4. Drive Studio configure → failing test → correction → successful test → actual interactive preview → enable. Check zero preview user/link/session/token writes and unchanged administrator cookie. Confirm chooser offers local and both providers.
5. Exercise reject/default, explicit JIT/default roles, configured matching, existing links, collisions/case variants and external-claim injection. Assert exact Elsa identities/permissions. Exercise explicit override/disable/archive/restore/remove and stable links after source changes.
6. Inspect/revoke links/sessions, test access/refresh/cookie denial and unrelated-user survival, final-path recovery and two-node concurrent last-path mutations. Repeat with restricted/read-only permissions and ownership modes.
7. Restart durable single-node host mid-flow; complete callback once. Start shared-store/key nodes, initiate on A and complete on B; race callback/result consumption and provider mutations. Record each mode actually passed; unsupported/unavailable is not passed.
8. Capture source-owned/read-only, policy editing, missing module, failure/recovery, light/dark, keyboard/narrow and retained-tab Studio surfaces. Use current SDK and CSS/type/build gates.
9. Verify extension catalogs/API baselines/generated maps, packed modules/assets and a separate public consumer. Review exact draft head, run current required checks, merge under org protocol, repeat affected actual acceptance on resulting main and reconcile #3579/#2771/spec/docs. Keep passed, failed, skipped, unavailable, cancelled and incomplete distinct.
