import React from "react";
import type { StudioWeaverToolResult } from "../../sdk";

/** Show the normalized engine diagnostic without interpreting module payloads or replacing their presentation. */
export function WeaverResultError({ error, summary }: { error: StudioWeaverToolResult["error"]; summary: string }) {
  return error?.message && error.message !== summary ? <p>{error.message}</p> : null;
}
