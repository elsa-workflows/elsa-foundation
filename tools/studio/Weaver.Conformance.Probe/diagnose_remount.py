#!/usr/bin/env python3
"""Temporary #3692 Linux diagnosis. Never establishes the independent package witness."""
import argparse
import json
from pathlib import Path
import subprocess
from types import SimpleNamespace

from artifacts import ConformanceRun, REPO, check
from journeys import qualify_distributions
from journey_runtime import JourneyRuntime, safe_browser_failure


class DiagnosticComplete(Exception):
    pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    if not args.output.is_absolute():
        parser.error("--output must be absolute")
    run = ConformanceRun(args.output, args.version)
    driver_source = run.source
    product_source = "2d7a636c89b9a0e0dc32e0d30b60e6317aac343a"
    subprocess.run(["git", "merge-base", "--is-ancestor", product_source, driver_source], cwd=REPO, check=True)
    shared = ["Directory.Packages.props", "Studio/Directory.Packages.props", "NuGet.config", "global.json", "Studio/package.json", "Studio/pnpm-lock.yaml"]
    check(not subprocess.check_output(["git", "diff", "--name-only", product_source, driver_source, "--", *shared], cwd=REPO, text=True).strip(),
          "The diagnostic needs the failed product's exact shared toolchain and dependency pins")
    changed = set(subprocess.check_output(["git", "diff", "--name-only", product_source, driver_source, "--", "tools/studio/Weaver.Conformance.Probe"], cwd=REPO, text=True).splitlines())
    allowed = {"tools/studio/Weaver.Conformance.Probe/" + name for name in ["browser.mjs", "journey_runtime.py", "diagnose_remount.py", "test_browser_failure_diagnostic.py"]}
    check(changed <= allowed, "The diagnostic must preserve every other failed-run driver/template input")
    # Product archives retain their real failed-main source. Driver immutability
    # remains the normal strict check, using a separate immutable context.
    immutable = SimpleNamespace(source=driver_source, report=run.report)
    run.assert_immutable_inputs = lambda: ConformanceRun.assert_immutable_inputs(immutable)
    run.source = product_source
    run.report.update(source=product_source, diagnosticDriverSource=driver_source, diagnosticOnly=True)
    original = JourneyRuntime.browser

    def bounded(runtime, records, name, journey, **options):
        value = original(runtime, records, name, journey, **options)
        if journey == "consumed-intent-remount":
            raise DiagnosticComplete()
        return value

    JourneyRuntime.browser = bounded
    status, exit_code = "diagnostic-failed", 1
    try:
        run.produce()
        qualify_distributions(run)
        raise RuntimeError("The diagnostic did not stop at its declared boundary")
    except DiagnosticComplete:
        status, exit_code = "diagnostic-passed", 0
    except Exception:
        # Report static categories/coordinates only; the raw browser log stays private.
        pass
    finally:
        JourneyRuntime.browser = original
        run.report.update(status=status, independentPackageWitness=False, diagnosticOnly=True)
        run.save()
        diagnostic = safe_browser_failure({"failureDiagnostic": run.report.get("browserFailureDiagnostic")})
        print(json.dumps({"status": status, "source": run.source, "driverSource": driver_source, "independentPackageWitness": False,
                          "scope": "original nine-case prefix and consumed-intent-remount only",
                          "failureDiagnostic": diagnostic}))
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
