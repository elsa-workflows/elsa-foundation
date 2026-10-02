# PR2315 review round 4 — configured discovery transport

Reviewed head: `365a3ae433a0c8aa4759ffe23593e391b92c2027`. Actual Copilot review **5393698368** has one new actionable finding. All hosted workflows on that head are terminal green, including CI37026263376, Docker37026265197, CodeQuality37026255588, Maps37026262837 and filters37026262570. These are earlier-source results, not certification of the next patch.

## Comment ledger

- [4167252521](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4167252521) — **fix**. NormalizeBearerClaims=true plus RequireHttpsMetadata=false allowed a remote HTTP authority. Use one URI predicate for OIDC Authority and final bearer Authority/MetadataAddress: absolute HTTPS, or HTTP loopback with the flag explicitly false. Check before stock postconfiguration and after all final postconfigurers. Retain absent/default-discovery semantics, custom HTTPS endpoints, metadata pinning, and legacy normalization-disabled behavior. Reply and resolution follow tested publication; the linked public thread is authoritative for their actual status.

## Existing failed-gate reconciliation

Current365a full Linux x64 Build & test110901560540 passed Identity446, IAM EF191, Runtime1949, Runtime EF836, Workbench39 and Architecture635, each0failed/0skipped in those assemblies. The previous0229 one-read failure remains unexplained; the strengthened probe/count acceptance now has exact-head full Linux evidence. It is not called a flake or a causally repaired production failure. Log `/tmp/runtime-2308-pr365a-build-linux.log`. Dedicated Architecture and Core-only also finished successfully. Broad CI includes retained opt-in skips in other assemblies; no whole-CI zero-skip claim is made.

## Review of scope and limits

Root reviewed the bounded production/test patches. The URI restriction covers configured endpoints, not discovered JWKS URLs or redirects. IdentityModel8.19.2 uses one retriever for metadata and advertised keys, with its HTTPS check controlled by RequireHttps. The [bearer contract](contracts/bearer-normalization.md#discovery-transport-proof-boundary) therefore keeps HTTPS required for deployment and the exception limited to trusted local development/test issuer/key configuration. No new retriever/transport layer or change to the trusted host-code boundary is claimed.

Independent read-only source review found no configured-URI policy bypass or blocking defect. Root reviewed the noted registration-order limit: a stock JwtBearerPostConfigureOptions registered before Elsa may reject an HTTPS-required HTTP MetadataAddress with its existing constant HTTPS error before Elsa's ConfigurationInvalid classification. This remains a stable, value-free activation refusal before serving or mapping, as FR008/013 require; no new diagnostic from arbitrary earlier host/framework code is claimed. Normal Elsa registration order is covered by the executed exact-classification controls. Later postconfiguration is covered by an actual host startup control, not just direct validator invocation.

Initial focused preflight compiled7controls:5passed/2failed. The two Authority-negative cases surfaced an existing NormalizationTestHost failure-cleanup defect: failed startup had not initialized hosted services, so StopAsync threw ArgumentNullException from LINQ and masked OptionsValidationException. Correct failed-host disposal rather than weakening the rejection assertion. Log `/tmp/runtime-2308-review4-preflight.log`. Restored positive, mutation, real-child and freshness gates are still pending at this checkpoint.

After the failed-start disposal correction and later-postconfigure regression, the rebuilt focused preflight passed8, failed0, skipped0 on macOS arm64/net10.0. Log `/tmp/runtime-2308-review4-preflight-restored.log`. The temporary restriction-removal mutation and complete restored affected gates follow; this focused result is not publication or main qualification.

## Executed mutation and publication checkpoint

Removing only `&& uri.IsLoopback` from the shared URI predicate compiled and ran five existing-project controls: three failed, two passed, zero skipped. Remote HTTP Authority, early custom MetadataAddress and later-postconfigured MetadataAddress each activated unexpectedly and failed the expected OptionsValidationException assertion. The two HTTPS-required cases still refused. Log `/tmp/runtime-2308-review4-mutation.log`; harness/receipt `/tmp/runtime-2308-review4-mutation.py` and `/tmp/runtime-2308-review4-mutation-receipt.json`. Validator source was restored byte-for-byte: original/restored SHA256 `94bb51a3b416be18b79edad04a5d6594b9f0ab53f599f455697eba9bb82de5e8`.

The restored full Identity run was submitted through the shared build-slot wrapper and is live, waiting behind unrelated sessions at this checkpoint. Root may publish the reviewed coherent fix to start exact-head hosted gates in parallel with that local queue; it is not eligible for merge until the restored affected suites, real Worker actor, architecture/freshness checks and actual new-head review/CI pass. Completed local gates and resulting-main/source-package qualification will be recorded publicly on #2308/PR2315 with their exact source, rather than forcing a documentation-only push after every observation. T027/T028 remain unchecked.

No new test project, provider matrix, EF suite or cadence. T027/T028, resulting-main qualification and Worker profile publication remain open.
