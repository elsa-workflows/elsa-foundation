import { useEffect, useMemo, useRef, useState } from "react";
import { Check, Layers, Workflow } from "lucide-react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { StatusChip, StudioAlert, StudioButton, StudioField, StudioSearchInput } from "@elsa-workflows/studio-ui";
import { listDefinitions } from "../api/workflowDesign";
import type { WorkflowDefinitionSummary } from "../workflowTypes";
import type { TriggerClient } from "./api";
import { Loading, RequestError, label, useTriggerLoad, useTriggerScope } from "./shared";
import { targetStateTone } from "./triggerTypePresentation";
import type { TargetSelector, TargetView, TriggerView } from "./types";
import { WorkflowIdentity } from "./WorkflowIdentity";

const designPageSize = 20;

type DiscoveryMode = "all" | "runtime";

function SlotSelector({
  value,
  knownSlots,
  disabled,
  onChange
}: {
  value: TargetSelector;
  knownSlots: readonly TargetView[];
  disabled?: boolean;
  onChange(slotName: string): void;
}) {
  const pills = useMemo(() => {
    const map = new Map<string, TargetView | undefined>();
    map.set("default", undefined);
    for (const slot of knownSlots) {
      map.set(slot.slotName, slot);
    }
    if (value.slotName && !map.has(value.slotName)) {
      map.set(value.slotName, undefined);
    }
    return [...map.entries()].map(([slotName, target]) => ({ slotName, target }));
  }, [knownSlots, value.slotName]);

  return (
    <div className="wf-trigger-slot-selector">
      {pills.length > 0 ? (
        <div className="wf-trigger-slot-pills" role="group" aria-label="Known publication slots">
          {pills.map(({ slotName, target }) => {
            const active = value.slotName === slotName;
            return (
              <button
                key={slotName}
                type="button"
                className="wf-trigger-slot-pill"
                data-selected={active || undefined}
                disabled={disabled}
                onClick={() => onChange(slotName)}
              >
                <Layers size={13} aria-hidden />
                <span>{slotName}</span>
                {target?.serving?.version ? (
                  <span className="wf-trigger-slot-pill-meta">v{target.serving.version}</span>
                ) : target ? (
                  <span className="wf-trigger-slot-pill-meta">{label(target.state)}</span>
                ) : null}
              </button>
            );
          })}
        </div>
      ) : null}
      <StudioField
        label="Publication slot"
        description="This association follows the slot's serving version. A workflow without a serving publication can be saved disabled."
      >
        <input
          aria-label="Publication slot"
          value={value.slotName}
          disabled={disabled}
          onChange={event => onChange(event.currentTarget.value)}
        />
      </StudioField>
    </div>
  );
}

