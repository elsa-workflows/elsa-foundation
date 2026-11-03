import type { WorkflowEditorDocumentAdapter } from "./WorkflowEditorDocumentAdapter";
import React, { useCallback, useEffect, useRef } from "react";
import { studioNavigationRequestedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDefinitionDetails } from "../workflowTypes";
import { updateDefinitionMetadata } from "../api/workflowDesign";

interface DefinitionMetadataParams {
  context: StudioEndpointContext;
  documentAdapter?: WorkflowEditorDocumentAdapter;
  details: WorkflowDefinitionDetails | null;
  setDetails: React.Dispatch<React.SetStateAction<WorkflowDefinitionDetails | null>>;
  setStatus(value: string): void;
}

// Name/description live on the definition, not on draft state, so they persist through a dedicated
// (debounced) metadata call rather than the draft autosave. The edit is reflected optimistically; a
// failed save keeps the typed value and surfaces a status (the backend endpoint may not be live yet).
export function useDefinitionMetadata({ context, documentAdapter, details, setDetails, setStatus }: DefinitionMetadataParams) {
  const detailsRef = useRef<WorkflowDefinitionDetails | null>(null);
  const metaSaveTimeoutRef = useRef<number | null>(null);
  const pendingMetaRef = useRef<{ name?: string; description?: string }>({});
  useEffect(() => { detailsRef.current = details; }, [details]);

  // Sends any pending name/description edit now (used by the debounce timer AND on unmount, so an edit
  // isn't lost when the panel closes within the debounce window). Both fields are always sent — merging
  // the pending patch over the current definition — so a name-only edit can't drop the description
  // regardless of whether the backend does a partial or full-replace update.
  const inFlightRef = useRef<Promise<boolean> | null>(null);
  useEffect(() => {
    const preventNavigation = (event: Event) => {
      const pending = pendingMetaRef.current;
      if (inFlightRef.current || pending.name !== undefined || pending.description !== undefined) event.preventDefault();
    };
    window.addEventListener(studioNavigationRequestedEvent, preventNavigation);
    window.addEventListener("beforeunload", preventNavigation);
    return () => {
      window.removeEventListener(studioNavigationRequestedEvent, preventNavigation);
      window.removeEventListener("beforeunload", preventNavigation);
    };
  }, []);
  const flushDefinitionMeta = useCallback(async (): Promise<boolean> => {
    if (metaSaveTimeoutRef.current !== null) {
      window.clearTimeout(metaSaveTimeoutRef.current);
      metaSaveTimeoutRef.current = null;
    }
    if (inFlightRef.current && !await inFlightRef.current) return false;
    const body = pendingMetaRef.current;
    const definition = detailsRef.current?.definition;
    if (!definition || (body.name === undefined && body.description === undefined)) return true;
    const request = (documentAdapter?.metadata ?? updateDefinitionMetadata)(context, definition.id, {
      name: body.name ?? definition.name,
      description: body.description ?? definition.description ?? null
    }).then(saved => {
      if (pendingMetaRef.current === body) pendingMetaRef.current = {};
      setDetails(prev => prev && prev.definition.id === saved.definition.id
        ? { ...prev, definition: { ...saved.definition, ...pendingMetaRef.current } } : prev);
      return true;
    }).catch(() => { setStatus("Couldn't save name/description."); return false; });
    inFlightRef.current = request;
    const saved = await request;
    if (inFlightRef.current === request) inFlightRef.current = null;
    return saved;
  }, [context, documentAdapter, setDetails, setStatus]);

  const updateDefinitionMeta = useCallback((patch: { name?: string; description?: string }) => {
    setDetails(current => current ? { ...current, definition: { ...current.definition, ...patch } } : current);
    pendingMetaRef.current = { ...pendingMetaRef.current, ...patch };
    if (metaSaveTimeoutRef.current !== null) window.clearTimeout(metaSaveTimeoutRef.current);
    metaSaveTimeoutRef.current = window.setTimeout(() => { void flushDefinitionMeta(); }, 800);
  }, [flushDefinitionMeta, setDetails]);

  // Flush a pending name/description save on unmount (or when the target workflow changes) so a debounced
  // edit isn't discarded when the properties panel closes within the debounce window.
  useEffect(() => () => { void flushDefinitionMeta(); }, [flushDefinitionMeta]);

  return { updateDefinitionMeta, flushDefinitionMeta };
}
