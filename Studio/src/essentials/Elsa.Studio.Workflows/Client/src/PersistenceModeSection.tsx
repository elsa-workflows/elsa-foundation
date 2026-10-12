import { isPersistenceMode, persistenceModes, readStoredPersistenceMode } from "./workflowProperties";
import type { PersistenceMode } from "./workflowTypes";

const descriptions: Record<PersistenceMode, string> = {
  Durable: "Commits after every activity. After a crash the run resumes at the last completed activity.",
  Balanced: "Commits on suspend, fault, completion, before an activity that is not safe to repeat, at the end of each pass and every N activities. After a crash, the activities since the last commit run again.",
  Ephemeral: "Commits on suspend or fault, before an activity that is not safe to repeat, and when a pass ends without completing the run, plus a completion record unless the host turns it off. A run of activities that are all safe to repeat, completed in one pass, leaves only its completion record, or no trace."
};

const hostDefaultDescription = "Runs use the host's default mode.";
const unrecognizedDescription = "This stored mode is not one this Studio recognizes, and publishing will reject it. It is kept until you choose another option.";

export function PersistenceModeSection({ strategyOptions, onChange }: {
  strategyOptions: unknown;
  onChange(mode: PersistenceMode | null): void;
}) {
  const stored = readStoredPersistenceMode(strategyOptions);
  const recognized = isPersistenceMode(stored) ? stored : null;
  const unrecognized = stored !== null && recognized === null;

  return (
    <section className="wf-properties-section">
      <h3>Persistence</h3>
      <div className="wf-properties-strategy-field">
        <label htmlFor="wf-persistence-mode">Persistence mode</label>
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
          <option value="">Use host default</option>
          {unrecognized ? <option value={stored} disabled>{stored} (unrecognized)</option> : null}
          {persistenceModes.map(mode => (
            <option key={mode} value={mode}>{mode}</option>
          ))}
        </select>
        <p id="wf-persistence-mode-help" className="wf-properties-strategy-status wf-muted">
          {unrecognized ? unrecognizedDescription : recognized ? descriptions[recognized] : hostDefaultDescription}
          {" "}A change applies to runs of the next published version.
        </p>
      </div>
    </section>
  );
}