export function WorkflowTargetPicker({
  api,
  client,
  trigger,
  value,
  fixedDefinitionId,
  disabled,
  onChange
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  trigger?: TriggerView;
  value: TargetSelector;
  fixedDefinitionId?: string;
  disabled?: boolean;
  onChange(target: TargetSelector): void;
}) {
  const scope = useTriggerScope(api);
  const [mode, setMode] = useState<DiscoveryMode>("all");
  const [search, setSearch] = useState("");
  const [runtimeSearch, setRuntimeSearch] = useState("");
  const [runtimeAfter, setRuntimeAfter] = useState<string>();
  const [runtimeSubmitCount, setRuntimeSubmitCount] = useState(0);

  // Design workflow browsing & search state.
  const [designState, setDesignState] = useState<"loading" | "ready" | "failed">("loading");
  const [designError, setDesignError] = useState("");
  const [designItems, setDesignItems] = useState<WorkflowDefinitionSummary[]>([]);
  const [designNextContinuation, setDesignNextContinuation] = useState<string | null>(null);
  const [designNextPage, setDesignNextPage] = useState<number | null>(null);
  const [designTotalCount, setDesignTotalCount] = useState<number | null>(null);
  const [designRetry, setDesignRetry] = useState(0);
  const designGeneration = useRef(0);

  // Runtime published slots (loaded upfront to enrich Design cards AND to serve Runtime-only / denied fallback).
  const effectiveMode: DiscoveryMode = designState === "failed" ? "runtime" : mode;
  const runtimeQueryTerm = effectiveMode === "runtime" ? runtimeSearch : "";
  const runtimeQueryAfter = effectiveMode === "runtime" ? runtimeAfter : undefined;
  const runtimeKey = JSON.stringify([scope, fixedDefinitionId ?? "", runtimeQueryTerm, runtimeQueryAfter ?? "", runtimeSubmitCount]);
  const { result: runtimeResult, reload: reloadRuntime } = useTriggerLoad(
    signal => fixedDefinitionId || typeof client.targets !== "function"
      ? Promise.resolve({ items: [] as TargetView[], next: undefined })
      : client.targets({ search: runtimeQueryTerm, after: runtimeQueryAfter }, signal),
    runtimeKey
  );

  // Also load any known Runtime slots for a fixed workflow definition so slot pills are populated.
  const { result: fixedRuntimeResult } = useTriggerLoad(
    signal => fixedDefinitionId && typeof client.targets === "function"
      ? client.targets({ search: fixedDefinitionId }, signal).catch(() => ({ items: [] as TargetView[], next: undefined }))
      : Promise.resolve({ items: [] as TargetView[], next: undefined }),
    JSON.stringify([scope, "fixed", fixedDefinitionId ?? ""])
  );

  useEffect(() => {
    if (fixedDefinitionId) return;
    const controller = new AbortController();
    const generation = ++designGeneration.current;
    setDesignState("loading");
    setDesignError("");
    setDesignNextContinuation(null);
    setDesignNextPage(null);
    setDesignTotalCount(null);

    void listDefinitions(
      api.backend,
      {
        search: search.trim(),
        state: "all",
        page: 1,
        pageSize: designPageSize
      },
      controller.signal
    ).then(
      response => {
        if (controller.signal.aborted || designGeneration.current !== generation) return;
        setDesignItems(response.definitions);
        setDesignNextContinuation(response.nextContinuationToken ?? null);
        setDesignNextPage(response.isPaged ? null : 2);
        setDesignTotalCount(response.totalCount ?? null);
        setDesignState("ready");
      },
      error => {
        if (controller.signal.aborted || designGeneration.current !== generation) return;
        setDesignError(error instanceof Error ? error.message : String(error));
        setDesignItems([]);
        setDesignState("failed");
      }
    );

    return () => {
      controller.abort();
    };
  }, [api.backend, fixedDefinitionId, scope, search, designRetry]);

  async function loadMoreDesign() {
    if (designState === "loading") return;
    const continuationToken = designNextContinuation ?? undefined;
    if (!continuationToken && designNextPage === null) return;
    const generation = ++designGeneration.current;
    setDesignState("loading");
    try {
      const page = designNextPage ?? 2;
      const response = await listDefinitions(api.backend, {
        search: search.trim(),
        state: "all",
        page,
        pageSize: designPageSize,
        continuationToken
      });
      if (designGeneration.current !== generation) return;
      setDesignItems(current => [...current, ...response.definitions]);
      setDesignNextContinuation(response.nextContinuationToken ?? null);
      setDesignNextPage(response.isPaged ? null : page + 1);
      setDesignTotalCount(response.totalCount ?? designTotalCount);
      setDesignState("ready");
    } catch (error) {
      if (designGeneration.current !== generation) return;
      setDesignError(error instanceof Error ? error.message : String(error));
      setDesignState("failed");
    }
  }

  const runtimeItems = useMemo(
    () => (runtimeResult.state === "loaded" ? runtimeResult.value.items : []),
    [runtimeResult]
  );
  const runtimeHasNext = runtimeResult.state === "loaded" && Boolean(runtimeResult.value.next);

  const slotsByDefinition = useMemo(() => {
    const map = new Map<string, TargetView[]>();
    const source = fixedDefinitionId
      ? (fixedRuntimeResult.state === "loaded" ? fixedRuntimeResult.value.items : [])
      : runtimeItems;
    for (const item of source) {
      const list = map.get(item.definitionId);
      if (list) list.push(item);
      else map.set(item.definitionId, [item]);
    }
    return map;
  }, [fixedDefinitionId, fixedRuntimeResult, runtimeItems]);

  const selectedKnownSlots = useMemo(
    () => (value.definitionId ? slotsByDefinition.get(value.definitionId) ?? [] : []),
    [slotsByDefinition, value.definitionId]
  );

  const canLoadMoreDesign = Boolean(designNextContinuation)
    || (designNextPage !== null && designTotalCount !== null && designItems.length < designTotalCount);

  if (fixedDefinitionId) {
    return (
      <section className="wf-trigger-target-section" aria-label="Choose subscription target">
        <div className="wf-trigger-selected-target">
          <div className="wf-trigger-selected-target-header">
            <span className="wf-trigger-picker-card-icon" aria-hidden="true">
              <Workflow size={16} />
            </span>
            <div className="wf-trigger-selected-target-body">
              <WorkflowIdentity api={api} scope={scope} definitionId={fixedDefinitionId} />
            </div>
          </div>
          <SlotSelector
            value={{ definitionId: fixedDefinitionId, slotName: value.slotName }}
            knownSlots={selectedKnownSlots}
            disabled={disabled}
            onChange={slotName => onChange({ definitionId: fixedDefinitionId, slotName })}
          />
        </div>
      </section>
    );
  }

  const requiresHttpResponse = trigger?.http?.mode === "WorkflowResponse";

  function selectDefinition(definitionId: string) {
    const slots = slotsByDefinition.get(definitionId) ?? [];
    const preferredSlot = slots.find(slot => slot.slotName === value.slotName)?.slotName
      ?? slots.find(slot => slot.slotName === "default")?.slotName
      ?? slots[0]?.slotName
      ?? (value.slotName || "default");
    onChange({ definitionId, slotName: preferredSlot });
  }

  return (
    <section className="wf-trigger-target-section" aria-label="Choose subscription target">
      {designState === "failed" ? (
        <StudioAlert tone="warning">
          Workflow Design search is unavailable ({designError}). Showing published Runtime slots instead.
          {" "}
          <StudioButton type="button" size="sm" onClick={() => setDesignRetry(count => count + 1)}>
            Retry workflow search
          </StudioButton>
        </StudioAlert>
      ) : null}

      <div className="wf-trigger-picker-toolbar">
        <div className="wf-trigger-picker-search">
          <StudioSearchInput
            aria-label={effectiveMode === "runtime" ? "Search subscription targets" : "Search workflows"}
            placeholder={effectiveMode === "runtime" ? "Filter published workflows by ID…" : "Search workflows by name…"}
            value={search}
            disabled={disabled}
            onChange={event => {
              const next = event.currentTarget.value;
              setSearch(next);
              if (effectiveMode === "runtime") {
                setRuntimeAfter(undefined);
                setRuntimeSearch(next.trim());
              }
            }}
          />
          {effectiveMode === "runtime" ? (
            <StudioButton
              type="button"
              disabled={disabled}
              onClick={() => {
                setRuntimeAfter(undefined);
                setRuntimeSearch(search.trim());
                setRuntimeSubmitCount(count => count + 1);
              }}
            >
              Search workflows
            </StudioButton>
          ) : null}
        </div>
        {designState !== "failed" ? (
          <div className="wf-trigger-source-switch" role="group" aria-label="Workflow discovery source">
            <button
              type="button"
              className="wf-trigger-source-pill"
              aria-pressed={mode === "all"}
              disabled={disabled}
              onClick={() => setMode("all")}
            >
              All workflows
            </button>
            <button
              type="button"
              className="wf-trigger-source-pill"
              aria-pressed={mode === "runtime"}
              disabled={disabled}
              onClick={() => {
                setMode("runtime");
                setRuntimeAfter(undefined);
                setRuntimeSearch(search.trim());
              }}
            >
              Published slots
            </button>
          </div>
        ) : null}
      </div>

      {effectiveMode === "all" ? (
        <div className="wf-trigger-picker-results-wrap">
          {designState === "loading" && designItems.length === 0 ? (
            <Loading label="Loading workflows" />
          ) : designItems.length === 0 ? (
            <div className="wf-trigger-picker-empty" role="status">
              <p>{search.trim() ? "No workflows match this search." : "No accessible workflows found."}</p>
            </div>
          ) : (
            <div
              className="wf-trigger-picker-list"
              role="listbox"
              aria-label="Workflow search results"
              aria-busy={designState === "loading"}
            >
              {designItems.map(definition => {
                const selected = value.definitionId === definition.id;
                const slots = slotsByDefinition.get(definition.id) ?? [];
                const servingSlot = slots.find(slot => slot.state === "Available") ?? slots[0];
                const title = definition.name?.trim() || definition.id;
                const incompatibleHttp = requiresHttpResponse && servingSlot?.supportsWorkflowOwnedHttpResponse === false;
                return (
                  <button
                    key={definition.id}
                    type="button"
                    role="option"
                    aria-selected={selected}
                    className="wf-trigger-picker-card"
                    data-selected={selected || undefined}
                    data-definition-id={definition.id}
                    disabled={disabled}
                    onClick={() => selectDefinition(definition.id)}
                  >
                    <span className="wf-trigger-picker-card-icon" aria-hidden="true">
                      <Workflow size={16} />
                    </span>
                    <span className="wf-trigger-picker-card-body">
                      <span className="wf-trigger-picker-card-title">{title}</span>
                      <span className="wf-trigger-picker-card-meta">
                        <span className="wf-trigger-mono">{definition.id}</span>
                        {definition.latestVersion ? <span>· v{definition.latestVersion}</span> : null}
                        {slots.length > 1 ? <span>· {slots.length} slots</span> : null}
                      </span>
                    </span>
                    <span className="wf-trigger-picker-card-badges">
                      {servingSlot ? (
                        <StatusChip tone={targetStateTone(servingSlot.state)}>{label(servingSlot.state)}</StatusChip>
                      ) : runtimeResult.state === "loaded" && !runtimeHasNext ? (
                        <StatusChip tone="neutral">Unpublished</StatusChip>
                      ) : null}
                      {incompatibleHttp ? <StatusChip tone="warning">No HTTP response</StatusChip> : null}
                      {selected ? (
                        <span className="wf-trigger-picker-check" aria-hidden="true">
                          <Check size={15} />
                        </span>
                      ) : null}
                    </span>
                  </button>
                );
              })}
            </div>
          )}
          {canLoadMoreDesign ? (
            <div className="wf-trigger-picker-pagination">
              <StudioButton
                type="button"
                size="sm"
                aria-label="Next workflow definitions"
                disabled={designState === "loading" || disabled}
                onClick={() => void loadMoreDesign()}
              >
                {designState === "loading" ? "Loading…" : "Load more workflows"}
              </StudioButton>
            </div>
          ) : null}
        </div>
      ) : (
        <div className="wf-trigger-picker-results-wrap">
          {runtimeResult.state === "loading" ? (
            <Loading label="Loading subscription targets" />
          ) : runtimeResult.state === "error" ? (
            <RequestError error={runtimeResult.error} title="Could not load workflow targets" retry={reloadRuntime} />
          ) : runtimeItems.length === 0 ? (
            <div className="wf-trigger-picker-empty" role="status">
              <p>No accessible workflow slots match this search.</p>
            </div>
          ) : (
            <div
              className="wf-trigger-picker-list"
              role="listbox"
              aria-label="Published workflow slots"
            >
              {runtimeItems.map(item => {
                const selected = value.definitionId === item.definitionId && value.slotName === item.slotName;
                const title = item.displayName || item.definitionId;
                const incompatibleHttp = requiresHttpResponse && item.supportsWorkflowOwnedHttpResponse === false;
                return (
                  <button
                    key={`${item.definitionId}:${item.slotName}`}
                    type="button"
                    role="option"
                    aria-selected={selected}
                    className="wf-trigger-picker-card"
                    data-selected={selected || undefined}
                    data-definition-id={item.definitionId}
                    data-slot-name={item.slotName}
                    disabled={disabled}
                    onClick={() => onChange({ definitionId: item.definitionId, slotName: item.slotName })}
                  >
                    <span className="wf-trigger-picker-card-icon" aria-hidden="true">
                      <Workflow size={16} />
                    </span>
                    <span className="wf-trigger-picker-card-body">
                      <span className="wf-trigger-picker-card-title">{title}</span>
                      <span className="wf-trigger-picker-card-meta">
                        <span className="wf-trigger-mono">{item.definitionId}</span>
                        <span>· Slot: {item.slotName}</span>
                        {item.serving?.version ? <span>· v{item.serving.version}</span> : null}
                      </span>
                    </span>
                    <span className="wf-trigger-picker-card-badges">
                      <StatusChip tone={targetStateTone(item.state)}>{label(item.state)}</StatusChip>
                      {incompatibleHttp ? <StatusChip tone="warning">No HTTP response</StatusChip> : null}
                      {selected ? (
                        <span className="wf-trigger-picker-check" aria-hidden="true">
                          <Check size={15} />
                        </span>
                      ) : null}
                    </span>
                  </button>
                );
              })}
            </div>
          )}
          {runtimeResult.state === "loaded" && (runtimeResult.value.next || runtimeAfter) ? (
            <div className="wf-trigger-picker-pagination">
              {runtimeAfter ? (
                <StudioButton type="button" size="sm" onClick={() => setRuntimeAfter(undefined)}>
                  First workflow targets
                </StudioButton>
              ) : null}
              {runtimeResult.value.next ? (
                <StudioButton type="button" size="sm" onClick={() => setRuntimeAfter(runtimeResult.value.next ?? undefined)}>
                  Next workflow targets
                </StudioButton>
              ) : null}
            </div>
          ) : null}
        </div>
      )}

      {value.definitionId ? (
        <div className="wf-trigger-selected-target">
          <div className="wf-trigger-selected-target-header">
            <span className="wf-trigger-picker-card-icon" aria-hidden="true">
              <Workflow size={16} />
            </span>
            <div className="wf-trigger-selected-target-body">
              <WorkflowIdentity api={api} scope={scope} definitionId={value.definitionId} />
            </div>
            {!disabled ? (
              <StudioButton
                type="button"
                size="sm"
                aria-label="Clear workflow selection"
                onClick={() => onChange({ definitionId: "", slotName: "default" })}
              >
                Clear
              </StudioButton>
            ) : null}
          </div>
          <SlotSelector
            value={value}
            knownSlots={selectedKnownSlots}
            disabled={disabled}
            onChange={slotName => onChange({ ...value, slotName })}
          />
        </div>
      ) : null}
    </section>
  );
}
