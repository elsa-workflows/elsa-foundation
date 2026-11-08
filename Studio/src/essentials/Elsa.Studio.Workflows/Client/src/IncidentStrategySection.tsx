import { useEffect, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError } from "./api/capabilities";
import { listIncidentStrategies } from "./api/publishing";
import { readIncidentStrategyReference } from "./workflowProperties";
import type {
  IncidentStrategiesResponse,
  IncidentStrategyDescriptor,
  IncidentStrategyReference
} from "./workflowTypes";

type IncidentStrategiesState =
  | { status: "loading" }
  | { status: "ready"; catalog: IncidentStrategiesResponse }
  | { status: "unavailable" }
  | { status: "error" };

function useIncidentStrategies(context: StudioEndpointContext) {
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<IncidentStrategiesState>({ status: "loading" });

  useEffect(() => {
    let cancelled = false;
    setState({ status: "loading" });
    listIncidentStrategies(context).then(
      catalog => {
        if (!cancelled) setState({ status: "ready", catalog });
      },
      error => {
        if (!cancelled) {
          setState({ status: error instanceof ApiCapabilityUnavailableError ? "unavailable" : "error" });
        }
      }
    );
    return () => {
      cancelled = true;
    };
  }, [attempt, context]);

  return { state, retry: () => setAttempt(current => current + 1) };
}

function strategyKey(reference: IncidentStrategyReference): string {
  return JSON.stringify([reference.alias, reference.version]);
}

function sameStrategy(left: IncidentStrategyReference, right: IncidentStrategyReference): boolean {
  return left.alias.toUpperCase() === right.alias.toUpperCase() && left.version === right.version;
}

function strategyLabel(descriptor: IncidentStrategyDescriptor): string {
  return `${descriptor.displayName} — ${descriptor.alias} / ${descriptor.version}`;
}

export function IncidentStrategySection({ strategyOptions, context, onChange }: {
  strategyOptions: unknown;
  context: StudioEndpointContext;
  onChange(reference: IncidentStrategyReference | null): void;
}) {
  const stored = readIncidentStrategyReference(strategyOptions);
  const { state, retry } = useIncidentStrategies(context);
  const storedLabel = stored ? `${stored.alias} / ${stored.version}` : "Use host default";

  if (state.status !== "ready") {
    return (
      <section className="wf-properties-section">
        <h3>Failure handling</h3>
        <dl className="wf-properties-info">
          <dt>Incident strategy</dt>
          <dd><output>{storedLabel}</output></dd>
        </dl>
        {state.status === "loading" ? (
          <p className="wf-properties-strategy-status wf-muted" role="status">Loading incident strategies… The stored selection is unchanged.</p>
        ) : state.status === "unavailable" ? (
          <p className="wf-properties-strategy-status wf-muted" role="status">Incident strategy discovery is not available from this host. The stored selection is unchanged.</p>
        ) : (
          <p className="wf-properties-strategy-status is-error" role="alert">
            Incident strategies could not be loaded. The stored selection is unchanged.{" "}
            <button type="button" onClick={retry}>Retry</button>
          </p>
        )}
      </section>
    );
  }

  const { items, defaultStrategy } = state.catalog;
  const resolvedStored = stored ? items.find(item => sameStrategy(item, stored)) : undefined;
  const selectedKey = resolvedStored
    ? strategyKey(resolvedStored)
    : stored
      ? strategyKey(stored)
      : "";
  const defaultDescriptor = items.find(item => sameStrategy(item, defaultStrategy));
  const selectedDescriptor = stored ? resolvedStored : defaultDescriptor;
  const defaultLabel = defaultDescriptor
    ? `Use host default — ${strategyLabel(defaultDescriptor)}`
    : `Use host default — ${defaultStrategy.alias} / ${defaultStrategy.version}`;

  return (
    <section className="wf-properties-section">
      <h3>Failure handling</h3>
      <div className="wf-properties-strategy-field">
        <label htmlFor="wf-incident-strategy">Incident strategy</label>
        <select
          id="wf-incident-strategy"
          aria-label="Incident strategy"
          aria-describedby="wf-incident-strategy-help"
          value={selectedKey}
          onChange={event => {
            if (!event.target.value) {
              onChange(null);
              return;
            }
            const selected = items.find(item => strategyKey(item) === event.target.value);
            if (selected) onChange({ alias: selected.alias, version: selected.version });
          }}
        >
          <option value="">{defaultLabel}</option>
          {stored && !resolvedStored ? (
            <option value={selectedKey} disabled>{storedLabel} (unresolved)</option>
          ) : null}
          {items.map(item => (
            <option key={strategyKey(item)} value={strategyKey(item)}>
              {strategyLabel(item)}
            </option>
          ))}
        </select>
        <p
          id="wf-incident-strategy-help"
          className="wf-properties-strategy-status wf-muted"
          role={items.length === 0 ? "status" : undefined}
        >
          {items.length === 0
            ? "No incident strategies are advertised by this host. The host-default selection remains available."
            : stored && !resolvedStored
              ? "This exact stored strategy is no longer advertised. It will be preserved until you choose another option."
              : selectedDescriptor?.description
                ?? (stored
                  ? `Pins ${stored.alias} / ${stored.version} when this workflow is published.`
                  : `Publishing currently resolves the host default to ${defaultStrategy.alias} / ${defaultStrategy.version}.`)}
        </p>
      </div>
    </section>
  );
}
