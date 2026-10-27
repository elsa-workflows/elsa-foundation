#!/usr/bin/env python3
"""Qualify actual packaged Studio compositions and their causal failure controls."""

import argparse
import json
from pathlib import Path
import shutil
import sys

from artifacts import Artifacts, PROFILES, check, digest
import journeys


def qualify(args):
    p = Artifacts(args.output, args.packages, args.version)
    folder = Path(__file__).parent
    p.report["qualificationInputHashes"] = {str(path.relative_to(folder)): digest(path.read_bytes()) for path in folder.rglob("*")
                                            if path.is_file() and "__pycache__" not in path.parts}
    try:
        check(not args.packages or args.reference_web, "--packages requires --reference-web with the matching actual published distribution")
        p.produce()
        if args.reference_web:
            check(not (p.root / "reference-web-published").exists(), "Reference Web was already produced; do not replace its provenance")
            shutil.copytree(args.reference_web, p.root / "reference-web-published")
            p.report["referenceWebProducer"] = "supplied published distribution; producer revision is external provenance"
        p.module("1.0.0", "module-v1")
        p.module("2.0.0", "module-v2")
        for name in PROFILES:
            p.consumer(name)
        p.hide_build_inputs()
        p.report["results"] = {}
        steps = [
            ("baselineCompositions", journeys.baseline),
            ("actualReferenceWeb", journeys.actual_reference_web),
            ("coldAuthorization", journeys.cold_authorization),
            ("queryPermissionAndSuppression", journeys.policies),
            ("themeRestart", journeys.theme_restart),
            ("missingAssetControl", journeys.missing_asset_control),
        ]
        for label, execute in steps:
            p.report.update(status="running", stage=label)
            p.save()
            print(label, flush=True)
            p.report["results"][label] = execute(p)
            p.save()
        journeys.stream_client(p)
        streams = {}
        for label, control in [("normal", False), ("disabledAutoReload", True), ("restored", False)]:
            p.report.update(stage="liveStream:" + label)
            p.save()
            print("liveStream:" + label, flush=True)
            streams[label] = journeys.live_stream(p, "stream-" + label, control=control)
            p.report["results"]["liveStream"] = streams
            p.save()
        # A no-op reconciliation is not evidence for an AutoReload=false control.
        # Exercise that precise false-green risk through the same actual native host.
        p.report.update(stage="controlGuard:no-update")
        p.save()
        try:
            journeys.live_stream(p, "stream-no-update", control=True, update=False)
        except RuntimeError as error:
            observation = json.loads((p.root / "stream-no-update-reconcile.json").read_text())
            outcome = observation["outcome"]
            run = outcome.get("runResult") or {}
            check(str(error) == journeys.EXPECTED_UPDATE_ERROR and observation["httpStatus"] == 200
                  and outcome.get("outcomeCode") == 0 and run.get("isDegraded") is False
                  and not (run.get("changeSet") or {}).get("updated"), "No-update guard failed for an unrelated reason")
            p.report["results"]["noUpdateControlGuard"] = {"status": "failed", "stage": "expected-package-update",
                                                          "expectedFailureObserved": True, "observation": observation}
        else:
            raise RuntimeError("No-update reconciliation was incorrectly accepted as the disabled-reload control")
        for name, profile in p.profiles.items():
            check(not profile["audit"].exists() or not profile["audit"].read_text().strip(), name + " invoked consumer frontend tooling")
            p.report["profiles"][name].update(status="qualified", frontendInvocations=0)
        for package in p.report["packages"].values():
            archive = p.root / "hidden-build-feed" / Path(package["path"]).name
            check(digest(archive.read_bytes()) == package["archiveSha256"], "Original product archive changed during qualification")
        p.report.update(status="passed", stage="complete", frontendInvocations=0,
                        limitations=["Executed platform is the current supported native profile; no unavailable-platform skip is accepted",
                                     "Controlled backend validates Studio transport/auth behavior, not an Elsa Server deployment",
                                     "Native runtime feeds remain intentional deployed inputs; build inputs are hidden separately",
                                     "No arbitrary filesystem mutation/link-cycle recovery, release, deployment or publisher activation"])
        return 0
    except Exception as error:
        p.report.update(status="failed", error=str(error))
        return 1
    finally:
        p.save()
        print(json.dumps({key: p.report.get(key) for key in ["root", "status", "stage", "error"]}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="New owned artifact directory")
    parser.add_argument("--packages", type=Path, help="Actual Studio and lightweight Modularity candidate archives at one version")
    parser.add_argument("--reference-web", type=Path, help="Matching actual published Web distribution when supplying archives")
    parser.add_argument("--version", default="4.0.0-composition3364", help="Injected local producer candidate version")
    sys.exit(qualify(parser.parse_args()))
