# T004 submission-rule evidence

Date: 6 November 2026. This is internal rule validation, not durable acceptance, delivery, provider, API or UX qualification. No producer/DI/shared ABI is enabled.

The initial 40 focused cases passed. Independent review then found two missing constraints: complete-envelope size and a configurable deployment lifetime ceiling. Twelve regression cases compiled and failed against the unfixed behavior; the existing 40 still passed. Final enforcement passes all 52 cases, and the complete affected Runtime test project passes 3,403 with zero failures/skips.

| Run | Passed | Failed | Skipped | Evidence |
|---|---:|---:|---:|---|
| Causal regression control | 40 | 12 expected | 0 | [red TRX](regression-red.trx.gz) |
| Corrected focused rules | 52 | 0 | 0 | [green TRX](regression-green.trx.gz) |
| Complete Runtime project | 3,403 | 0 | 0 | Local `/tmp/elsa-3583-t004-runtime-whole-20261106/runtime-whole.trx`; counts in [manifest](manifest.json), current-head CI remains separate |

The tests distinguish complete normalized-envelope bytes from raw payload/HTTP-body caps, account for escaping/optional fields, enforce default ≤ deployment maximum and reject oversized overrides without clamping. Other cases cover canonical nested JSON/equality, duplicate members, exact UTF-8 identities, detached payloads, independent fixed deadlines and immediate-only/no-ID behavior. The helper receives trusted acceptance time; it does not establish a physical provider clock or retry store.

Commands used the existing Runtime project, one build worker and disabled shared compilation. The red/green runs used `--filter FullyQualifiedName~MessageSubmissionRulesTests`; the complete Runtime run used `--no-build --no-restore`. All used TRX output. Source/test byte hashes and result hashes are in the manifest. The red source/test snapshot was uncommitted; its frozen hashes are recorded separately and are not presented as a Git commit.

Independent re-review found no remaining concrete rule defect after the fixes. Existing compiler/analyzer warnings were visible in builds; no warning cleanup was included. Shared source mapping, store/actor integration, real hosts/providers/process death, package consumers, API/activity/Studio journeys and resulting-main delivery remain outstanding in [tasks](../../tasks.md).
