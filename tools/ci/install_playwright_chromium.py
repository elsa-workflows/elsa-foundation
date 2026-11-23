#!/usr/bin/env python3
"""Install Playwright's Chromium on a GitHub-hosted Ubuntu runner without needing apt when the image already has
what Chromium needs (#4162).

`playwright install --with-deps chromium` always runs `apt-get update && apt-get install` for the full dependency
list, so every browser job depended on the Ubuntu mirrors resolving. Some runners intermittently cannot resolve
azure.archive.ubuntu.com or security.ubuntu.com, and the step then fails after about ten minutes with exit code 100.
On the ubuntu-latest image every shared library Chromium loads is already installed; only Playwright's extra font
packages are missing.

This script does the install in four steps:

1. Download Chromium with `playwright install chromium` (Playwright's CDN, not apt), with bounded retries.
2. Ask Playwright which of its system packages are missing: `playwright install-deps --dry-run chromium` simulates
   the install with `apt-get install -s`, which reads the local package lists and needs no network.
3. Send only the missing packages to apt, with bounded retries. Each attempt runs `apt-get update` and
   `apt-get install --download-only` under a timeout (the network part, safe to stop), then installs from the
   downloaded archives without the network. A missing shared library or tool fails the step after its retries.
   Missing font packages (fonts-*, xfonts-*) only change how non-Latin text is drawn, so when apt still cannot
   fetch them the step continues with a warning annotation that names them.
4. Launch Chromium once and open a page, so the step only succeeds when the browser actually runs here.

Usage (from the directory whose node_modules holds Playwright):
  install_playwright_chromium.py [--playwright ./node_modules/.bin/playwright]
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
import time
from dataclasses import dataclass, field
from typing import Callable, Sequence

FONT_PREFIXES = ("fonts-", "xfonts-")

ATTEMPTS = 3
BACKOFF_SECONDS = (15, 45)
BROWSER_DOWNLOAD_TIMEOUT = 600
APT_UPDATE_TIMEOUT = 60
APT_DOWNLOAD_TIMEOUT = 120
APT_OPTIONS = ("-o", "DPkg::Lock::Timeout=120")

ALL_INSTALLED = "All system dependencies are installed."
MISSING_HEADER = re.compile(r"^Missing system dependencies \((\d+)\):[ \t]*$", re.MULTILINE)

LAUNCH_CHECK = """
let playwright;
for (const name of ['@playwright/test', 'playwright']) {
  try { playwright = require(name); break; } catch {}
}
if (!playwright) {
  console.error('Neither @playwright/test nor playwright resolves from ' + process.cwd());
  process.exit(2);
}
(async () => {
  const browser = await playwright.chromium.launch();
  const page = await browser.newPage();
  await page.setContent('<p>ready</p>');
  const version = browser.version();
  await browser.close();
  console.log('Chromium ' + version + ' launched and rendered a page.');
})().catch(error => { console.error(error); process.exit(1); });
"""


class InstallError(Exception):
    """The browser cannot be installed or run on this runner."""


@dataclass
class Result:
    code: int
    output: str = ""


Runner = Callable[[Sequence[str], bool], Result]


def run_command(command: Sequence[str], capture: bool) -> Result:
    print("$ " + " ".join(command), flush=True)
    if capture:
        completed = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        print(completed.stdout, end="", flush=True)
        return Result(completed.returncode, completed.stdout)
    return Result(subprocess.run(command).returncode)


def with_timeout(seconds: int, command: Sequence[str]) -> list[str]:
    # coreutils timeout signals the whole process group it starts, so apt's fetch methods stop with it. Under sudo it
    # runs as root and can stop apt-get; a timeout outside sudo could not.
    return ["timeout", "--kill-after=15", str(seconds), *command]


def sudo(command: Sequence[str]) -> list[str]:
    return ["sudo", "env", "DEBIAN_FRONTEND=noninteractive", *command]


def parse_missing(output: str) -> list[str] | None:
    """Missing package names from Playwright's dry-run report, [] when none are missing, or None when the report is
    not one of the two forms Playwright prints (for example when the simulation itself failed)."""
    header = MISSING_HEADER.search(output)
    if header is None:
        return [] if ALL_INSTALLED in output else None
    packages: list[str] = []
    # The header pattern stops before its line break, so the first split item is the header line's empty rest.
    for line in output[header.end():].splitlines()[1:]:
        if not line.startswith((" ", "\t")) or not line.strip():
            break
        packages.append(line.strip())
    return packages if len(packages) == int(header.group(1)) else None


def split_fonts(packages: Sequence[str]) -> tuple[list[str], list[str]]:
    fonts = [package for package in packages if package.startswith(FONT_PREFIXES)]
    required = [package for package in packages if not package.startswith(FONT_PREFIXES)]
    return required, fonts


@dataclass
class Installer:
    playwright: str
    run: Runner = run_command
    sleep: Callable[[float], None] = time.sleep
    attempts: int = ATTEMPTS
    backoff: Sequence[int] = BACKOFF_SECONDS
    warnings: list[str] = field(default_factory=list)

    def retry(self, description: str, attempt: Callable[[], bool]) -> bool:
        for number in range(1, self.attempts + 1):
            print(f"::group::{description} (attempt {number} of {self.attempts})", flush=True)
            succeeded = attempt()
            print("::endgroup::", flush=True)
            if succeeded:
                return True
            if number < self.attempts:
                delay = self.backoff[min(number - 1, len(self.backoff) - 1)]
                print(f"{description} failed on attempt {number}; retrying in {delay} s.", flush=True)
                self.sleep(delay)
        return False

    def download_browser(self) -> None:
        command = with_timeout(BROWSER_DOWNLOAD_TIMEOUT, [self.playwright, "install", "chromium"])
        if not self.retry("Download Chromium", lambda: self.run(command, False).code == 0):
            raise InstallError(f"Chromium could not be downloaded after {self.attempts} attempts.")

    def apt_update(self) -> bool:
        return self.run(sudo(with_timeout(APT_UPDATE_TIMEOUT, ["apt-get", *APT_OPTIONS, "update"])), False).code == 0

    def apt_attempt(self, packages: Sequence[str]) -> bool:
        # A failed update is not final: the image's lists may still name reachable archives, so try the download.
        self.apt_update()
        download = ["apt-get", *APT_OPTIONS, "install", "-y", "--no-install-recommends", "--download-only", *packages]
        if self.run(sudo(with_timeout(APT_DOWNLOAD_TIMEOUT, download)), False).code != 0:
            return False
        install = sudo(["apt-get", *APT_OPTIONS, "install", "-y", "--no-install-recommends", "--no-download",
                        *packages])
        return self.run(install, False).code == 0

    def missing_packages(self) -> list[str]:
        command = [self.playwright, "install-deps", "--dry-run", "chromium"]
        missing = parse_missing(self.run(command, True).output)
        if missing is not None:
            return missing
        # The simulation reads only the local package lists; it fails when they lack a package Playwright names.
        # Refresh them once (with retries) and ask again.
        if not self.retry("Refresh apt package lists", self.apt_update):
            raise InstallError("Playwright could not report missing system packages, and apt-get update failed "
                               f"after {self.attempts} attempts.")
        missing = parse_missing(self.run(command, True).output)
        if missing is None:
            raise InstallError("Playwright's dependency report (install-deps --dry-run) is not in a known form. "
                               "If Playwright changed it, update tools/ci/install_playwright_chromium.py.")
        return missing

    def install_system_packages(self) -> None:
        missing = self.missing_packages()
        if not missing:
            print("The runner image already has every system package Playwright lists for Chromium; apt is not used.")
            return
        required, fonts = split_fonts(missing)
        if self.retry("Install missing system packages", lambda: self.apt_attempt(missing)):
            return
        if required:
            raise InstallError(f"apt could not install {' '.join(required)} after {self.attempts} attempts. "
                               "Chromium needs these packages; see the apt output above for the cause.")
        message = (f"apt could not install the font packages {' '.join(fonts)} after {self.attempts} attempts. "
                   "Chromium runs without them; non-Latin text renders with fallback glyphs in this job.")
        self.warnings.append(message)
        print(f"::warning title=Playwright fonts not installed::{message}", flush=True)

    def verify_launch(self) -> None:
        if self.run(["node", "-e", LAUNCH_CHECK], False).code != 0:
            raise InstallError("Chromium was installed but did not launch on this runner; see the error above.")

    def install(self) -> None:
        self.download_browser()
        self.install_system_packages()
        self.verify_launch()


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--playwright", default="./node_modules/.bin/playwright",
                        help="Playwright CLI to use (default: ./node_modules/.bin/playwright)")
    arguments = parser.parse_args(argv)
    try:
        Installer(arguments.playwright).install()
    except InstallError as error:
        print(f"::error title=Chromium install failed::{error}", flush=True)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
