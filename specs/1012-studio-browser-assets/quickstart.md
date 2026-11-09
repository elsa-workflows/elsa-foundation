# Qualification guide

1. Frozen-install the Studio workspace, then run affected whole tests and typecheck/lint/build. Confirm generated standard output exists only under Hosting and module output stays with modules.
2. Run the producer asset verifier and whole Studio .NET project. Pack the actual solution and verify package identities/metadata; an intentionally missing required shell output must fail precisely before restoration succeeds.
3. Run `python3 tools/studio/HostingAssets.Probe/qualify.py` after committing source inputs. Inspect its printed owned artifact/report directory: actual package IDs, same candidate version, archive/cache/publish bytes, hidden owned inputs and zero denied frontend invocations.
4. Verify actual application root/deep link and module route in Chromium, plus complete resource/MIME/cache/range checks and real unknown API/asset404. Remove a required published vendor/SDK resource, observe specific failure, restore exact bytes and pass.
5. Qualify separately named fallback, SDK same-path collision and direct-reference custom-frontend opt-out against the same production packages. Final full backend/auth/optional combinations remain #3363/#3364.
6. Run required architecture/maps/lock gates, independent PR review, CI and resulting-main audit. Git push403 is currently a remote-delivery impediment, not successful delivery.
