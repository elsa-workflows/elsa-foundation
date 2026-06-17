import { describe, expect, it } from "vitest";
import {
  appendConsoleEntries,
  createConsoleFilter,
  createConsoleEntryFromLine,
  formatConsoleSourceLabel,
  getConsoleStreamName,
  parseAnsiSegments,
  isRecoverableConsoleStreamError,
  register,
  type ConsoleEntry
} from "../module";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

describe("console stream module", () => {
  it("registers a bottom panel contribution", () => {
    const panels: unknown[] = [];
    const api = {
      host: { http: { getJson: async () => ({}) } },
      panels: { add: (panel: unknown) => panels.push(panel) }
    } as ElsaStudioModuleApi;

    register(api);

    expect(panels).toHaveLength(1);
    expect(panels[0]).toMatchObject({
      id: "console-stream",
      title: "Console",
      order: 1000
    });
  });

  it("normalizes console stream names", () => {
    expect(getConsoleStreamName(0)).toBe("stdout");
    expect(getConsoleStreamName("stdout")).toBe("stdout");
    expect(getConsoleStreamName(1)).toBe("stderr");
    expect(getConsoleStreamName("stderr")).toBe("stderr");
    expect(getConsoleStreamName("Stderr")).toBe("stderr");
  });

  it("creates entries from streamed lines", () => {
    const entry = createConsoleEntryFromLine({
      id: "line-1",
      receivedAt: "2026-06-15T00:00:00.000Z",
      sequence: 5,
      stream: 1,
      text: "failed",
      source: {
        id: "pod-source",
        displayName: "Elsa.Server",
        serviceName: "elsa-server",
        podName: "elsa-server-7d9f",
        containerName: "server",
        namespace: "prod"
      }
    });

    expect(entry).toEqual({
      id: "line-1",
      timestamp: "2026-06-15T00:00:00.000Z",
      sequence: 5,
      stream: "stderr",
      text: "failed",
      sourceId: "pod-source",
      sourceLabel: "prod / elsa-server-7d9f / server"
    });
  });

  it("trims appended entries to the maximum console size", () => {
    const current = Array.from({ length: 1_999 }, (_, index) => entry(index));
    const appended = appendConsoleEntries(current, [entry(1_999), entry(2_000)]);

    expect(appended).toHaveLength(2_000);
    expect(appended[0].id).toBe("line-1");
    expect(appended.at(-1)?.id).toBe("line-2000");
  });

  it("parses ANSI color and reset segments", () => {
    expect(parseAnsiSegments("plain")).toEqual([{ text: "plain", className: "" }]);
    expect(parseAnsiSegments("\x1b[31mred\x1b[0m normal")).toEqual([
      { text: "red", className: "console-stream-ansi-fg-red" },
      { text: " normal", className: "" }
    ]);
  });

  it("treats stream timeout errors as recoverable", () => {
    expect(isRecoverableConsoleStreamError(new Error("Server timeout elapsed without receiving a message from the server."))).toBe(true);
    expect(isRecoverableConsoleStreamError(new Error("Something else"))).toBe(false);
  });

  it("formats process and pod source labels", () => {
    expect(formatConsoleSourceLabel({
      id: "machine:123",
      displayName: "Elsa.Server",
      machineName: "machine",
      processId: 123
    })).toBe("Elsa.Server · machine:123");

    expect(formatConsoleSourceLabel({
      id: "pod-source",
      podName: "elsa-server-7d9f",
      containerName: "server",
      namespace: "prod"
    })).toBe("prod / elsa-server-7d9f / server");
  });

  it("omits sourceId when all sources are selected", () => {
    expect(createConsoleFilter(null)).toEqual({ limit: 2000 });
    expect(createConsoleFilter("source-1")).toEqual({ limit: 2000, sourceId: "source-1" });
  });
});

function entry(sequence: number): ConsoleEntry {
  return {
    id: `line-${sequence}`,
    timestamp: new Date(sequence).toISOString(),
    sequence,
    stream: "stdout",
    text: `line ${sequence}`,
    sourceId: null,
    sourceLabel: null
  };
}
