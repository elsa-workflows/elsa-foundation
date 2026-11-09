import { appendUnique } from "./paging";
import React, { useEffect, useId, useRef, useState } from "react";
import {
  type StudioActivityPropertyEditorProps,
  type StudioEndpointContext
} from "@elsa-workflows/studio-sdk";
import {
  classifyEtlApiError,
  getEtlApiCapability,
  getEtlConnectorVersion,
  invalidateEtlApiCapabilityCache,
  listEtlDefinitions,
  listEtlConnectorDefinitions,
  listEtlConnectorVersions,
  listEtlVersions,
  type EtlApiFailureKind
} from "./etlApi";
import type { EtlDefinitionSummaryView, EtlPageRequest, EtlPublishedVersionView } from "./etlTypes";

const pageSize = 50;

export interface EtlPublishedSelectionEditorProps extends StudioActivityPropertyEditorProps {
  /** Supplied by the ETL module contribution wrapper; property editor context has no backend transport. */
  endpointContext: StudioEndpointContext;
}

interface CurrentSelection {
  token: string;
  definitionId: string;
  version: number;
  contentHash: string;
}

type PipelineCatalog = "file" | "connector";

export function EtlPublishedSelectionEditor({
  value,
  disabled = false,
  context,
  onChange,
  endpointContext
}: EtlPublishedSelectionEditorProps) {
  const editorId = useId();
  const readOnly = disabled || context.readOnly === true;
  const currentToken = typeof value === "string" ? value : null;
  const current = currentToken ? parseSelection(currentToken) : null;
  const [catalog, setCatalog] = useState<PipelineCatalog>("file");
  const [definitionId, setDefinitionId] = useState(current?.definitionId ?? "");
  const [definitions, setDefinitions] = useState<EtlDefinitionSummaryView[]>([]);
  const [definitionsCursor, setDefinitionsCursor] = useState<string | null>(null);
  const [definitionsLoading, setDefinitionsLoading] = useState(true);
  const [definitionsMoreLoading, setDefinitionsMoreLoading] = useState(false);
  const [definitionsFailure, setDefinitionsFailure] = useState<EtlApiFailureKind | null>(null);
  const [definitionsMoreFailure, setDefinitionsMoreFailure] = useState<EtlApiFailureKind | null>(null);
  const [definitionsRetry, setDefinitionsRetry] = useState(0);
  const [versions, setVersions] = useState<EtlPublishedVersionView[]>([]);
  const [versionsCursor, setVersionsCursor] = useState<string | null>(null);
  const [versionsLoading, setVersionsLoading] = useState(false);
  const [versionsMoreLoading, setVersionsMoreLoading] = useState(false);
  const [versionsFailure, setVersionsFailure] = useState<EtlApiFailureKind | null>(null);
  const [versionsMoreFailure, setVersionsMoreFailure] = useState<EtlApiFailureKind | null>(null);
  const [versionsRetry, setVersionsRetry] = useState(0);
  const definitionsGeneration = useRef(0);
  const versionsGeneration = useRef(0);
  const definitionsMoreRequest = useRef<AbortController | null>(null);
  const versionsMoreRequest = useRef<AbortController | null>(null);
  const selectionProbeGeneration = useRef(0);
  const catalogChoiceGeneration = useRef(0);
  const manualCatalogChoice = useRef<{ token: string | null; catalog: PipelineCatalog } | null>(null);
  const autoDetectedConnector = useRef<{ token: string; endpointContext: StudioEndpointContext } | null>(null);

  useEffect(() => {
    const generation = ++selectionProbeGeneration.current;
    const controller = new AbortController();
    const selection = currentToken ? parseSelection(currentToken) : null;
    const manualChoice = manualCatalogChoice.current;
    if (manualChoice?.token === currentToken) {
      setCatalog(manualChoice.catalog);
      return () => controller.abort();
    }
    if (manualChoice) {
      manualCatalogChoice.current = null;
      autoDetectedConnector.current = null;
      setCatalog("file");
    }
    if (!selection) {
      revertStaleAutomaticChoice(autoDetectedConnector, currentToken, endpointContext, setCatalog);
      return () => controller.abort();
    }

    const choiceGeneration = catalogChoiceGeneration.current;
    void getEtlConnectorVersion(endpointContext, selection.definitionId, selection.version, controller.signal)
      .then(artifact => {
        if (controller.signal.aborted || generation !== selectionProbeGeneration.current || choiceGeneration !== catalogChoiceGeneration.current) return;
        const version = artifact.version;
        if (version.selection === selection.token &&
          version.definitionId === selection.definitionId &&
          version.definitionVersion === selection.version &&
          version.contentHash === selection.contentHash) {
          autoDetectedConnector.current = { token: selection.token, endpointContext };
          setCatalog("connector");
        } else {
          revertStaleAutomaticChoice(autoDetectedConnector, currentToken, endpointContext, setCatalog);
        }
      })
      .catch(() => {
        if (controller.signal.aborted || generation !== selectionProbeGeneration.current || choiceGeneration !== catalogChoiceGeneration.current) return;
        revertStaleAutomaticChoice(autoDetectedConnector, currentToken, endpointContext, setCatalog);
      });

    return () => {
      controller.abort();
    };
  }, [endpointContext, currentToken]);

  useEffect(() => {
    const generation = ++definitionsGeneration.current;
    const controller = new AbortController();
    definitionsMoreRequest.current?.abort();
    setDefinitions([]);
    setDefinitionsCursor(null);
    setDefinitionsLoading(true);
    setDefinitionsMoreLoading(false);
    setDefinitionsFailure(null);
    setDefinitionsMoreFailure(null);

    const area = catalog === "connector" ? "design-v2" : "design";
    void getEtlApiCapability(endpointContext, area)
      .then(() => {
        if (controller.signal.aborted) return null;
        return listDefinitions(endpointContext, catalog, { limit: pageSize }, controller.signal);
      })
      .then(page => {
        if (!page || controller.signal.aborted || generation !== definitionsGeneration.current) return;
        setDefinitions(page.items);
        setDefinitionsCursor(page.nextCursor);
        setDefinitionsLoading(false);
      })
      .catch(error => {
        if (controller.signal.aborted || generation !== definitionsGeneration.current) return;
        setDefinitionsFailure(classifyEtlApiError(error));
        setDefinitionsLoading(false);
      });

    return () => {
      controller.abort();
      definitionsMoreRequest.current?.abort();
      definitionsGeneration.current++;
    };
  }, [endpointContext, catalog, definitionsRetry]);

  useEffect(() => {
    const generation = ++versionsGeneration.current;
    const controller = new AbortController();
    versionsMoreRequest.current?.abort();
    setVersions([]);
    setVersionsCursor(null);
    setVersionsMoreLoading(false);
    setVersionsFailure(null);
    setVersionsMoreFailure(null);
    if (!definitionId) {
      setVersionsLoading(false);
      return () => {
        controller.abort();
        versionsGeneration.current++;
      };
    }

    setVersionsLoading(true);
    void listVersions(endpointContext, catalog, definitionId, { limit: pageSize }, controller.signal)
      .then(page => {
        if (controller.signal.aborted || generation !== versionsGeneration.current) return;
        setVersions(page.items);
        setVersionsCursor(page.nextCursor);
        setVersionsLoading(false);
      })
      .catch(error => {
        if (controller.signal.aborted || generation !== versionsGeneration.current) return;
        setVersionsFailure(classifyEtlApiError(error));
        setVersionsLoading(false);
      });

    return () => {
      controller.abort();
      versionsMoreRequest.current?.abort();
      versionsGeneration.current++;
    };
  }, [endpointContext, catalog, definitionId, versionsRetry]);

  useEffect(() => {
    setDefinitionId(current?.definitionId ?? "");
  }, [currentToken]);

  async function loadMoreDefinitions() {
    if (!definitionsCursor || definitionsMoreLoading || definitionsLoading || readOnly) return;
    const generation = definitionsGeneration.current;
    const controller = new AbortController();
    definitionsMoreRequest.current?.abort();
    definitionsMoreRequest.current = controller;
    setDefinitionsMoreLoading(true);
    setDefinitionsMoreFailure(null);
    try {
      const page = await listDefinitions(endpointContext, catalog, { limit: pageSize, cursor: definitionsCursor }, controller.signal);
      if (controller.signal.aborted || generation !== definitionsGeneration.current) return;
      setDefinitions(previous => appendUnique(previous, page.items, item => item.definitionId));
      setDefinitionsCursor(page.nextCursor);
    } catch (error) {
      if (controller.signal.aborted || generation !== definitionsGeneration.current) return;
      setDefinitionsMoreFailure(classifyEtlApiError(error));
    } finally {
      if (!controller.signal.aborted && generation === definitionsGeneration.current) setDefinitionsMoreLoading(false);
    }
  }

  async function loadMoreVersions() {
    if (!definitionId || !versionsCursor || versionsMoreLoading || versionsLoading || readOnly) return;
    const generation = versionsGeneration.current;
    const selectedDefinitionId = definitionId;
    const controller = new AbortController();
    versionsMoreRequest.current?.abort();
    versionsMoreRequest.current = controller;
    setVersionsMoreLoading(true);
    setVersionsMoreFailure(null);
    try {
      const page = await listVersions(endpointContext, catalog, selectedDefinitionId, { limit: pageSize, cursor: versionsCursor }, controller.signal);
      if (controller.signal.aborted || generation !== versionsGeneration.current) return;
      setVersions(previous => appendUnique(previous, page.items, item => item.selection));
      setVersionsCursor(page.nextCursor);
    } catch (error) {
      if (controller.signal.aborted || generation !== versionsGeneration.current) return;
      setVersionsMoreFailure(classifyEtlApiError(error));
    } finally {
      if (!controller.signal.aborted && generation === versionsGeneration.current) setVersionsMoreLoading(false);
    }
  }

  const savedDefinitionIsListed = current && definitions.some(item => item.definitionId === current.definitionId);
  const savedVersionIsListed = current && versions.some(item => item.selection === current.token);
  const savedSelectionValue = currentToken && current && definitionId === current.definitionId && !savedVersionIsListed
    ? currentToken
    : "";

  return (
    <section className="etl-selection-editor" aria-label="Published ETL selection">
      <div className="etl-editor-field">
        <label htmlFor={`${editorId}-catalog`}>Pipeline catalog</label>
        <select
          id={`${editorId}-catalog`}
          value={catalog}
          disabled={readOnly}
          onChange={event => {
            catalogChoiceGeneration.current++;
            const selectedCatalog = event.currentTarget.value as PipelineCatalog;
            autoDetectedConnector.current = null;
            manualCatalogChoice.current = { token: currentToken, catalog: selectedCatalog };
            setCatalog(selectedCatalog);
          }}
        >
          <option value="file">File pipelines</option>
          <option value="connector">Connector pipelines</option>
        </select>
      </div>

      <div className="etl-editor-field">
        <label htmlFor={`${editorId}-definition`}>Published ETL definition</label>
        <select
          id={`${editorId}-definition`}
          value={definitionId}
          disabled={readOnly || definitionsLoading || definitionsFailure !== null}
          onChange={event => {
            catalogChoiceGeneration.current++;
            autoDetectedConnector.current = null;
            manualCatalogChoice.current = { token: currentToken, catalog };
            setDefinitionId(event.currentTarget.value);
          }}
        >
          <option value="">Choose a published ETL definition</option>
          {current && !savedDefinitionIsListed ? (
            <option value={current.definitionId}>Saved definition: {current.definitionId}</option>
          ) : null}
          {definitions.map(item => (
            <option key={item.definitionId} value={item.definitionId}>
              {item.definitionId}{item.lastPublishedVersion > 0 ? ` · latest v${item.lastPublishedVersion}` : " · no published versions"}
            </option>
          ))}
        </select>
        {definitionsLoading ? <p role="status" aria-live="polite">Loading approved ETL definitions…</p> : null}
        {definitionsFailure ? <FailureState kind={definitionsFailure} subject="ETL definitions" onRetry={() => { invalidateEtlApiCapabilityCache(endpointContext); setDefinitionsRetry(value => value + 1); }} /> : null}
        {definitionsMoreFailure ? <FailureState kind={definitionsMoreFailure} subject="more definitions" onRetry={() => void loadMoreDefinitions()} /> : null}
        {!definitionsLoading && !definitionsFailure && definitions.length === 0 ? <p role="status">No ETL definitions are available yet.</p> : null}
        {definitionsCursor && !definitionsLoading && !definitionsFailure ? (
          <button type="button" disabled={readOnly || definitionsMoreLoading} onClick={() => void loadMoreDefinitions()}>
            {definitionsMoreLoading ? "Loading more definitions…" : "Load more definitions"}
          </button>
        ) : null}
      </div>

      <div className="etl-editor-field">
        <label htmlFor={`${editorId}-version`}>Published ETL version</label>
        <select
          id={`${editorId}-version`}
          value={savedSelectionValue || (savedVersionIsListed ? currentToken! : "")}
          disabled={readOnly || !definitionId || versionsLoading || versionsFailure !== null}
          onChange={event => {
            const selected = versions.find(version => version.selection === event.currentTarget.value);
            if (selected) {
              catalogChoiceGeneration.current++;
              autoDetectedConnector.current = null;
              manualCatalogChoice.current = { token: selected.selection, catalog };
              onChange(selected.selection);
            }
          }}
        >
          <option value="">Choose an immutable published version</option>
          {savedSelectionValue ? <option value={savedSelectionValue}>Saved selection · v{current?.version ?? "unknown"}</option> : null}
          {versions.map(item => (
            <option key={item.selection} value={item.selection}>
              v{item.definitionVersion} · {item.contentHash.slice(0, 12)}
            </option>
          ))}
        </select>
        {definitionId && versionsLoading ? <p role="status" aria-live="polite">Loading published versions…</p> : null}
        {versionsFailure ? <FailureState kind={versionsFailure} subject="published versions" onRetry={() => { invalidateEtlApiCapabilityCache(endpointContext); setVersionsRetry(value => value + 1); }} /> : null}
        {versionsMoreFailure ? <FailureState kind={versionsMoreFailure} subject="more versions" onRetry={() => void loadMoreVersions()} /> : null}
        {definitionId && !versionsLoading && !versionsFailure && versions.length === 0 ? <p role="status">This definition has no published versions.</p> : null}
        {versionsCursor && !versionsLoading && !versionsFailure ? (
          <button type="button" disabled={readOnly || versionsMoreLoading} onClick={() => void loadMoreVersions()}>
            {versionsMoreLoading ? "Loading more versions…" : "Load more versions"}
          </button>
        ) : null}
      </div>

      {currentToken && !current ? (
        <p role="status" aria-live="polite">The saved ETL selection is preserved. Choose a published version to replace it.</p>
      ) : current ? (
        <p className="etl-selection-current" role="status">Saved immutable selection · {current.definitionId} v{current.version}</p>
      ) : null}
    </section>
  );
}

