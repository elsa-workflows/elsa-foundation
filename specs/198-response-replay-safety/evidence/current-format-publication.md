# Current-format External publication

## Scope

The owner confirmed Foundation is unreleased and unused; backwards compatibility for its
exports is not required. After the accepted #2517 correction, a rebuilt normal Workbench
authored, published and executed a fresh `HttpEndpoint → SetVariable → WriteHttpResponse`
workflow, then returned its exact executable-export bytes. No manually constructed executable
or old-artifact import supplied this capture. The response declaration remains External.

Capture source is `11c852be19a24fd173edb77ab2cdbfc0aa977c42`, the reviewed merge of
accepted main `9c9f475929018d96a16fb3a24549a304a7612254` into the retained response branch.
Its complete production tree equals that main source. Tests and documentation also retain
the unmerged response work; no whole-tree equivalence to the old baseline is claimed.

## Executed capture

The clean capture checkout built Workbench successfully. Its preceding focused merge checks
passed 21 compiler/golden/refusal cases and three EF profile/non-overwrite cases, without skips.
On 8 October 2026 at 18:24 UTC the normal published POST returned HTTP 200 with `Alice Smith`.
The deterministic computation joins the submitted `firstName` and `lastName`. The four
executable nodes are Sequence, HttpEndpoint, SetVariable and WriteHttpResponse.

| Pin | Value |
|---|---|
| Definition | `14C9KTKZfxp` |
| Definition version | `14C9KTQmMAR` |
| Publication source reference | `activation-ref:publication-14C9KVSBH9L` |
| Artifact ID | `artifact-48fcccd01c49` |
| Artifact hash | `sha256:48fcccd01c4967d6ea576fdf6dc5a8d9383d6f6d858f9886c910cd3167ea5198` |
| Response profile | External, resolved from its omitted JSON default |
| Exact export | 29,688 bytes; SHA-256 `cce757438f683fee93204df0dc3cda84531e09106787f99247868c837620b3f3` |
| Manifest SHA-256 | `1c73bfc4d5f6d8f573446fdeaaea51d6c9d8ce09aca4b76223ea2bb9fc70c69c` |
| Workbench DLL SHA-256 | `56aaccc8b496bc3b7757ab3f959417c5bc2c5aa7beaf5fd9c5e7a9c359d3fbc6` |
| Loaded Runtime DLL SHA-256 | `40e5ffe57a55e204880b948acc0e2ceed307fb3d0a7f4224c90dc6f7700930ce` |

The [portable manifest](../../../tests/essentials/Activities/Http/IntegrationTests/Fixtures/ResponseReplayHost/Fixtures/current-format-external-manifest.json)
records the dynamic route, inputs, computation and source/config/assembly pins. Both binaries
identify capture commit `11c852be1` in their product version. The capture and separate verifier
exited zero. Root independently checked their actual logs, hashes and retained SQLite state:
execution `14C9Ka2cpxu` is Completed, pins the expected artifact, and has no incident row.
Stored artifact readback confirms External and the same hash.

The fresh content root holds all relative SQLite stores. Source and copied configuration
were checked, inherited redirect names were absent, and no existing developer database was
used. Owned process 80787 stopped; root verified it and the port listener were gone. The
content root, database, raw logs and private run receipt remain retained in the program
journal under `2400-current-format-2515/`.

## Fixture handoff and limits

The new exact closure and manifest are added under distinct `current-format-external-*`
filenames. The original `pre-candidate-external-*` files retain their original bytes and
source claims as historical evidence. Current-runtime tests use the new files; they do not
require old-export import compatibility. No database migration or rewriting is involved.

The unchanged assertions in `CapturedExternalClosure_ImportsAndExecutesThroughChildHttpHost`
passed 1/1 without skips at clean `b42a3995da836a4773832b69df275f179bb42979`. It imported
the exact current-format closure through production reconciliation and executed the child
HTTP host, checking the persisted artifact/profile, Completed outcome, HTTP status/body
and committed response body. Root inspected the actual TRX (SHA-256
`4fd5d224d6cb8e8d8778ff42a0abc1b16e4a93283c31422e3802cebf5f102fc5`) and unchanged source
receipt; independent review found no material fixture handoff issue. This completes #2515's
current-format integration handoff.

At capture and handoff the declaration was External. This establishes a current-format
baseline, not ReplaySafe classification acceptance, matched query counts, latency, crash
recovery on the final candidate or completion of #2400. The subsequent conditional
declaration and its proof remain owned by the current #2400 task checkpoint.
