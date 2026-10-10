# Affected project verification

Root verified these full local project results on 8 October 2026 against candidate production source at `14f0f370b34ddf9f7fe868fe4c91f4827573bf75`. The production annotation remains conditional. These results complete T015 and part of T014; they do not establish matched query counts, causal mutation, final HTTP integration, Workbench E2E, architecture/maps or delivery acceptance.

| Project | Passed / total | Failed / skipped | TRX SHA-256 |
|---|---:|---:|---|
| Publishing API | 720 / 720 | 0 / 0 | `54bb92968800d35be25501634664c684fefb98f90034cd0c62a0dc46a4703563` |
| HTTP activities, after the contract assertion below | 204 / 204 | 0 / 0 | `b79f26f77469d4ab1d1ec661f65da048575bdf3303ce97703651e2789ae53089` |
| Runtime | 2,125 / 2,125 | 0 / 0 | `99703feee078e36a3a6a39a902e071d2d2ef99b3438d77d4734e3567a69f524a` |
| Runtime resumption | 21 / 21 | 0 / 0 | `decd27bb06b8b2ac5cd91c7ad8e048255a51e2c90ffd6bb71f64424109459371` |
| Runtime EF persistence | 904 / 904 | 0 / 0 | `cee323e5351f0c10dec2d0e1eb8e6e2ce6a79c2b1dc2dbfce8bc77c8ae89648b` |

Each executed test process returned exit code zero. Root parsed the actual TRX counters and checked the per-process receipts. Commands used `dotnet test` on the exact project paths in [the validation guide](../quickstart.md), through the normal build-slot wrapper, sequentially. Publisher and HTTP activity projects used existing restored assets. Runtime, resumption and runtime EF used `-p:RestoreLockedMode=true` to restore their committed dependencies before building and testing.

The first Runtime launch used `--no-restore` and stopped with `NETSDK1004` because that test project's local assets file was absent. No test executed in that launch. Its log and nonzero process receipt remain retained; the subsequent restore and passing suite are separate evidence, not a runtime repair.

The first HTTP activity run also passed 204/204, but compilation reported CS8604 in the new `ActivateAndProjectAsync` fixture. The fixture now asserts that the compiled activity contract is present and passes the same checked local value to activation and projection. The table records the full affected-project rerun after that test-only correction; source SHA-256 is `cf7d67cc8ae12d8f4d827b0e12b1878160ce123f4b150f66be616800e67792a5`. The earlier passing TRX remains retained at `300bce625a2956224fb5dc4bfab56e204c9cfa1c272101be41fa7b999980c01d`. Unrelated existing obsolete-API and analyzer warnings were not changed.

Source conservation was checked independently: 3,974 source/configuration files for the first Publisher/HTTP runs and 4,160 for Runtime/resumption/EF were unchanged during their respective runs. Their retained source-manifest hashes are `c7e446fe6de0bfe2a55b985078301611bfd1ecc7cf48b2e0fb2258f0441def3c` and `a1b81f8591fe17666dac36297d926e4e7f7505015801f569e6e6e6a8596d68d8`. The small HTTP fixture correction was separately pinned and unchanged during its rerun. Concurrent T010/T011 edits were confined to the other HTTP integration project and are not covered by these project results. Final source reconciliation must retain that distinction.

Raw logs, TRX files, source manifests and root review receipts are retained in the program's private `2400-full-affected-gates-20261008` evidence directory. No elapsed-time or query-reduction claim is derived from these correctness suites.

## Restored integration and rebuilt Workbench

After the bounded causal mutation restored exact candidate source, the complete HTTP integration project passed 39/39 with zero failures/skips and exit 0. TRX SHA-256: `fbc340c51ac0da816f7508d2b28cc21a971ec6860f67d11ff5f33982916a18d1`. This completes T014 for that source; [the mutation report](causal-mutation.md) records source and binary conservation and the retained crash/count results.

T016 built `src/apps/Elsa.Workbench/Elsa.Workbench.csproj` with locked restore (exit 0, 64 warnings, 0 errors) and ran the unchanged `e2e-tests/http/Test-HttpMethods.ps1` against its owned fresh Development/SQLite content root, with Coalesced/50 from committed `shells.json`. All GET/POST/PUT/DELETE calls returned HTTP200 and the expected `method-ok` body. Because the existing script can print failures without a nonzero exit, the private wrapper also required the four exact success lines, 4/4 summary and no FAIL. Runner exit 0; method log SHA-256 `520575cedc94f6b5534a0e3757db0609477d2a1f883af7f6fd0a52e350831304`.

Root verified all three configuration copies against source, 6,189 source/configuration files unchanged across build/run, and the full output binary manifest. Only owned PID 39518 was stopped; the loopback listener was absent afterwards. The isolated content root was retained. Raw build/run logs, manifests and root acceptance are in private journal `2400-t016/attempt1/`. This is SQLite HTTP-method E2E evidence, not PostgreSQL timing or the still-missing FR-004 external-reference restart proof.

## Architecture and generated maps

The corrected T017 Architecture run passed 635/635, with zero failures/skips and exit 0. TRX SHA-256: `d7feb5816f187c9c8d5e65cfedd2c314a2ad88058cdf9c2d4a4850813d6cdb8c`. The generated-maps checker also returned exit 0 after the narrow refresh.

The first run remains failed evidence: Architecture passed 610/635, and Maps reported eight stale files. The child fixture project had not been registered in `Elsa.Server.slnx`; its integration project also required the canonical solution folder. The solution registration is corrected. The repository architecture preparation script restored the required locked Debug/Release metadata; the map refresh now includes the child project, its references and the new specification. These are fixture registration, local prerequisite and generated-document corrections, not runtime repairs. Raw logs, TRX files and root receipt remain in private journal `2400-t017/`.

This accepts T017 for the current source. FR-004 external-payload restart coverage, its affected revalidation, final decision/review and PR/resulting-main gates remain pending.