function FailureState({ kind, subject, onRetry }: { kind: EtlApiFailureKind; subject: string; onRetry(): void }) {
  const message = kind === "forbidden"
    ? `You do not have permission to read ${subject}.`
    : kind === "unavailable"
      ? `The ETL API is unavailable for ${subject}.`
      : kind === "not-found"
        ? `The requested ${subject} is no longer available.`
        : `Could not load ${subject}.`;
  return (
    <div role="alert">
      <p>{message}</p>
      <button type="button" onClick={onRetry}>Retry</button>
    </div>
  );
}



function parseSelection(token: string): CurrentSelection | null {
  const match = /^etl1:([A-Za-z0-9][A-Za-z0-9._-]{0,255}):([1-9][0-9]*):([0-9a-f]{64})$/.exec(token);
  if (!match) return null;
  const version = Number(match[2]);
  if (!Number.isSafeInteger(version) || version <= 0) return null;
  return { token, definitionId: match[1], version, contentHash: match[3] };
}

function listDefinitions(context: StudioEndpointContext, catalog: PipelineCatalog, request: EtlPageRequest, signal: AbortSignal) {
  return catalog === "connector"
    ? listEtlConnectorDefinitions(context, request, signal)
    : listEtlDefinitions(context, request, signal);
}

function listVersions(context: StudioEndpointContext, catalog: PipelineCatalog, definitionId: string, request: EtlPageRequest, signal: AbortSignal) {
  return catalog === "connector"
    ? listEtlConnectorVersions(context, definitionId, request, signal)
    : listEtlVersions(context, definitionId, request, signal);
}

function revertStaleAutomaticChoice(
  choice: { current: { token: string; endpointContext: StudioEndpointContext } | null },
  currentToken: string | null,
  endpointContext: StudioEndpointContext,
  setCatalog: (catalog: PipelineCatalog) => void
) {
  const previous = choice.current;
  if (!previous || previous.token === currentToken && previous.endpointContext === endpointContext) return;
  choice.current = null;
  setCatalog("file");
}
