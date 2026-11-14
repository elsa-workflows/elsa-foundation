"""Synthetic T045 reader controls only; these are never composed UX evidence."""
import copy
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

from case_records import retained
from effects_journeys import (CHECK_TOOL, EXPECTED_CASES, OWNER, HOME_PREFERENCES_PATH, ROUTE,
                             require_browser_captures, require_browser_terminal, require_called_refusal, require_uncertainty_presentation)
from test_effects_journeys import check_fixture, refusal_fixture


SOURCE = "a" * 40
SPEC = {"tool": CHECK_TOOL, "condition": "browser"}


def write_json(path, value):
    path.write_text(json.dumps(value) + "\n")
    return retained(path)


def png(width, height):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress((b"\0" + b"\x80\x80\x80" * width) * height)) + chunk(b"IEND", b""))


def capture_fixture(root):
    browser = {"effectsStateCaptures": {}, "evidence": [],
               "effectsLoading": {"actualBootstrapHeld": True, "fabricatedResponse": False},
               "effectsPolicyKeyboard": {"openedWithEnter": True, "closedWithEnter": True, "retainedSummaryFocus": True}}
    states = {"loading": [], "empty": [], "policy": [], "populated": ["awaiting-approval"],
              "review-" + CHECK_TOOL: ["awaiting-approval"], "result-" + CHECK_TOOL: ["performed"],
              "terminal": ["performed"], "recent": [], "reopened": ["performed"]}
    images = {(layout, theme): png(width, height) for layout, width, height in (("desktop", 1920, 1080), ("narrow", 390, 844))
              for theme in ("light", "dark")}
    for state, statuses in states.items():
        single = state in ("loading", "empty", "populated")
        for layout, theme in (("desktop", "light"),) if single else images:
            key = state + "/" + layout + "/" + theme
            image = root / (key.replace("/", "-") + ".png")
            image.write_bytes(images[layout, theme])
            viewport = {"width": 1920, "height": 1080} if layout == "desktop" else {"width": 390, "height": 844}
            value = {"state": state, "testedSource": SOURCE, "scenario": SPEC, "layout": layout, "theme": theme,
                     "viewport": viewport, "route": ROUTE, "snapshot": "Weaver assistant; Host privilege policy; " + state,
                     "image": retained(image), "statuses": statuses, "busy": "false", "messageCount": 0 if state in ("loading", "empty", "policy", "recent") else 2,
                     "composerEnabled": state not in ("loading", "populated", "review-" + CHECK_TOOL),
                     "enabledApply": 0, "policy": ["Enabled"] * 3, "focus": []}
            receipt = write_json(root / (key.replace("/", "-") + ".json"), value)
            browser["effectsStateCaptures"][key] = receipt
            browser["evidence"].extend((retained(image), receipt))
    for name, control in (("effects-policy-open", "Host privilege policy"), ("effects-policy-close", "Host privilege policy"),
                          ("effects-new-conversation", "New conversation"), ("effects-reopen-conversation", "Reopen the exact recent conversation")):
        browser["evidence"].append(write_json(root / (name + "-interaction.json"), {"method": "keyboard", "key": "Enter", "control": control, "completed": True}))
    browser["evidence"].append(write_json(root / "effects-terminal-composer-interaction.json", {"method": "keyboard", "control": "Ask Weaver", "completed": True,
        "focused": True, "typedCharacters": 12, "sendEnabled": True, "messagesBefore": 1, "messagesAfter": 1}))
    return browser


def replace_capture(browser, root, key, mutate):
    ref = browser["effectsStateCaptures"][key]
    value = json.loads(Path(ref["path"]).read_text())
    mutate(value)
    updated = write_json(Path(ref["path"]), value)
    browser["effectsStateCaptures"][key] = updated
    browser["evidence"] = [updated if row["path"] == ref["path"] else row for row in browser["evidence"]]


