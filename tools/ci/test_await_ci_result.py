"""Focused tests for the publish gate that waits for a commit's CI push run (#4002)."""

from __future__ import annotations

import io
import unittest
from contextlib import redirect_stderr

import await_ci_result as gate

SHA = "0123456789abcdef0123456789abcdef01234567"
OTHER = "fedcba9876543210fedcba9876543210fedcba98"


def ci_run(**overrides: object) -> dict[str, object]:
    run: dict[str, object] = {
        "id": 1,
        "path": ".github/workflows/ci.yml@refs/heads/main",
        "event": "push",
        "status": "completed",
        "conclusion": "success",
        "head_sha": SHA,
    }
    run.update(overrides)
    return run


class DecideTests(unittest.TestCase):
    def test_a_successful_completed_run_passes(self) -> None:
        self.assertEqual(gate.decide([ci_run()], SHA)[0], "success")

    def test_a_failed_run_refuses(self) -> None:
        self.assertEqual(gate.decide([ci_run(conclusion="failure")], SHA)[0], "failure")

    def test_a_cancelled_run_refuses(self) -> None:
        verdict, reason = gate.decide([ci_run(conclusion="cancelled")], SHA)
        self.assertEqual(verdict, "failure")
        self.assertIn("cancelled", reason)

    def test_an_in_progress_run_waits(self) -> None:
        self.assertEqual(gate.decide([ci_run(status="in_progress", conclusion=None)], SHA)[0], "wait")

    def test_a_failed_attempt_beside_a_running_one_waits(self) -> None:
        runs = [ci_run(id=1, conclusion="failure"), ci_run(id=2, status="queued", conclusion=None)]
        self.assertEqual(gate.decide(runs, SHA)[0], "wait")

    def test_a_later_success_beside_an_earlier_failure_passes(self) -> None:
        runs = [ci_run(id=1, conclusion="failure"), ci_run(id=2, conclusion="success")]
        self.assertEqual(gate.decide(runs, SHA)[0], "success")

    def test_no_visible_run_waits(self) -> None:
        self.assertEqual(gate.decide([], SHA)[0], "wait")

    def test_runs_for_other_commits_events_or_workflows_are_ignored(self) -> None:
        runs = [
            ci_run(head_sha=OTHER),
            ci_run(event="schedule"),
            ci_run(event="pull_request"),
            ci_run(path=".github/workflows/packages.yml@refs/heads/main"),
        ]
        self.assertEqual(gate.decide(runs, SHA)[0], "wait")


class WaitTests(unittest.TestCase):
    def run_gate(self, pages: list[list[dict[str, object]]], *, timeout: float = 600, interval: float = 60) -> tuple[int, list[str], list[float]]:
        now = [0.0]
        slept: list[float] = []
        logged: list[str] = []

        def fetch() -> list[dict[str, object]]:
            return pages.pop(0) if len(pages) > 1 else pages[0]

        def sleep(seconds: float) -> None:
            slept.append(seconds)
            now[0] += seconds

        code = gate.wait_for_success(fetch, SHA, timeout_seconds=timeout, interval_seconds=interval,
                                     clock=lambda: now[0], sleep=sleep, log=logged.append)
        return code, logged, slept

    def test_waits_through_a_running_run_then_passes(self) -> None:
        code, logged, slept = self.run_gate([[ci_run(status="in_progress", conclusion=None)], [ci_run()]])
        self.assertEqual((code, slept), (0, [60]))
        self.assertIn("succeeded", logged[-1])

    def test_refuses_as_soon_as_every_run_has_failed(self) -> None:
        code, _, slept = self.run_gate([[ci_run(conclusion="failure")]])
        self.assertEqual((code, slept), (1, []))

    def test_a_transient_api_error_is_retried_not_a_verdict(self) -> None:
        from urllib.error import HTTPError, URLError
        now = [0.0]
        slept: list[float] = []
        logged: list[str] = []
        answers: list[object] = [HTTPError("u", 502, "bad gateway", None, None), URLError("dns"), ValueError("bad json"), [ci_run()]]

        def fetch() -> list[dict[str, object]]:
            answer = answers.pop(0)
            if isinstance(answer, Exception):
                raise answer
            return answer

        def sleep(seconds: float) -> None:
            slept.append(seconds)
            now[0] += seconds

        code = gate.wait_for_success(fetch, SHA, timeout_seconds=600, interval_seconds=60, clock=lambda: now[0], sleep=sleep, log=logged.append)
        self.assertEqual((code, slept), (0, [60, 60, 60]))
        self.assertTrue(all("retrying" in line for line in logged[:3]))

    def run_errors(self, answers: list[object], *, timeout: float = 600) -> tuple[int, list[str], list[float]]:
        now = [0.0]
        slept: list[float] = []
        logged: list[str] = []

        def fetch() -> list[dict[str, object]]:
            answer = answers.pop(0) if len(answers) > 1 else answers[0]
            if isinstance(answer, Exception):
                raise answer
            return answer

        def sleep(seconds: float) -> None:
            slept.append(seconds)
            now[0] += seconds

        code = gate.wait_for_success(fetch, SHA, timeout_seconds=timeout, interval_seconds=60, clock=lambda: now[0], sleep=sleep, log=logged.append)
        return code, logged, slept

    def test_a_permission_refusal_fails_at_once(self) -> None:
        import io
        from email.message import Message
        from urllib.error import HTTPError
        for code, body in ((401, b""), (404, b"{}"), (403, b'{"message":"Resource not accessible by integration"}')):
            with self.subTest(code=code):
                result, logged, slept = self.run_errors([HTTPError("u", code, "refused", Message(), io.BytesIO(body))])
                self.assertEqual((result, slept), (1, []))
                self.assertIn("actions: read", logged[-1])

    def test_rate_limited_403s_are_retried_with_or_without_a_retry_after_header(self) -> None:
        import io
        from email.message import Message
        from urllib.error import HTTPError
        with_header = Message()
        with_header["Retry-After"] = "30"
        exhausted = Message()
        exhausted["x-ratelimit-remaining"] = "0"
        secondary = HTTPError("u", 403, "rate limited", Message(), io.BytesIO(b'{"message":"You have exceeded a secondary rate limit."}'))
        for error in (HTTPError("u", 403, "rate limited", with_header, None), HTTPError("u", 403, "rate limited", exhausted, None), secondary):
            with self.subTest(error=error):
                code, _, slept = self.run_errors([error, [ci_run()]])
                self.assertEqual((code, slept), (0, [60]))

    def test_a_truncated_body_is_retried(self) -> None:
        from http.client import IncompleteRead
        code, logged, slept = self.run_errors([IncompleteRead(b"partial"), [ci_run()]])
        self.assertEqual((code, slept), (0, [60]))
        self.assertIn("retrying", logged[0])

    def test_gives_up_at_the_deadline(self) -> None:
        code, logged, slept = self.run_gate([[ci_run(status="queued", conclusion=None)]], timeout=150, interval=60)
        self.assertEqual(code, 1)
        self.assertEqual(slept, [60, 60, 60])
        self.assertIn("Gave up", logged[-1])


class MainTests(unittest.TestCase):
    def test_rejects_invalid_input_with_exit_2(self) -> None:
        with redirect_stderr(io.StringIO()):
            self.assertEqual(gate.main(["--repository", "not-a-repo", "--sha", SHA]), 2)
            self.assertEqual(gate.main(["--repository", "o/r", "--sha", "abc"]), 2)
            self.assertEqual(gate.main(["--repository", "o/r", "--sha", SHA, "--timeout-minutes", "0"]), 2)


if __name__ == "__main__":
    unittest.main()
