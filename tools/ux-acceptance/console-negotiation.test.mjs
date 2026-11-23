import assert from "node:assert/strict";
import test from "node:test";
import { isReadOnlyConsoleNegotiation } from "../../Studio/tests/acceptance/consoleNegotiation.mjs";

const backend = "http://localhost:50925";
const endpoint = `${backend}/_elsa/server/diagnostics/console-logs/hub/negotiate?negotiateVersion=1`;

test("the observed empty SignalR negotiation is transport, not a data mutation", () => {
  for (const body of [null, ""]) assert.equal(isReadOnlyConsoleNegotiation("POST", endpoint, body, backend), true);
});

test("the transport allowance cannot hide mutations or requests to other hosts", () => {
  for (const [method, address, body] of [
    ["PUT", endpoint, null],
    ["DELETE", endpoint, null],
    ["POST", endpoint, "{}"],
    ["POST", endpoint, "mutation"],
    ["POST", endpoint.replace(":50925", ":50926"), null],
    ["POST", endpoint.replace("http:", "https:"), null],
    ["POST", endpoint.replace("/_elsa/server/diagnostics/console-logs/hub/negotiate", "/design/workflows/definitions"), null],
    ["POST", endpoint.replace("/negotiate", ""), null],
    ["POST", endpoint.replace("/negotiate", "/negotiate/other"), null],
    ["POST", endpoint.replace("=1", "=2"), null],
    ["POST", endpoint + "&negotiateVersion=1", null],
    ["POST", endpoint + "&action=write", null],
    ["POST", endpoint.split("?")[0], null],
    ["POST", endpoint + "#other", null],
    ["POST", endpoint.replace("//localhost", "//user@localhost"), null]
  ]) assert.equal(isReadOnlyConsoleNegotiation(method, address, body, backend), false, `${method} ${address}`);
});
