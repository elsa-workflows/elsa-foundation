# Implementation Plan: Isolated Studio Package Consumer Proof

**Branch**: `agent/codex/3363-studio-package-consumer-proof` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

## Summary

Complete #3363 by reusing the production Hosting browser and Api dependency probes. Add an opt-in required-dependency omission control to the Api probe, retaining original archives and changing only the private copy's Api-to-Core nuspec edge. Preserve the normal proof's failure exit and report whether the specific expected failure occurred. Prove normal/fault/restored outcomes for both dependencies and published resources. Keep custom-module/addon integration, CI wiring and the final developer recipe in their later leaves.

## Technical Context

- Existing Python3 standard-library tooling: argparse, pathlib, zipfile, ElementTree, subprocess and the current report/host lifecycle helpers. No new dependency or framework.
- .NET10/SDK10.0.402 and actual Core/Api/Hosting/Dashboard candidates; public package composition only. The producer/browser driver may use the pinned frontend toolchain, while the consumer denies it.
- Storage is owned temporary archive/feed/cache/source/publish/report directories. Shared checkout and supplied candidates remain unchanged.
- Verification uses actual package restore, locked restore, compile/publish, published HTTP and Chromium, plus metadata-only causal controls. No new test project or product C# implementation.
- Local macOS and existing Linux CI are the qualification profiles. No new performance target or arbitrary filesystem/platform guarantee.

## Constitution Check

Framework6.1.0 and Elsa6.3.0 Gate indexes and the review checklist apply. Pre-design and post-design: pass. No domain/package/public API/feature/event/task/persistence/versioning change is introduced. Existing production implementations supply behavior; generated consumers are test callers, not replacement libraries. Section2.21 risk-based controls establish red then restored green; section2.23 feature registration/per-implementation tests are unchanged because no production feature or C# implementation is added. SectionE5 versions are read from actual candidates or the existing local producer argument. SectionE7 has no new test project to classify. Draft/provisional rules do not affect this verification change.

## Project Structure

- `tools/studio/ApiPackageClosure.Probe/qualify.py`: preserve the existing positive API-only consumer; add locked restore, private unsigned-archive dependency omission and precise result evidence.
- Its `README.md`: commands and expected outcomes, with original/copy and intended/unrelated failures distinguished.
- `tools/studio/HostingAssets.Probe/`: reuse production UI/customization and missing-vendor proof; add explicit dynamic runtime-script HTTP/browser/sentinel checks and missing/empty runtime controls from round1.
- `specs/1013-studio-package-consumer-proof/`: specification, this plan, research, report model, quickstart, tasks and validation evidence. This is internal qualification tooling; its canonical command contract stays with the tool README rather than a second public API contract.

## Design and qualification

1. Keep actual Core/Api identity/version selection strict. Positive consumers have one Api PackageReference and use Core's public metadata, so another Studio module cannot mask the missing Api dependency.
2. Add `--omit-api-core-dependency`. Before changing the selected private feed, retain both original packages in an owned original-packages directory. Refuse signed inputs and an already absent edge. Remove only dependency elements naming Core from the Api nuspec, preserving every other archive entry's bytes and the Api DLL hash. Record original/mutant hashes and removed requirements; never write to the supplied feed.
3. Enable lock-file generation and repeat restore in locked mode in the Api consumer. Add private node/npm/pnpm/npx denial shims and retain a zero-invocation audit in normal and control profiles. Keep its package/HTTP cache isolated and public/runtime type checks, DLL hashes, hidden inputs and owned-server cleanup.
4. A valid dependency negative still exits1 with product status failed. Mark expected failure observed only when publish fails with the missing-Core compile diagnostic and the restored graph has no Core library. A network/restore/SDK/unrelated failure or masked dependency is not a valid control. Preserve full logs and bounded diagnostics.
5. Run actual normal/omitted/restored Api profiles and verify source candidates and payloads stayed unchanged. Exercise already-missing/invalid-input rejection where it proves the control cannot report an unrelated failure as success.
6. Run Hosting normal/missing published React/restored profiles against the actual four production candidates. Require the precise missing-resource404 error; verify Node denial and published UI/module/resource behavior again after restoration. Separately assert runtime-script status/MIME/global shape and a nondefault HostId in HTTP and browser root/deep routes. Prove missing404, empty200 and browser-only empty-body failures before restoring the normal profiles.
7. Run Python syntax, complete affected generated consumers, required architecture graph/whole architecture project/maps, and review the complete diff. Product C#/frontend source, project references and versions stay unchanged, so unrelated Studio/frontend suites need no local repeat; exact-head CI still applies.
8. Open one draft PR, independently review correctness/standards/acceptance to convergence, set terminal spec status in that implementation PR, request formal review and require current-head checks. Verify reviewed merge/resulting main and actual artifact proof before Done.

## Workflow and complexity

No architecture exception or unresolved technical choice. [Research](research.md) consolidates inspected existing tools; no new technology research is needed. One implementation leaf/worker; independent read-only delivery review only. The required Spec Kit branch hook ran with the Issue Bus Lite branch name. Optional auto-commit hooks are skipped for deliberate checkpoints; agent-context hooks are disabled. Existing ignore rules cover generated files. No remote feed mutation, publication, deployment or publisher activation.
