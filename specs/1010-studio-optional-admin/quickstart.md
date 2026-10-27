# Qualification guide

Prerequisites: .NET10 and the repository's current package feeds/locks; frontend verification additionally uses
the pinned Node/pnpm workspace. This leaf does not publish packages or enroll a real user store automatically.

1. Register AddElsaStudio, AddElsaStudioNuplane and MapElsaStudio/MapElsaStudioNuplane in an owned temporary host.
   Select a non-default shell/file/feed and a ContentRoot different from CWD. Verify discovery/registry and401/403.
2. Read feature state, apply a valid full edit and observe deterministic config reload. Race two old-revision saves;
   expect one winner/conflict, complete JSON and unchanged unrelated shell. Exercise secret masks and ownership refusals.
3. In an ordinary store, preview prune and retain files while diagnostics report unknown/incomplete protection.
   In a separate owned native store, explicitly enroll through the existing public protocol before installation.
4. Install/update a real fixture and retain an old shell scope. Preview without physical work, explicitly confirm,
   physically delete an obsolete content-only install and retain active/recovery/live-use packages. Reject stale bindings.
5. Serve actual package assets with range/MIME/revalidation. Pause between size preflight/read and attempt native
   reconcile/confirmed prune; require busy refusal and unchanged bytes. Test sparse oversized files, links/traversal,
   unsupported shapes, cancellation and saturated/slow responses.
6. Run whole affected .NET projects and frontend checks/browser tests; reconcile discovered/executed counts. Run
   causal controls, locks, architecture/maps, actual package inspection and reference Web smoke.
7. Complete draft review, exact-head CI/external review and resulting-main verification before marking #3359 Done.

The final multi-package isolated NuGet/browser/custom-module matrix remains on #3363–#3365.
