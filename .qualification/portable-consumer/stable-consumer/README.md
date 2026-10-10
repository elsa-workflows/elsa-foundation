# Portable stable CShells and Nuplane consumer

This is a portable copy of the accepted stable-family consumer. Its `consumer/Program.cs`, project, assertions, and verification script are copied byte-for-byte from the accepted `stable-consumer` artifact. The runner changes only the fixture-feed path so the two copied fixture archives live under this portable root.

The accepted public NuGet audit JSON files are not duplicated here. `package-manifest.json` carries all 18 exact NuGet.org flat-container URLs and SHA-256 values, plus hashes of the two audit files from which those values were taken. `download-public-archives.py` checks the manifest's package identities and URLs, downloads the archives, and rejects any hash mismatch before placing a file in the expected sibling package directories.

## Hosted Ubuntu run

Use a fresh checkout or extraction of this directory on an Ubuntu runner with Python 3 and .NET SDK/runtime 8, 9, and 10 already installed. The .NET SDKs can be supplied by the runner's normal setup-dotnet step. From this directory, run:

```bash
python3 download-public-archives.py
python3 stable-consumer/run-stable-consumer.py \
  hosted-stable-2026-10 \
  0.0.30 \
  0.0.11 \
  https://api.nuget.org/v3/index.json \
  https://api.nuget.org/v3/index.json
```

Use a fresh copy of the payload for each qualification run, or choose a new phase name with no existing phase cache or evidence. The consumer restores from canonical NuGet.org with package-source mapping, uses a phase-specific fresh cache, and performs the established sequential build and runtime scenarios for `net8.0`, `net9.0`, and `net10.0`. It records logs and evidence under `stable-consumer/evidence/<phase>/` and checks that its scenario-run roots are cleaned up.

The test covers the existing public-package-to-serving-shell scenarios only. It makes no package-pruning or assembly-unloading claim.
