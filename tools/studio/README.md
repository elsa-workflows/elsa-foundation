# Studio package qualification

Run from the repository root with the supported .NET 10/native profile, Python 3,
the built Studio producer frontend and Studio's Playwright Chromium installation.
Commit inputs before a frozen run:

```sh
python3 tools/ci/verify_studio_nuget_packages.py --self-test
python3 tools/ci/qualify_studio_consumers.py --self-test
python3 tools/ci/qualify_studio_consumers.py
```

The last command produces an owned committed snapshot of the actual 22 Studio
packages, the two lightweight Modularity packages and the actual reference Web,
then runs the complete archive/consumer/lifecycle matrix. It does not publish.
`--version` selects the injected local candidate version; `--output` selects a new
owned directory. Producer frontend tooling and browser automation are distinct
from the denied consumer frontend toolchain.

For candidates already built by CI, use all three matching inputs:

```sh
python3 tools/ci/qualify_studio_consumers.py \
  --packages /absolute/studio-packages \
  --modularity-packages /absolute/modularity-packages \
  --reference-web /absolute/published-reference-web \
  --expected-producer-commit "$GITHUB_SHA" \
  --output /absolute/new-qualification-directory
```

The expected commit is optional for externally supplied development artifacts;
their actual repository metadata is retained without inventing current-source
provenance. CI requires every candidate's repository commit and the tool revision
to match its producer commit. The reference application owns its selected modules;
an available sample package need not be installed in that distribution. Selected
reference DLLs must match the candidate archives and its version stamp.

| Proof | Executed boundary |
| --- | --- |
| Archive verifier | Exact 22 Studio identities, common version/license/icon; six public dependency boundaries; required producer assets and every generated public payload, including CSS and lazy chunks |
| Archive controls | Original/restored success; genuine dependency/framework omission, exclusive/empty dependency ranges, missing vendor/CSS/chunk, empty entry and changed SDK failures; originals immutable |
| [API closure](ApiPackageClosure.Probe/README.md) | Isolated package-only restore/publish/run; precise missing-Core dependency control and restoration |
| [Hosting assets](HostingAssets.Probe/README.md) | Actual standard/fallback shell, custom frontend opt-out and collision; vendor/runtime faults and restoration; archive/cache/publish/HTTP/browser observations |
| [Composition](Composition.Probe/README.md) | Four external custom-module/optional profiles, separate actual Web, cold auth, managed theme restart and actual network stream across native replacement |
| [Hosting reuse/pruning](HostingReuse.Probe/README.md) | Existing scripted shared-adapter sequencing plus actual enrolled store/admitted range overlap, each normal/control/restored; this is distinct from the actual production package consumers |

Only the precise intended stage, diagnostic, input bytes and causal state qualify
a failed control. Its child remains `status: failed`, with separate expected-failure
recognition. A no-op reconcile cannot qualify as evidence for disabled automatic
reload: the genuine expected v2 update and archive hash are required, and the
actual no-update case must be rejected. Restore/network/SDK failures, native
refusal, missing reports, tool mismatches and unrun cases fail the aggregate.

The printed root contains `report.json`, complete child reports/logs, candidates,
isolated graphs and owned published processes' observations. `evidence/index.json`
maps retained receipts, graphs, screenshots and private mutant archives to hashes;
Every archive child receipt referenced by the aggregate is retained and hash-checked,
including original, fault and restored JSON. Literal project pins must retain the
SDK-generated inclusive minimum without an upper bound; other range forms fail
the archive contract.
CI uploads these and the original product candidates, without caches or full
published/source trees. The initial archive verifier also writes a failed receipt
when its check fails before consumer execution. Existing publisher holds and
read-only validation permissions are unchanged.

The [accepted hosting contract](../../docs/reports/studio-hosting-contract.md)
owns product boundaries. These proofs do not claim arbitrary filesystem mutation,
source-link cycle recovery, performance measurement, deployed Elsa Server behavior,
remote publication or deployment.
