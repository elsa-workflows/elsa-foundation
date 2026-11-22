import { URL } from "node:url";

// SignalR uses an empty POST to choose a connection transport. Keep this one endpoint
// distinct from data mutations; other POSTs, hub messages and nonempty bodies still fail.
export function isReadOnlyConsoleNegotiation(method, address, body, backend) {
  const url = new URL(address);
  return method === "POST" && url.origin === new URL(backend).origin &&
    url.pathname === "/_elsa/server/diagnostics/console-logs/hub/negotiate" &&
    !url.username && !url.password && !url.hash &&
    url.searchParams.size === 1 && url.searchParams.get("negotiateVersion") === "1" &&
    (body === null || body === "");
}
