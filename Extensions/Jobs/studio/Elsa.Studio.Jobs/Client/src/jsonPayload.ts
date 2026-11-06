import { JobsFault } from "./jobsApi";
import type { InputView, ResultView } from "./types";

const maximumBytes = 1048576;
const bytes = (value: string) => new TextEncoder().encode(value).length;

// Compare decimal values without routing the original digits through a JS number.
function decimalIdentity(token: string) {
  const match = /^(-?)(\d+)(?:\.(\d+))?(?:[eE]([+-]?\d+))?$/.exec(token)!;
  const coefficient = (match[2] + (match[3] ?? "")).replace(/^0+/, "");
  if (!coefficient) return match[1] + "0";
  const digits = coefficient.replace(/0+$/, "");
  return match[1] + digits + "e" + (Number(match[4] ?? "0") - (match[3]?.length ?? 0) + coefficient.length - digits.length);
}

export function parseInvocationJson(json: string): unknown {
  if (bytes(json) > maximumBytes) throw new Error("The input envelope must fit within 1 MiB.");
  let value: unknown;
  try { value = JSON.parse(json); } catch { throw new Error("Input must be valid JSON."); }
  // The SDK JSON transport serializes JS values. Reject any numeric token whose value
  // would change, including overflow, underflow, rounded integers and negative zero.
  for (const match of json.matchAll(/"(?:[^"\\]|\\.)*"|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/g)) {
    const token = match[0]; if (token.startsWith('"')) continue;
    const number = Number(token);
    if (!Number.isFinite(number) || decimalIdentity(token) !== decimalIdentity(JSON.stringify(number)))
      throw new Error("Input contains a number that Studio cannot send without changing its value. Use a schema-supported string or edit the number.");
  }
  return value;
}

export function retainedValueJson(value: string | null | undefined) {
  if (typeof value !== "string" || bytes(value) > maximumBytes) throw new JobsFault(503);
  try { JSON.parse(value); } catch { throw new JobsFault(503); }
  return value;
}

// Embed only the authorized retained value text. Serializing the parsed value would
// round large integers and replace an overflowing JSON number with null.
export function payloadDocument(view: InputView | ResultView, kind: "input" | "result") {
  const payload = kind === "input" ? (view as InputView).input : (view as ResultView).payload;
  const result = view as ResultView;
  const metadata = kind === "input" ? { state: view.state } : { state: view.state, resultId: result.resultId,
    outcome: result.outcome, completedAt: result.completedAt };
  if (!payload) {
    if (kind === "input" || view.valueJson != null) throw new JobsFault(503);
    return JSON.stringify({ ...metadata, payload: null }, null, 2);
  }
  const raw = retainedValueJson(view.valueJson);
  return JSON.stringify(metadata).slice(0, -1) + `,"${kind === "input" ? "input" : "payload"}":{"schema":${JSON.stringify(payload.schema)},"value":${raw}}}`;
}
