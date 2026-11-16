import { StudioField } from "@elsa-workflows/studio-ui";
import { useEffect, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getHostPersistenceMode } from "./api/persistenceMode";
import { isPersistenceMode, persistenceModes, readStoredPersistenceMode } from "./workflowProperties";
import type { PersistenceMode } from "./workflowTypes";

const descriptions: Record<PersistenceMode, string> = {
  Durable: "Commits after every activity. After a crash the run resumes at the last completed activity.",
  Balanced: "Commits on suspend, fault, completion, before an activity that is not safe to repeat, at the end of each pass and every N activities. After a crash, the activities since the last commit run again.",
  Ephemeral: "Commits on suspend, fault, cancellation, an incident or an alteration, on dispatching a child workflow, before an activity that is not safe to repeat, and when a pass ends without completing the run, plus a completion record unless the host turns it off. A run that completes in one pass with none of those earlier commits leaves only its completion record, or no trace."
};

const hostDefaultDescription = "Runs use the host's default mode.";
const unrecognizedDescription = "This stored mode is not one this Studio recognizes, and publishing will reject it. It is kept until you choose another option.";

export function PersistenceModeSection({ strategyOptions, context, onChange }: {
  strategyOptions: unknown;
  context: StudioEndpointContext;
  onChange(mode: PersistenceMode | null): void;
}) {
  const [defaultState, setDefaultState] = useState<{ context: StudioEndpointContext; mode: string } | null>(null);
  useEffect(() => {
    let cancelled = false;
    void getHostPersistenceMode(context).then(mode => {
      if (!cancelled) setDefaultState({ context, mode });
    }, () => {
      if (!cancelled) setDefaultState(null);
    });
    return () => { cancelled = true; };
  }, [context]);
  const hostDefault = defaultState?.context === context ? defaultState.mode : null;
  const stored = readStoredPersistenceMode(strategyOptions);
  const recognized = isPersistenceMode(stored) ? stored : null;
  const unrecognized = stored !== null && recognized === null;

  return (
    <section className="wf-properties-section">
      <h3>Persistence</h3>
      <div className="wf-properties-strategy-field"><StudioField controlId="wf-persistence-mode" label="Persistence mode">

        <select
          id="wf-persistence-mode"
          aria-label="Persistence mode"
          aria-describedby="wf-persistence-mode-help"
          value={stored ?? ""}
          onChange={event => {
            const selected = event.target.value;
            if (!selected) {
              onChange(null);
              return;
            }
            if (isPersistenceMode(selected)) onChange(selected);
          }}
        >
          <option value="">{hostDefault ? `Use host default (${hostDefault})` : "Use host default"}</option>
          {unrecognized ? <option value={stored} disabled>{stored} (unrecognized)</option> : null}
          {persistenceModes.map(mode => (
            <option key={mode} value={mode}>{mode}</option>
          ))}
        </select>
        <p id="wf-persistence-mode-help" className="wf-properties-strategy-status wf-muted">
          {unrecognized ? unrecognizedDescription : recognized ? descriptions[recognized] : hostDefault && isPersistenceMode(hostDefault) ? descriptions[hostDefault] : hostDefaultDescription}
          {" "}A change applies to runs of the next published version.
        </p>
      </StudioField></div>
    </section>
  );
}
