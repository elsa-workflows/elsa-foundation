#!/usr/bin/env python3
"""Qualify independent Weaver distributions using real owned package consumers."""
from __future__ import annotations

import argparse
from pathlib import Path
import re


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path, help="New absolute owned directory outside the checkout")
    parser.add_argument("--version", default="4.0.0-weaver3438", help="Unique local candidate version; never published")
    options = parser.parse_args()
    if not options.output.is_absolute():
        parser.error("--output must be absolute")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", options.version):
        parser.error("--version must be a semantic version")
    from artifacts import ConformanceRun
    ConformanceRun(options.output.resolve(), options.version).execute()


if __name__ == "__main__":
    main()
