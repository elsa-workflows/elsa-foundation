"""Inert controls for install_playwright_chromium.py (#4162). A fake runner stands in for Playwright, apt and node,
so no package or browser is installed."""
import io
import unittest
from contextlib import redirect_stdout
from unittest.mock import patch

import install_playwright_chromium as installer

# The two report forms Playwright 1.61.1 prints for `install-deps --dry-run` (reportMissingDependenciesLinux).
ALL_PRESENT = "All system dependencies are installed.\n"
FONTS_MISSING = """Missing system dependencies (9):
  fonts-freefont-ttf
  fonts-ipafont-gothic
  fonts-tlwg-loma-otf
  fonts-unifont
  fonts-wqy-zenhei
  xfonts-cyrillic
  xfonts-encodings
  xfonts-scalable
  xfonts-utils
"""
LIBRARY_MISSING = """Missing system dependencies (2):
  fonts-unifont
  libnss3
"""
SIMULATION_FAILED = """Error: 'apt-get install -s' exited with code 100:
E: Unable to locate package libavif16
"""


class FakeRunner:
    """Answers each command by its kind; `apt` is the list of exit codes for successive apt download attempts."""

    def __init__(self, reports, apt=(), download=(0,), launch=0, update=0):
        self.reports = list(reports)
        self.apt = list(apt)
        self.download = list(download)
        self.launch = launch
        self.update = update
        self.commands = []

    def __call__(self, command, capture):
        self.commands.append(list(command))
        if "--dry-run" in command:
            return installer.Result(1, self.reports.pop(0))
        if command[-2:] == ["install", "chromium"]:
            return installer.Result(self.download.pop(0) if len(self.download) > 1 else self.download[0])
        if command[:2] == ["node", "-e"]:
            return installer.Result(self.launch)
        if "update" in command:
            return installer.Result(self.update)
        if "--download-only" in command:
            return installer.Result(self.apt.pop(0))
        if "--no-download" in command:
            return installer.Result(0)
        raise AssertionError(f"unexpected command {command}")

    def kinds(self):
        names = []
        for command in self.commands:
            if "--dry-run" in command:
                names.append("report")
            elif command[-2:] == ["install", "chromium"]:
                names.append("browser")
            elif command[:2] == ["node", "-e"]:
                names.append("launch")
            elif "update" in command:
                names.append("update")
            elif "--download-only" in command:
                names.append("fetch")
            else:
                names.append("dpkg")
        return names


def install(runner):
    sleeps = []
    subject = installer.Installer("pw", run=runner, sleep=sleeps.append)
    with redirect_stdout(io.StringIO()) as output:
        try:
            subject.install()
            error = None
        except installer.InstallError as raised:
            error = str(raised)
    return subject, sleeps, error, output.getvalue()


class ParseTests(unittest.TestCase):
    def test_reads_both_report_forms(self):
        self.assertEqual(installer.parse_missing(ALL_PRESENT), [])
        self.assertEqual(len(installer.parse_missing(FONTS_MISSING)), 9)
        self.assertEqual(installer.parse_missing(LIBRARY_MISSING), ["fonts-unifont", "libnss3"])

    def test_unknown_or_truncated_reports_are_not_guessed(self):
        self.assertIsNone(installer.parse_missing(SIMULATION_FAILED))
        self.assertIsNone(installer.parse_missing(""))
        self.assertIsNone(installer.parse_missing("Missing system dependencies (3):\n  libnss3\n"))

    def test_fonts_are_told_apart_from_libraries_and_tools(self):
        required, fonts = installer.split_fonts(["xvfb", "fonts-unifont", "libnss3", "xfonts-utils"])
        self.assertEqual(required, ["xvfb", "libnss3"])
        self.assertEqual(fonts, ["fonts-unifont", "xfonts-utils"])


