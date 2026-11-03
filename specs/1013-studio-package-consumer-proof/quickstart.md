# Validate Package-Only Consumption

Use one genuine production candidate set at a common version. See the canonical command behavior in [Api closure](../../tools/studio/ApiPackageClosure.Probe/README.md) and [Hosting browser proof](../../tools/studio/HostingAssets.Probe/README.md).

```sh
studio_packages=/absolute/path/to/candidate-packages
python3 tools/studio/ApiPackageClosure.Probe/qualify.py --packages "$studio_packages"
python3 tools/studio/ApiPackageClosure.Probe/qualify.py --packages "$studio_packages" --omit-api-core-dependency
python3 tools/studio/ApiPackageClosure.Probe/qualify.py --packages "$studio_packages"
python3 tools/studio/HostingAssets.Probe/qualify.py --packages "$studio_packages"
python3 tools/studio/HostingAssets.Probe/qualify.py --packages "$studio_packages" --omit-published-vendor
python3 tools/studio/HostingAssets.Probe/qualify.py --packages "$studio_packages"
```

Expected exits are0,1,0 for each group. The dependency negative must fail at publish for missing Core, with its separate expected-failure observation true and original archives retained. The resource negative must specifically report `HTTP asset mismatch: /studio/vendor/react.js (status 404)`. Another failure is not a valid control. Check each owned report/log, original/mutated hashes, locked graph, published-only runtime and frontend-denial audit; both restored runs must pass.

No command publishes remotely. Producer/browser-driver tooling prerequisites do not become consumer prerequisites. Custom module/addon/auth/Console Stream combinations and CI wiring remain #3364/#3365.


The runtime configuration must also be present and applied. Run the browser proof with `--runtime-script-fault missing` and `--runtime-script-fault empty`; require the documented specific404/global-assignment failures in the Hosting README, then restore the normal profile. HTTP reports and root/deep browser routes must retain the configured nondefault HostId.
