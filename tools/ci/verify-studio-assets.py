#!/usr/bin/env python3
"""Fail when a consolidated Studio frontend build omits host or RCL assets."""

from __future__ import annotations

import argparse
import json
import re
import tempfile
from pathlib import Path


EXPECTED_ASSETS = (
    "Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/index.html",
    "Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/assets/index.js",
    "Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/assets/index.css",
    "Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/vendor/react.js",
    "Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/vendor/react-dom.js",
    "Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/vendor/react-dom-client.js",
    "Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/vendor/react-query.js",
    "Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/vendor/lucide-react.js",
    "Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/vendor/studio-ui.js",
    "Studio/src/essentials/Elsa.Studio.Hosting/wwwroot/studio/sdk/index.js",
    "Studio/src/essentials/Elsa.Studio.Dashboard/wwwroot/studio/modules/dashboard/module.js",
    "Studio/src/essentials/Elsa.Studio.Workflows/wwwroot/studio/modules/workflows/module.js",
    "Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/wwwroot/studio/modules/agent-specialists/module.js",
    "Extensions/Attention/studio/Elsa.Studio.Attention/wwwroot/studio/modules/attention/module.js",
    "Extensions/ConsoleStream/studio/Elsa.Studio.ConsoleStream/wwwroot/studio/modules/console-stream/module.js",
    "Extensions/Connections/studio/Elsa.Studio.Connections/wwwroot/studio/modules/connections/module.js",
    "Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/wwwroot/studio/modules/open-telemetry/module.js",
    "Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/wwwroot/studio/modules/structured-logs/module.js",
    "Extensions/Etl/studio/Elsa.Studio.Etl/wwwroot/studio/modules/etl/module.js",
    "Extensions/ExpressionEditors.JavaScript/studio/Elsa.Studio.ExpressionEditors.JavaScript/wwwroot/studio/modules/expression-editors/javascript/module.js",
    "Extensions/ExpressionEditors.Liquid/studio/Elsa.Studio.ExpressionEditors.Liquid/wwwroot/studio/modules/expression-editors/liquid/module.js",
    "Extensions/ExtensionBuilder/studio/Elsa.Studio.ExtensionBuilder/wwwroot/studio/modules/extension-builder/module.js",
    "Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/wwwroot/studio/modules/features/module.js",
    "Extensions/Samples.WeatherForecast/studio/Elsa.Studio.Samples.WeatherForecast/wwwroot/studio/modules/weather/module.js",
    "Extensions/Secrets/studio/Elsa.Studio.Secrets/wwwroot/studio/modules/secrets/module.js",
    "Extensions/Weaver/studio/Elsa.Studio.Weaver.Workflows/wwwroot/studio/modules/weaver-workflows/module.js",
    "Extensions/Workflows.Dashboard/studio/Elsa.Studio.Workflows.Dashboard/wwwroot/studio/modules/workflows-dashboard/module.js",
)


# The producer contract is part of each module's static-web-asset package payload.
EXPECTED_ASSETS += tuple(path.rsplit("/", 1)[0] + "/entry-styles.json"
                         for path in EXPECTED_ASSETS if path.endswith("/module.js"))


def verify_entry_styles(root: Path) -> None:
    for path in EXPECTED_ASSETS:
        if not path.endswith("/entry-styles.json"):
            continue
        contract = root / path
        data = json.loads(contract.read_text())
        styles = data.get("styles")
        if data.get("version") != 1 or not isinstance(styles, list) or any(
            not isinstance(name, str) or not re.fullmatch(r"module-[A-Za-z0-9_-]+\.css", name)
            or not (contract.parent / name).is_file() for name in styles
        ) or len(styles) != len(set(styles)):
            raise ValueError(f"Invalid module entry stylesheet contract: {path}")


def missing_assets(root: Path) -> list[str]:
    return [path for path in EXPECTED_ASSETS if not (root / path).is_file()]


def self_test() -> None:
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        for path in EXPECTED_ASSETS:
            asset = root / path
            asset.parent.mkdir(parents=True, exist_ok=True)
            asset.write_text('{"version":1,"styles":[]}' if path.endswith("/entry-styles.json") else "")
        if missing_assets(root):
            raise AssertionError("Complete fixture was reported as missing assets.")
        verify_entry_styles(root)
        for path in EXPECTED_ASSETS:
            removed = root / path
            removed.unlink()
            if missing_assets(root) != [path]:
                raise AssertionError(f"Missing asset was not reported precisely: {path}")
            removed.write_text('{"version":1,"styles":[]}' if path.endswith("/entry-styles.json") else "")
        contract = root / next(path for path in EXPECTED_ASSETS if path.endswith("/entry-styles.json"))
        for data in [{"version": 2, "styles": []}, {"version": 1, "styles": ["../outside.css"]},
                     {"version": 1, "styles": ["module-missing.css"]}]:
            contract.write_text(json.dumps(data))
            try:
                verify_entry_styles(root)
            except ValueError:
                pass
            else:
                raise AssertionError("Invalid entry stylesheet contract was accepted: " + str(data))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="consolidated repository root")
    parser.add_argument("--self-test", action="store_true", help="exercise complete and missing-asset cases")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        print("Studio asset verifier self-test passed.")
        return
    missing = missing_assets(args.root.resolve())
    if missing:
        for path in missing:
            print(f"Missing generated Studio asset: {path}")
        raise SystemExit(1)
    verify_entry_styles(args.root.resolve())
    print(f"Verified {len(EXPECTED_ASSETS)} generated Studio host and RCL assets.")


if __name__ == "__main__":
    main()