class InstallTests(unittest.TestCase):
    def test_complete_image_never_calls_apt(self):
        runner = FakeRunner([ALL_PRESENT])
        _, sleeps, error, _ = install(runner)
        self.assertIsNone(error)
        self.assertEqual(runner.kinds(), ["browser", "report", "launch"])
        self.assertEqual(sleeps, [])

    def test_mirror_outage_recovers_on_a_later_attempt(self):
        runner = FakeRunner([FONTS_MISSING], apt=[100, 100, 0])
        subject, sleeps, error, _ = install(runner)
        self.assertIsNone(error)
        self.assertEqual(sleeps, [15, 45])
        self.assertEqual(subject.warnings, [])
        self.assertEqual(runner.kinds()[-2:], ["dpkg", "launch"])
        fetch = next(command for command in runner.commands if "--download-only" in command)
        # timeout runs under sudo, as root, so it can stop apt-get when the mirrors hang.
        self.assertEqual(fetch[:6], ["sudo", "env", "DEBIAN_FRONTEND=noninteractive", "timeout", "--kill-after=15",
                                     str(installer.APT_DOWNLOAD_TIMEOUT)])
        update = next(command for command in runner.commands if "update" in command)
        self.assertEqual(update[:4], ["sudo", "env", "DEBIAN_FRONTEND=noninteractive", "timeout"])
        offline_install = next(command for command in runner.commands if "--no-download" in command)
        self.assertNotIn("timeout", offline_install)
        self.assertEqual(fetch[-9:], installer.parse_missing(FONTS_MISSING))

    def test_lasting_outage_for_fonts_only_warns_and_still_proves_the_browser(self):
        runner = FakeRunner([FONTS_MISSING], apt=[100, 100, 100])
        subject, sleeps, error, output = install(runner)
        self.assertIsNone(error)
        self.assertEqual(sleeps, [15, 45])
        self.assertEqual(runner.kinds().count("fetch"), 3)
        self.assertEqual(runner.kinds()[-1], "launch")
        self.assertIn("::warning title=Playwright fonts not installed::", output)
        self.assertIn("fonts-unifont", subject.warnings[0])

    def test_lasting_outage_for_a_library_fails_and_names_it(self):
        runner = FakeRunner([LIBRARY_MISSING], apt=[100, 100, 100])
        _, _, error, _ = install(runner)
        self.assertIn("libnss3", error)
        self.assertNotIn("fonts-unifont", error)
        self.assertNotIn("launch", runner.kinds())

    def test_browser_that_does_not_launch_fails(self):
        runner = FakeRunner([ALL_PRESENT], launch=1)
        _, _, error, _ = install(runner)
        self.assertIn("did not launch", error)

    def test_browser_download_is_retried_then_fails_clearly(self):
        recovered = FakeRunner([ALL_PRESENT], download=[1, 0])
        self.assertIsNone(install(recovered)[2])
        self.assertEqual(recovered.kinds()[:2], ["browser", "browser"])

        broken = FakeRunner([], download=[1])
        _, sleeps, error, _ = install(broken)
        self.assertIn("could not be downloaded after 3 attempts", error)
        self.assertEqual(broken.kinds(), ["browser"] * 3)
        self.assertEqual(sleeps, [15, 45])

    def test_failed_simulation_refreshes_lists_once_then_asks_again(self):
        runner = FakeRunner([SIMULATION_FAILED, ALL_PRESENT])
        _, _, error, _ = install(runner)
        self.assertIsNone(error)
        self.assertEqual(runner.kinds(), ["browser", "report", "update", "report", "launch"])

    def test_unreadable_report_fails_instead_of_skipping_dependencies(self):
        runner = FakeRunner([SIMULATION_FAILED, SIMULATION_FAILED])
        _, _, error, _ = install(runner)
        self.assertIn("not in a known form", error)

        offline = FakeRunner([SIMULATION_FAILED], update=100)
        _, _, error, _ = install(offline)
        self.assertIn("apt-get update failed after 3 attempts", error)

    def test_main_reports_an_error_annotation_and_exit_code(self):
        with patch.object(installer.Installer, "install", side_effect=installer.InstallError("boom")):
            with redirect_stdout(io.StringIO()) as output:
                code = installer.main(["--playwright", "pw"])
        self.assertEqual(code, 1)
        self.assertIn("::error title=Chromium install failed::boom", output.getvalue())


if __name__ == "__main__":
    unittest.main()