class EffectsCaptureIntegrityTests(unittest.TestCase):
    def test_complete_named_matrix_passes_its_reader_only(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            require_browser_captures(capture_fixture(root), root, SPEC, SOURCE)

    def test_missing_state_theme_or_narrow_capture_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            original = capture_fixture(root)
            for key in ("loading/desktop/light", "policy/narrow/dark", "review-" + CHECK_TOOL + "/narrow/light", "reopened/desktop/dark"):
                browser = copy.deepcopy(original)
                browser["effectsStateCaptures"].pop(key)
                with self.subTest(key=key), self.assertRaisesRegex(RuntimeError, "named state"):
                    require_browser_captures(browser, root, SPEC, SOURCE)

    def test_hash_valid_wrong_states_source_theme_and_composer_are_rejected(self):
        cases = [("loading/desktop/light", lambda row: row.update(composerEnabled=True)),
                 ("empty/desktop/light", lambda row: row.update(messageCount=1)),
                 ("populated/desktop/light", lambda row: row.update(messageCount=0)),
                 ("terminal/desktop/light", lambda row: row.update(statuses=["executed"])),
                 ("terminal/desktop/light", lambda row: row.update(composerEnabled=False)),
                 ("terminal/desktop/light", lambda row: row.update(busy="true")),
                 ("terminal/desktop/light", lambda row: row.update(enabledApply=1)),
                 ("policy/narrow/dark", lambda row: row.update(theme="light")),
                 ("policy/narrow/dark", lambda row: row.update(policy=["Disabled"] * 3)),
                 ("reopened/desktop/light", lambda row: row.update(testedSource="b" * 40)),
                 ("reopened/desktop/light", lambda row: row.update(snapshot=""))]
        for key, mutate in cases:
            with self.subTest(key=key), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                browser = capture_fixture(root)
                replace_capture(browser, root, key, mutate)
                with self.assertRaises(RuntimeError): require_browser_captures(browser, root, SPEC, SOURCE)

    def test_a_json_receipt_or_another_viewport_cannot_replace_the_actual_image(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            browser = capture_fixture(root)
            wrong = write_json(root / "not-an-image.json", {"status": "passed"})
            replace_capture(browser, root, "terminal/desktop/light", lambda row: row.update(image=wrong))
            with self.assertRaisesRegex(RuntimeError, "PNG"): require_browser_captures(browser, root, SPEC, SOURCE)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            browser = capture_fixture(root)
            other = json.loads(Path(browser["effectsStateCaptures"]["terminal/narrow/light"]["path"]).read_text())["image"]
            replace_capture(browser, root, "terminal/desktop/light", lambda row: row.update(image=other))
            with self.assertRaisesRegex(RuntimeError, "PNG"): require_browser_captures(browser, root, SPEC, SOURCE)

    def test_missing_or_incomplete_keyboard_evidence_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            browser = capture_fixture(root)
            browser["evidence"] = [row for row in browser["evidence"] if Path(row["path"]).name != "effects-reopen-conversation-interaction.json"]
            with self.assertRaisesRegex(RuntimeError, "keyboard"): require_browser_captures(browser, root, SPEC, SOURCE)


def terminal_fixture(root):
    prepared, execution, before, after, layout_before, layout_after, checks_before, checks_after = check_fixture()
    origin, session_path = "http://127.0.0.1:16666", "/_elsa/weaver/sessions/session-one"
    browser = {"provider": "copilot", "effectsRefs": {}, "effectsTerminal": {"sessionPath": session_path, "statuses": ["performed"],
               "composerTypedWithoutSending": True, "exactRecentSession": True},
               "effects": {"commandId": "command-one", "prepared": prepared, "execution": execution, "before": before, "after": after,
                           "layoutBefore": layout_before, "layoutAfter": layout_after, "checksBefore": checks_before, "checksAfter": checks_after}}
    def save(name, value): browser["effectsRefs"][name] = write_json(root / (name + ".json"), value)
    session = {"data": {"sessionId": "session-one", "messages": [{"role": "tool", "status": "completed", "content": json.dumps(execution["toolResult"])}]}}
    for name, endpoint, value in (("reopenedSession", session_path, session), ("recoveryOwner", OWNER, after),
                                  ("recoveryLayout", HOME_PREFERENCES_PATH, layout_after), ("recoveryChecks", OWNER + "/checks", checks_after),
                                  ("policyBootstrap-false", "/_elsa/weaver/bootstrap", {"data": {"privileges": {"administrative": False, "irrecoverable": False, "activation": False}}})):
        save(name, {"url": origin + endpoint, "status": 200, "value": value})
    delivery = {"url": origin + "/_elsa/weaver/proposals/proposal-one/execute", "request": {"revision": before["revision"]}, "status": 200, "response": {"data": execution}}
    save("execute", delivery); save("duplicate", delivery)
    trace = [{"kind": "copilot.call", "data": {"commandId": "command-one"}}]
    return browser, origin, trace


class EffectsTerminalIntegrityTests(unittest.TestCase):
    def test_uncertainty_needs_both_the_card_caution_and_the_separate_native_warning(self):
        warning = "Execution was interrupted after dispatch. Its effect is uncertain and will not be replayed."
        presentation = {"cardStatus": "uncertain", "cardText": "Widget check is unconfirmed. No matching owner receipt is available here. Use the shared operation status; a possibly completed action must not be repeated automatically.",
                        "warningText": warning}
        # The genuine card deliberately has no copy of the separate native warning.
        require_uncertainty_presentation(presentation, warning)
        for mutate in (lambda row: row.update(warningText=""),
                       lambda row: row.update(warningText="The operation was refused; no effect occurred."),
                       lambda row: row.update(cardStatus="performed"),
                       lambda row: row.update(cardText=warning),
                       lambda row: row.update(cardText="Widget check is unconfirmed.")):
            changed = copy.deepcopy(presentation); mutate(changed)
            with self.assertRaises(RuntimeError): require_uncertainty_presentation(changed, warning)

    def test_retained_performed_result_and_native_owner_remain_exact(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            browser, origin, trace = terminal_fixture(root)
            require_browser_terminal(browser, root, SPEC, origin, trace)
            for name, mutate in (("reopenedSession", lambda row: row["value"]["data"].update(sessionId="another-session")),
                                 ("duplicate", lambda row: row["response"]["data"]["toolResult"].update(toolCallId="another-call")),
                                 ("duplicate", lambda row: row["response"]["data"].update(status="executed")),
                                 ("recoveryChecks", lambda row: row["value"].update(ranCount=2)),
                                 ("recoveryOwner", lambda row: row["value"].update(revision="changed"))):
                original = json.loads(Path(browser["effectsRefs"][name]["path"]).read_text())
                changed = copy.deepcopy(original); mutate(changed)
                browser["effectsRefs"][name] = write_json(root / (name + ".json"), changed)
                with self.subTest(name=name), self.assertRaises(RuntimeError): require_browser_terminal(browser, root, SPEC, origin, trace)
                browser["effectsRefs"][name] = write_json(root / (name + ".json"), original)
            with self.assertRaisesRegex(RuntimeError, "additional"):
                require_browser_terminal(browser, root, SPEC, origin, trace + trace)

    def test_uncertain_lifecycle_requires_current_cause_and_invocation_on_both_adapters(self):
        for provider in ("copilot", "anthropic"):
            args, kwargs = refusal_fixture(provider, reviewed=True)
            result = {"toolCallId": args[6][1]["payload"]["toolCallId"], "toolName": CHECK_TOOL, "succeeded": False,
                      "payload": {"typeId": "weaver.operation-outcome", "schemaVersion": "1", "data": {"status": "uncertain", "InvocationId": "invocation-one"}}}
            args[8] = "uncertain"
            args[9]["messages"][0]["content"] = json.dumps(result)
            args[6][2]["payload"]["preparedInvocation"]["invocationId"] = "invocation-one"
            continuation = args[0][-1]["data"]["results"]
            if provider == "copilot": continuation[:] = [result]
            else:
                continuation[0]["result"] = json.dumps(result)
                args[0][-2]["data"]["messages"][-1]["contents"] = copy.deepcopy(continuation)
            kwargs["uncertain_invocation"] = "invocation-one"
            require_called_refusal(*args, **kwargs)
            for mutate in (lambda row: row["payload"]["data"].update(status="refused"),
                           lambda row: row["payload"]["data"].update(InvocationId="another-invocation"),
                           lambda row: row.update(toolCallId="another-call"),
                           lambda row: row.update(receipt={"scope": "noPersistedEffect"})):
                changed = copy.deepcopy(args)
                current = copy.deepcopy(result); mutate(current)
                changed[9]["messages"][0]["content"] = json.dumps(current)
                with self.subTest(provider=provider), self.assertRaises(RuntimeError): require_called_refusal(*changed, **kwargs)

    def test_new_cases_are_additive_on_both_adapters(self):
        self.assertEqual(200, len(EXPECTED_CASES))
        for provider in ("copilot", "anthropic"):
            self.assertIn(provider + "/browser:uncertain", EXPECTED_CASES)
            self.assertIn(provider + "/browser:administrative-only-allowed", EXPECTED_CASES)
            for boundary in ("unconfigured", "administrative-only", "approval-disabled", "execution-disabled"):
                self.assertIn(provider + "/browser-policy:" + boundary, EXPECTED_CASES)


if __name__ == "__main__": unittest.main()
