import { useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityPlaybackControls } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityPlaybackControls";
import { useActivityPlayback } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useActivityPlayback";
import type { PlaybackPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/api/activityPlayback";
import { getTheme, themeTokenNames } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import "../../src/essentials/Elsa.Studio.Hosting/StudioUi/src/styles.css";

interface FixtureState {
  requests: { position: string; sequenceToken: string | null }[];
  hold: boolean; failOnce: boolean; denied: boolean;
  release(position: string): void;
}
declare global { interface Window { __activityPlaybackFixture: FixtureState } }
const parameters = new URLSearchParams(location.search), scenario = parameters.get("scenario");
const palette = getTheme("classic")![parameters.get("theme") === "dark" ? "dark" : "light"];
for (const token of themeTokenNames) document.documentElement.style.setProperty(`--${token.replace(/([A-Z])/g, "-$1").toLowerCase()}`, palette[token]);
document.documentElement.style.setProperty("--studio-font-ui", "system-ui, sans-serif");
document.body.style.cssText = "margin:0;background:var(--studio-surface);color:var(--studio-text);font-family:var(--studio-font-ui)";
const count = scenario === "empty" ? 0 : scenario === "one" ? 1 : 200;
const held = new Map<string, (() => void)[]>();
const controls: FixtureState = { requests: [], hold: false, failOnce: false, denied: false,
  release(position) { const pending = held.get(position) ?? []; held.delete(position); pending.forEach(finish => finish()); } };
window.__activityPlaybackFixture = controls;

function pageAt(position: number): PlaybackPage {
  const start = Math.floor(position / 50) * 50;
  const unavailable = scenario === "gap" || scenario === "unavailable" || scenario === "disabled" || scenario === "uncaptured" || scenario === "unknown-coverage" || scenario === "unknown-reason";
  return { state: unavailable ? "unavailable" : "available", reason: scenario === "unknown-reason" ? "__proto__" : scenario === "gap" ? "history-gap-unlocated" : unavailable ? "occurrence-recording-unavailable" : null,
    sequenceToken: unavailable ? null : "fixture-frozen-window", upperSequence: "210", retainedAfterSequence: "0",
    totalCount: unavailable || scenario === "unknown" ? null : String(count), selectedPosition: unavailable || !count ? null : String(position),
    readAt: "2026-11-14T06:00:00Z", hasNewerHistory: false,
    coverage: { state: scenario === "unknown-coverage" ? "__proto__" : scenario === "gap" ? "partial" : scenario === "disabled" || scenario === "uncaptured" ? scenario : "captured", supportedKinds: ["ActivityCompleted"], capturedFromStart: true, hasGaps: scenario === "gap",
      firstSequence: "1", lastSequence: "210", settledSequence: "210", purgedThroughSequence: "0" },
    items: unavailable ? [] : Array.from({ length: Math.max(0, Math.min(50, count - start)) }, (_, offset) => {
      const ordinal = start + offset;
      return { workflowExecutionId: "fixture-run", activityExecutionId: `fixture-execution-${ordinal}`, executableNodeId: `repeated-${ordinal % 3}`,
        historyEventId: `fixture-event-${ordinal}`, position: String(ordinal), sequence: String(ordinal + 1), observationKind: "ActivityCompleted",
        occurredAt: "2026-11-14T06:00:00Z", recordedAt: "2026-11-14T06:00:01Z",
        previousActivityExecutionId: ordinal ? `fixture-execution-${ordinal - 1}` : null,
        nextActivityExecutionId: ordinal < count - 1 ? `fixture-execution-${ordinal + 1}` : null };
    }) };
}
function Fixture() {
  const [selected, setSelected] = useState("");
  const [inspected, setInspected] = useState("");
  const context = useMemo(() => ({ baseUrl: "http://playback-fixture.invalid", http: {
    async getJson(path: string) {
      if (!path.includes("fixture-occurrences")) return { capabilities: [{ id: "elsa.api.runtime", contractVersion: "1",
        links: scenario === "unsupported" ? [] : [{ rel: "workflow-history-occurrences", href: "fixture-occurrences/{workflowExecutionId}" }] }] };
      const query = new URL(path, "http://playback-fixture.invalid").searchParams;
      const position = query.get("position") ?? query.get("anchorActivityExecutionId")?.split("-").at(-1) ?? "0";
      controls.requests.push({ position, sequenceToken: query.get("sequenceToken") });
      const resolve = () => {
        if (controls.denied) throw Object.assign(new Error("isolated current access refusal"), { status: 403 });
        if (controls.failOnce) { controls.failOnce = false; throw Object.assign(new Error("isolated transient failure"), { status: 503 }); }
        return pageAt(Number(position));
      };
      if (controls.hold) return new Promise((finish, reject) => {
        const pending = held.get(position) ?? []; pending.push(() => { try { finish(resolve()); } catch (error) { reject(error); } }); held.set(position, pending);
      });
      return resolve();
    }
  } }) as unknown as StudioEndpointContext, []);
  const playback = useActivityPlayback({ context, workflowExecutionId: "fixture-run", enabled: true,
    prepareSelection: async entry => ({ commit: () => setSelected(entry.activityExecutionId),
      limitation: scenario === "missing-pin" ? "Pinned diagram unavailable; recorded occurrence remains inspectable." : "" }) });
  return <main style={{ minWidth: 0, padding: scenario === "pointer" ? 0 : 12 }}><h1 style={{ fontSize: 18 }}>Isolated playback fixture</h1>
    <p>These responses test presentation behavior; they are not real runtime evidence.</p>
    <ActivityPlaybackControls playback={playback} scopeLabel="Entire fixture run" labelFor={entry => `Repeated activity ${Number(entry.position) % 3}`} />
    <output aria-label="Confirmed fixture occurrence">{selected || "None"}</output>
    {scenario === "pointer" ? <div onPointerDownCapture={() => playback.pause()}>
      <button type="button" onClick={() => setInspected(selected)}>Inspect occurrence below playback</button>
      <output aria-label="Inspected fixture occurrence">{inspected || "None"}</output>
    </div> : null}
  </main>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
