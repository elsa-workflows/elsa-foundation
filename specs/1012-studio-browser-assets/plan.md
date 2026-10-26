# Implementation Plan: Prebuilt Studio Browser Assets

**Branch**: `agent/codex/3361-studio-browser-assets` | **Date**: 2026-10-26 | **Spec**: [spec.md](spec.md)
**Input**: Program3340 task3361, accepted [hosting contract](../../docs/reports/studio-hosting-contract.md) and [asset-delivery decision](../../docs/reports/studio-browser-asset-delivery.md).

## Summary

Move the entire standard Client, StudioSdk and StudioUi source/build ownership from Web into the existing Hosting Razor library. Its current `/` static-web-asset base serves prebuilt `wwwroot/studio`; independently packaged module assets keep their current `_content` URLs. Keep public Add/Use/Map, browser imports and private npm identities stable. Update every active source/build/test/container entry point and the frozen workspace lock. Qualify actual production archives through an external host, not the spike's temporary wrappers.

## Technical Context

- .NET10 / SDK10.0.402, ASP.NET Core static web assets, existing Hosting/Api/Core and Dashboard production projects.
- TypeScript/React/Vite, pnpm11.9.0 locked workspace; browser SDK/UI facades share the Client source barrel and one import-map runtime.
- Producer builds frontend before .NET build/pack. Consumers use packaged static assets with no frontend toolchain. Add a producer-only pack guard for required shell/index/SDK/vendor outputs, without shipping essential server hooks in build/buildTransitive integration.
- Existing local static assets only; no new runtime state, server service/endpoint, public C# API, package or persistence model.
- Whole affected xUnit/Vitest/workspace suites; typecheck/lint/build; real Chromium application/module route; owned external archive/cache/publish/HTTP proof and negative controls. No performance measurements.
- Fixed `/studio` and normal root application routes; arbitrary PathBase remains unsupported. Publish the package payload through standard Razor SDK behavior. No custom file server or asset proxy.

## Constitution Check

Both constitution Gate indexes and the review checklist apply. Packaging cohesion (§2.13) is satisfied by the accepted Hosting owner; Core/Api take no browser/Hosting dependency. Existing public APIs, feature identities and module-owned static namespaces are preserved (§2.19/§2.21.1). No new public handler/model, friend grant, runtime singleton, event, scheduled task or feature is introduced. §E5 versioning remains computed/injected; use the same candidate version across package closure and retain existing compatibility checks rather than authoring constants. §E7 needs no new test project. Draft/provisional workflow rules do not affect this packaging change.

Pre-design and post-design check: pass; no new exception or clarification is needed. The original spike already chose ownership, URL and customization contracts. Source migration and actual production qualification remain unproved until implementation checks below pass.

## Project Structure

- `Studio/src/essentials/Elsa.Studio.Hosting/{Client,StudioSdk,StudioUi}`: sole standard frontend source ownership; generated `wwwroot/studio` remains ignored and packs through Razor SDK.
- `Studio/src/apps/Elsa.Studio.Web`: distribution host references Hosting; remove old frontend source ownership and obsolete generated shell payload, retaining Web's host-owned configuration/state paths.
- `Studio/{pnpm-workspace.yaml,pnpm-lock.yaml,package.json}`, active module/test imports, browser fixtures, examples, source guides, Docker/CI path inputs and asset verifiers move to the new paths.
- `tools/studio/HostingAssets.Probe/`: actual production package/published consumer qualification. Retain the historical AssetDelivery.Probe's explicit wrapper scope and update its source input location only where required to keep it reproducible.
- `specs/1012-studio-browser-assets/{spec,plan,research,data-model,quickstart,tasks}.md`, `contracts/browser-assets.md`, validation evidence.

## Implementation and qualification

1. Record a source-path inventory and move all three source trees atomically with active reference updates. Preserve public/private npm names, browser URLs, validators, module source ownership and frozen historical consolidation manifests/specs. Update current canonical guides instead of rewriting historical evidence.
2. Refresh only workspace importer/path metadata in the lock with the supported pnpm version; prove frozen install. Cover Hosting's shell CSS separately from module CSS where existing lint contracts distinguish them. Update layout/test source mirrors and generated-output/harness ignore boundaries.
3. Build actual frontend into Hosting. Add fail-fast producer packing checks for index, SDK and import-map vendors; verify payload completeness with the existing asset inventory and actual archive traversal. No consumer-side Node invocation.
4. Build/pack actual Core/Api/Hosting/Dashboard projects from an owned committed source snapshot with the real build configuration and copied, byte-accounted frontend output. Retain source revision and artifact hashes. Use an explicit candidate version consistently.
5. An external Web SDK host references only Hosting and Dashboard and uses public Add/Use/Map plus framework module selection and a small shell configuration. Use isolated package cache/source mapping; prove package-only libraries and archive/cache/publish byte identity. Deny node/npm/pnpm/npx on consumer restore, locked restore, publish and runtime PATH.
6. Hide owned producer, feed, consumer source and cache before running only published files. Fetch every packed shell/module resource; verify bytes/MIME/cache/ranges, root/deep links, runtime/discovery APIs and real unknown APIs/assets. Chromium boots the actual application and renders the selected module's route, recording console/network failures and actual host/SDK/module compatibility.
7. Repeat production customization profiles: separately named fallback with bundle, same-path SDK collision control, direct Hosting reference ExcludeAssets="build;buildTransitive" with custom document, working .NET discovery and retained module assets. Full backend/auth/custom-module/addon combinations remain later leaves.
8. Negative controls: omit required producer output (precise pack failure), remove one published vendor/SDK file (specific HTTP/browser failure), restore actual bytes and pass. Review every discovered failure and re-establish whole affected suites.
9. Run root locked restore if references change, architecture graph/whole project/maps, Studio package inventory/full pack and actual reference Web smoke. Independent correctness/standards/acceptance draft loop, exact-head CI/formal review and resulting-main audit precede Done. Current app push403 may prevent remote delivery; retain local checkpoints and report it without bypassing identity/permissions.

## Complexity Tracking

No new architecture exception. The qualification tool uses actual production project files/APIs and owned inputs, with small private orchestration helpers. The source checkout is never hidden or renamed while other programs use it; only its owned source snapshot is hidden. Browser automation is producer-side, outside the denied consumer environment. Optional Spec Kit auto-commit hooks are skipped in favor of deliberate reviewed checkpoints; context-update hooks are disabled.
