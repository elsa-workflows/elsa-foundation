# Independent native Dashboard package witness

The owned runner builds/locally packs the exact committed source, restores a package-only consumer online and then from its isolated offline feed, publishes it without frontend tooling, and exercises actual HTTP APIs with disposable SQLite definitions and Acme business records. It starts/stops only its own hosts. No production credential or model is used.

```sh
python3 tools/studio/Dashboard.Conformance.Probe/qualify.py \
  --output /tmp/dashboard-package-unique \
  --version 4.0.0-dashboard-probe
```

Use Python3.12 or newer and a new absolute output directory outside the checkout. Final qualification requires a clean committed tree. `--allow-dirty` is explicitly preliminary and cannot qualify a commit. The version names local archives only; this command publishes no package or release to a service.

The witness checks installed archive/assembly hashes, native discovery and record creation, independent author/viewer numerical results, tenant/record/field/aggregate/anonymous denials, authorized drill, private saved-question persistence, contribution disable/restore and unchanged common installed bytes. The consumer has no project references; production modules keep their own business storage/authority. The default Acme record store remains ephemeral.

`report.json`, private profile files and raw logs stay private: fixture tokens and response evidence may be present. Only `safe-receipt.json` is intended for shared evidence. It allowlists status/source, case/stage outcomes, package hashes and boundary flags; it omits commands, profile tokens, paths and raw responses. A passing receipt qualifies this independent native package witness only. Live inference, composed Studio/IAM/UX and final #3604 acceptance remain separate gates.

`test_qualification.py` checks refusal of incomplete/duplicated evidence and safe receipt extraction. Those controls do not replace running the package/native witness.
