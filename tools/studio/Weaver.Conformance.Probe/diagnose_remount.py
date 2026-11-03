#!/usr/bin/env python3
"""Temporary #3692 Linux diagnosis. Never establishes the independent package witness."""
import argparse
import json
from pathlib import Path

from artifacts import ConformanceRun
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
        print(json.dumps({"status": status, "source": run.source, "independentPackageWitness": False,
                          "scope": "original nine-case prefix and consumed-intent-remount only",
                          "failureDiagnostic": diagnostic}))
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
