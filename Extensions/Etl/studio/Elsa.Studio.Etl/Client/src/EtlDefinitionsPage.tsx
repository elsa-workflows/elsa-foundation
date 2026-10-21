import React, { useEffect, useId, useRef, useState } from "react";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  classifyEtlApiError,
  createEtlDefinition,
  EtlApiError,
  getEtlDefinition,
  getEtlVersion,
  invalidateEtlApiCapabilityCache,
  listEtlBindings,
  listEtlDefinitions,
  listEtlVersions,
  publishEtlDefinition,
  updateEtlDraft,
  validateEtlDraft,
  type EtlApiFailureKind
} from "./etlApi";
import { EtlPipelineEditor } from "./components/EtlPipelineEditor";
import { createDefaultEtlPipeline, isValidEtlDefinitionId, sameEtlPipeline } from "./authoringHelpers";
import { appendUnique } from "./paging";
import type {
  EtlBindingSummary,
  EtlDraftView,
  EtlDefinitionSummaryView,
  EtlPipeline,
  EtlPublishedArtifactView,
  EtlPublishedVersionView,
  EtlValidationView
} from "./etlTypes";

const pageSize = 50;

type MutationKind = "creating" | "loading-draft" | "saving" | "validating" | "publishing";
type RequestFailure = { error: unknown; kind: EtlApiFailureKind };

/** Authoring UI for finite ETL drafts and their immutable published artifacts. */
export function EtlDefinitionsPage({ context, dialogs }: { context: StudioEndpointContext; dialogs: StudioDialogApi }) {
  const pageId = useId();
  const currentContext = useRef(context);
  currentContext.current = context;
  const scopeGeneration = useRef(0);
  const operation = useRef<object | null>(null);
  const mutationController = useRef<AbortController | null>(null);
  const selectedDefinition = useRef<string | null>(null);
  const definitionsController = useRef<AbortController | null>(null);
  const definitionsMoreController = useRef<AbortController | null>(null);
  const bindingsController = useRef<AbortController | null>(null);
  const bindingsMoreController = useRef<AbortController | null>(null);
  const versionsController = useRef<AbortController | null>(null);
  const versionsMoreController = useRef<AbortController | null>(null);
  const artifactController = useRef<AbortController | null>(null);
  const definitionsGeneration = useRef(0);
  const bindingsGeneration = useRef(0);
  const versionsGeneration = useRef(0);
  const artifactGeneration = useRef(0);

  const [loadedContext, setLoadedContext] = useState<StudioEndpointContext | null>(null);
  const [definitions, setDefinitions] = useState<EtlDefinitionSummaryView[]>([]);
  const [definitionsCursor, setDefinitionsCursor] = useState<string | null>(null);
  const [definitionsLoading, setDefinitionsLoading] = useState(true);
  const [definitionsMoreLoading, setDefinitionsMoreLoading] = useState(false);
  const [definitionsFailure, setDefinitionsFailure] = useState<RequestFailure | null>(null);
  const [definitionsMoreFailure, setDefinitionsMoreFailure] = useState<RequestFailure | null>(null);
  const [bindings, setBindings] = useState<EtlBindingSummary[]>([]);
  const [bindingsCursor, setBindingsCursor] = useState<string | null>(null);
  const [bindingsLoading, setBindingsLoading] = useState(true);
  const [bindingsMoreLoading, setBindingsMoreLoading] = useState(false);
  const [bindingsFailure, setBindingsFailure] = useState<RequestFailure | null>(null);
  const [bindingsMoreFailure, setBindingsMoreFailure] = useState<RequestFailure | null>(null);
  const [draft, setDraft] = useState<EtlDraftView | null>(null);
  const [pipeline, setPipeline] = useState<EtlPipeline | null>(null);
  const [draftLoadFailure, setDraftLoadFailure] = useState<{ definitionId: string; failure: RequestFailure } | null>(null);
  const [mutation, setMutation] = useState<MutationKind | null>(null);
  const [editorFailure, setEditorFailure] = useState<string | null>(null);
  const [editorNotice, setEditorNotice] = useState<string | null>(null);
  const [validation, setValidation] = useState<EtlValidationView | null>(null);
  const [versions, setVersions] = useState<EtlPublishedVersionView[]>([]);
  const [versionsCursor, setVersionsCursor] = useState<string | null>(null);
  const [versionsLoading, setVersionsLoading] = useState(false);
  const [versionsMoreLoading, setVersionsMoreLoading] = useState(false);
  const [versionsFailure, setVersionsFailure] = useState<RequestFailure | null>(null);
  const [versionsMoreFailure, setVersionsMoreFailure] = useState<RequestFailure | null>(null);
  const [artifact, setArtifact] = useState<EtlPublishedArtifactView | null>(null);
  const [artifactSelection, setArtifactSelection] = useState<string | null>(null);
  const [artifactLoading, setArtifactLoading] = useState(false);
  const [artifactFailure, setArtifactFailure] = useState<string | null>(null);

  const contextIsLoaded = loadedContext === context;
  const visibleDefinitions = contextIsLoaded ? definitions : [];
  const visibleBindings = contextIsLoaded ? bindings : [];
  const visibleDraft = contextIsLoaded && draft?.definitionId === selectedDefinition.current ? draft : null;
  const visiblePipeline = visibleDraft && pipeline ? pipeline : null;
  const isDirty = !!visibleDraft && !!visiblePipeline && !sameEtlPipeline(visibleDraft.pipeline, visiblePipeline);
  const canValidate = !!visibleDraft && !!visiblePipeline && !isDirty && mutation === null;
  const canPublish = canValidate && !!validation && validation.revision === visibleDraft?.revision &&
    validation.contentHash !== null && validation.contentHash.length > 0 && validation.diagnostics.length === 0;

  useEffect(() => {
    const generation = ++scopeGeneration.current;
    abortRequests();
    selectedDefinition.current = null;
    operation.current = null;
    setMutation(null);
    setLoadedContext(null);
    setDefinitions([]);
    setDefinitionsCursor(null);
    setDefinitionsLoading(true);
    setDefinitionsFailure(null);
    setDefinitionsMoreFailure(null);
    setBindings([]);
    setBindingsCursor(null);
    setBindingsLoading(true);
    setBindingsFailure(null);
    setBindingsMoreFailure(null);
    setDraft(null);
    setPipeline(null);
    setDraftLoadFailure(null);
    setEditorFailure(null);
    setEditorNotice(null);
    setValidation(null);
    setVersions([]);
    setVersionsCursor(null);
    setVersionsLoading(false);
    setVersionsFailure(null);
    setVersionsMoreFailure(null);
    setArtifact(null);
    setArtifactSelection(null);
    setArtifactFailure(null);
    void loadFirstDefinitions(context, generation, true);
    void loadFirstBindings(context, generation);

    return () => {
      abortRequests();
      if (scopeGeneration.current === generation) scopeGeneration.current++;
    };
  }, [context]);

  function abortRequests() {
    mutationController.current?.abort();
    definitionsController.current?.abort();
    definitionsMoreController.current?.abort();
    bindingsController.current?.abort();
    bindingsMoreController.current?.abort();
    versionsController.current?.abort();
    versionsMoreController.current?.abort();
    artifactController.current?.abort();
  }

  function isCurrentScope(requestContext: StudioEndpointContext, generation: number, controller?: AbortController | null) {
    return currentContext.current === requestContext && scopeGeneration.current === generation && !controller?.signal.aborted;
  }

  function isCurrentMutation(requestContext: StudioEndpointContext, generation: number, request: object) {
    return isCurrentScope(requestContext, generation, mutationController.current) && operation.current === request;
  }

  function beginMutation(kind: MutationKind) {
    if (operation.current) return null;
    const request = {};
    operation.current = request;
    mutationController.current?.abort();
    mutationController.current = new AbortController();
    setMutation(kind);
    setEditorFailure(null);
    setEditorNotice(null);
    return request;
  }

  function finishMutation(request: object) {
    if (operation.current !== request) return;
    operation.current = null;
    setMutation(null);
  }

  async function loadFirstDefinitions(requestContext: StudioEndpointContext, generation: number, markLoaded: boolean) {
    const controller = new AbortController();
    definitionsController.current?.abort();
    definitionsMoreController.current?.abort();
    definitionsController.current = controller;
    const requestId = ++definitionsGeneration.current;
    setDefinitionsLoading(true);
    setDefinitionsFailure(null);
    setDefinitionsMoreFailure(null);
    try {
      const page = await listEtlDefinitions(requestContext, { limit: pageSize }, controller.signal);
      if (!isCurrentScope(requestContext, generation, controller) || requestId !== definitionsGeneration.current) return;
      setDefinitions(page.items);
      setDefinitionsCursor(page.nextCursor);
    } catch (error) {
      if (!isCurrentScope(requestContext, generation, controller) || requestId !== definitionsGeneration.current) return;
      setDefinitionsFailure(failureOf(error));
    } finally {
      if (isCurrentScope(requestContext, generation, controller) && requestId === definitionsGeneration.current) {
        setDefinitionsLoading(false);
        if (markLoaded) setLoadedContext(requestContext);
      }
    }
  }

  async function loadFirstBindings(requestContext: StudioEndpointContext, generation: number) {
    const controller = new AbortController();
    bindingsController.current?.abort();
    bindingsMoreController.current?.abort();
    bindingsController.current = controller;
    const requestId = ++bindingsGeneration.current;
    setBindingsLoading(true);
    setBindingsFailure(null);
    setBindingsMoreFailure(null);
    try {
      const page = await listEtlBindings(requestContext, { limit: pageSize }, controller.signal);
      if (!isCurrentScope(requestContext, generation, controller) || requestId !== bindingsGeneration.current) return;
      setBindings(page.items);
      setBindingsCursor(page.nextCursor);
    } catch (error) {
      if (!isCurrentScope(requestContext, generation, controller) || requestId !== bindingsGeneration.current) return;
      setBindingsFailure(failureOf(error));
    } finally {
      if (isCurrentScope(requestContext, generation, controller) && requestId === bindingsGeneration.current)
        setBindingsLoading(false);
    }
  }

  async function loadMoreDefinitions() {
    if (!definitionsCursor || definitionsMoreLoading || definitionsLoading || !contextIsLoaded) return;
    invalidateEtlApiCapabilityCache(context);
    const requestContext = context;
    const generation = scopeGeneration.current;
    const controller = new AbortController();
    definitionsMoreController.current?.abort();
    definitionsMoreController.current = controller;
    const requestId = ++definitionsGeneration.current;
    setDefinitionsMoreLoading(true);
    setDefinitionsMoreFailure(null);
    try {
      const page = await listEtlDefinitions(requestContext, { limit: pageSize, cursor: definitionsCursor }, controller.signal);
      if (!isCurrentScope(requestContext, generation, controller) || requestId !== definitionsGeneration.current) return;
      setDefinitions(previous => appendUnique(previous, page.items, item => item.definitionId));
      setDefinitionsCursor(page.nextCursor);
    } catch (error) {
      if (!isCurrentScope(requestContext, generation, controller) || requestId !== definitionsGeneration.current) return;
      setDefinitionsMoreFailure(failureOf(error));
    } finally {
      if (isCurrentScope(requestContext, generation, controller) && requestId === definitionsGeneration.current)
        setDefinitionsMoreLoading(false);
    }
  }

  async function loadMoreBindings() {
    if (!bindingsCursor || bindingsMoreLoading || bindingsLoading || !contextIsLoaded) return;
    invalidateEtlApiCapabilityCache(context);
    const requestContext = context;
    const generation = scopeGeneration.current;
    const controller = new AbortController();
    bindingsMoreController.current?.abort();
    bindingsMoreController.current = controller;
    const requestId = ++bindingsGeneration.current;
    setBindingsMoreLoading(true);
    setBindingsMoreFailure(null);
    try {
      const page = await listEtlBindings(requestContext, { limit: pageSize, cursor: bindingsCursor }, controller.signal);
      if (!isCurrentScope(requestContext, generation, controller) || requestId !== bindingsGeneration.current) return;
      setBindings(previous => appendUnique(previous, page.items, item => `${item.alias}:${item.role}`));
      setBindingsCursor(page.nextCursor);
    } catch (error) {
      if (!isCurrentScope(requestContext, generation, controller) || requestId !== bindingsGeneration.current) return;
      setBindingsMoreFailure(failureOf(error));
    } finally {
      if (isCurrentScope(requestContext, generation, controller) && requestId === bindingsGeneration.current)
        setBindingsMoreLoading(false);
    }
  }

  async function loadFirstVersions(requestContext: StudioEndpointContext, definitionId: string, generation: number) {
    const controller = new AbortController();
    versionsController.current?.abort();
    versionsMoreController.current?.abort();
    versionsController.current = controller;
    const requestId = ++versionsGeneration.current;
    setVersions([]);
    setVersionsCursor(null);
    setVersionsLoading(true);
    setVersionsFailure(null);
    setVersionsMoreFailure(null);
    try {
      const page = await listEtlVersions(requestContext, definitionId, { limit: pageSize }, controller.signal);
      if (!isCurrentScope(requestContext, generation, controller) || selectedDefinition.current !== definitionId || requestId !== versionsGeneration.current) return;
      setVersions(page.items);
      setVersionsCursor(page.nextCursor);
    } catch (error) {
      if (!isCurrentScope(requestContext, generation, controller) || selectedDefinition.current !== definitionId || requestId !== versionsGeneration.current) return;
      setVersionsFailure(failureOf(error));
    } finally {
      if (isCurrentScope(requestContext, generation, controller) && selectedDefinition.current === definitionId && requestId === versionsGeneration.current)
        setVersionsLoading(false);
    }
  }

  async function loadMoreVersions() {
    const definitionId = selectedDefinition.current;
    if (!definitionId || !versionsCursor || versionsMoreLoading || versionsLoading || !contextIsLoaded) return;
    invalidateEtlApiCapabilityCache(context);
    const requestContext = context;
    const generation = scopeGeneration.current;
    const cursor = versionsCursor;
    const controller = new AbortController();
    versionsMoreController.current?.abort();
    versionsMoreController.current = controller;
    const requestId = ++versionsGeneration.current;
    setVersionsMoreLoading(true);
    setVersionsMoreFailure(null);
    try {
      const page = await listEtlVersions(requestContext, definitionId, { limit: pageSize, cursor }, controller.signal);
      if (!isCurrentScope(requestContext, generation, controller) || selectedDefinition.current !== definitionId || requestId !== versionsGeneration.current) return;
      setVersions(previous => appendUnique(previous, page.items, item => item.selection));
      setVersionsCursor(page.nextCursor);
    } catch (error) {
      if (!isCurrentScope(requestContext, generation, controller) || selectedDefinition.current !== definitionId || requestId !== versionsGeneration.current) return;
      setVersionsMoreFailure(failureOf(error));
    } finally {
      if (isCurrentScope(requestContext, generation, controller) && selectedDefinition.current === definitionId && requestId === versionsGeneration.current)
        setVersionsMoreLoading(false);
    }
  }

  async function selectDefinition(definitionId: string) {
    if (definitionId === selectedDefinition.current || operation.current) return;
    const request = beginMutation("loading-draft");
    if (!request) return;
    const requestContext = context;
    const generation = scopeGeneration.current;
    const controller = mutationController.current!;
    setDraftLoadFailure(null);
    try {
      if (isDirty && !(await dialogs.confirm({
        title: "Discard draft edits?",
        message: `Discard unsaved edits to ${visibleDraft?.definitionId} and open ${definitionId}?`,
        confirmLabel: "Discard edits",
        tone: "danger"
      }))) return;
      if (!isCurrentMutation(requestContext, generation, request)) return;
      const nextDraft = await getEtlDefinition(requestContext, definitionId, controller.signal);
      if (!isCurrentMutation(requestContext, generation, request)) return;
      if (nextDraft.definitionId !== definitionId) throw new Error("The server returned a different definition ID.");
      selectedDefinition.current = nextDraft.definitionId;
      setDraft(nextDraft);
      setPipeline(nextDraft.pipeline);
      setValidation(null);
      setDraftLoadFailure(null);
      setEditorFailure(null);
      setEditorNotice(null);
      clearArtifact();
      void loadFirstVersions(requestContext, nextDraft.definitionId, generation);
    } catch (error) {
      if (isCurrentMutation(requestContext, generation, request))
        setDraftLoadFailure({ definitionId, failure: failureOf(error) });
    } finally {
      finishMutation(request);
    }
  }

  async function reloadSelectedDraft() {
    const definitionId = selectedDefinition.current;
    if (!definitionId || operation.current) return;
    const request = beginMutation("loading-draft");
    if (!request) return;
    const requestContext = context;
    const generation = scopeGeneration.current;
    const controller = mutationController.current!;
    try {
      if (isDirty && !(await dialogs.confirm({
        title: "Discard draft edits?",
        message: `Discard unsaved edits to ${definitionId} and reload its saved draft?`,
        confirmLabel: "Reload saved draft",
        tone: "danger"
      }))) return;
      if (!isCurrentMutation(requestContext, generation, request) || selectedDefinition.current !== definitionId) return;
      invalidateEtlApiCapabilityCache(requestContext);
      const reloaded = await getEtlDefinition(requestContext, definitionId, controller.signal);
      if (!isCurrentMutation(requestContext, generation, request) || selectedDefinition.current !== definitionId) return;
      if (reloaded.definitionId !== definitionId) throw new Error("The server returned a different definition ID.");
      setDraft(reloaded);
      setPipeline(reloaded.pipeline);
      setValidation(null);
      setDraftLoadFailure(null);
      setEditorFailure(null);
      setDefinitions(previous => previous.map(item => item.definitionId === definitionId
        ? { ...item, revision: reloaded.revision, lastPublishedVersion: reloaded.lastPublishedVersion, updatedAt: reloaded.updatedAt }
        : item));
      setEditorNotice(`Reloaded saved draft ${definitionId} at revision ${reloaded.revision}.`);
    } catch (error) {
      if (isCurrentMutation(requestContext, generation, request))
        setEditorFailure(errorMessage(error, "reload the draft"));
    } finally {
      finishMutation(request);
    }
  }

  function refreshDefinitions() {
    invalidateEtlApiCapabilityCache(context);
    void loadFirstDefinitions(context, scopeGeneration.current, true);
  }

  function retryBindings() {
    invalidateEtlApiCapabilityCache(context);
    void loadFirstBindings(context, scopeGeneration.current);
  }

  function retryVersions(definitionId: string) {
    invalidateEtlApiCapabilityCache(context);
    void loadFirstVersions(context, definitionId, scopeGeneration.current);
  }

  function retryArtifact(version: EtlPublishedVersionView) {
    invalidateEtlApiCapabilityCache(context);
    void loadPublishedArtifact(version);
  }

  async function createNewDefinition() {
    const request = beginMutation("creating");
    if (!request) return;
    const requestContext = context;
    const generation = scopeGeneration.current;
    const controller = mutationController.current!;
    try {
      const id = await dialogs.prompt({
        title: "New ETL definition",
        message: "Enter an identifier using letters, numbers, dots, underscores, or hyphens.",
        confirmLabel: "Create draft",
        defaultValue: "new-pipeline",
        placeholder: "orders"
      });
      if (!isCurrentMutation(requestContext, generation, request) || id === null) return;
      if (!isValidEtlDefinitionId(id)) {
        setEditorFailure("Definition IDs must start with a letter or number and contain only letters, numbers, dots, underscores, or hyphens (up to 256 characters). No definition was created.");
        return;
      }
      if (isDirty && !(await dialogs.confirm({
        title: "Discard draft edits?",
        message: `Discard unsaved edits to ${visibleDraft?.definitionId} and create ${id}?`,
        confirmLabel: "Discard and create",
        tone: "danger"
      }))) return;
      if (!isCurrentMutation(requestContext, generation, request)) return;

      const created = await createEtlDefinition(requestContext, { id, pipeline: createDefaultEtlPipeline() }, controller.signal);
      if (!isCurrentMutation(requestContext, generation, request)) return;
      if (created.definitionId !== id) throw new Error("The server returned a different definition ID.");
      selectedDefinition.current = created.definitionId;
      setDraft(created);
      setPipeline(created.pipeline);
      setValidation(null);
      setDraftLoadFailure(null);
      clearArtifact();
      void loadFirstVersions(requestContext, created.definitionId, generation);
      void loadFirstDefinitions(requestContext, generation, false);
      setEditorNotice(`Created draft ${created.definitionId}. Save and validate it before publishing.`);
    } catch (error) {
      if (isCurrentMutation(requestContext, generation, request)) setEditorFailure(errorMessage(error, "create the draft"));
    } finally {
      finishMutation(request);
    }
  }

  async function saveDraft() {
    if (!visibleDraft || !visiblePipeline || !isDirty || operation.current) return;
    const request = beginMutation("saving");
    if (!request) return;
    const requestContext = context;
    const generation = scopeGeneration.current;
    const controller = mutationController.current!;
    const definitionId = visibleDraft.definitionId;
    try {
      const saved = await updateEtlDraft(requestContext, definitionId, {
        expectedRevision: visibleDraft.revision,
        pipeline: visiblePipeline
      }, controller.signal);
      if (!isCurrentMutation(requestContext, generation, request) || selectedDefinition.current !== definitionId) return;
      if (saved.definitionId !== definitionId) throw new Error("The server returned a different definition ID.");
      setDraft(saved);
      setPipeline(saved.pipeline);
      setValidation(null);
      setDraftLoadFailure(null);
      setDefinitions(previous => previous.map(item => item.definitionId === definitionId
        ? { ...item, revision: saved.revision, updatedAt: saved.updatedAt }
        : item));
      setEditorNotice(`Saved ${definitionId} at revision ${saved.revision}. Validate this revision before publishing.`);
    } catch (error) {
      if (isCurrentMutation(requestContext, generation, request)) setEditorFailure(errorMessage(error, "save the draft"));
    } finally {
      finishMutation(request);
    }
  }

  async function validateDraft() {
    if (!visibleDraft || isDirty || operation.current) return;
    const request = beginMutation("validating");
    if (!request) return;
    const requestContext = context;
    const generation = scopeGeneration.current;
    const controller = mutationController.current!;
    const definitionId = visibleDraft.definitionId;
    const revision = visibleDraft.revision;
    setValidation(null);
    try {
      const result = await validateEtlDraft(requestContext, definitionId, { expectedRevision: revision }, controller.signal);
      if (!isCurrentMutation(requestContext, generation, request) || selectedDefinition.current !== definitionId) return;
      if (result.revision !== revision) {
        setEditorFailure("Validation returned for a different draft revision. Your loaded draft was kept; validate it again before publishing.");
        return;
      }
      setValidation(result);
      setEditorNotice(result.diagnostics.length === 0 && result.contentHash
        ? `Revision ${revision} is valid as immutable version ${result.definitionVersion}. Review the diagnostics and publish explicitly when ready.`
        : `Validation found ${result.diagnostics.length} issue${result.diagnostics.length === 1 ? "" : "s"}. Edit the draft and validate again.`);
    } catch (error) {
      if (isCurrentMutation(requestContext, generation, request)) setEditorFailure(errorMessage(error, "validate the draft"));
    } finally {
      finishMutation(request);
    }
  }

  async function publishDraft() {
    if (!canPublish || !visibleDraft || !validation || !validation.contentHash || operation.current) return;
    const request = beginMutation("publishing");
    if (!request) return;
    const requestContext = context;
    const generation = scopeGeneration.current;
    const controller = mutationController.current!;
    const definitionId = visibleDraft.definitionId;
    // Publish the server's exact validation tuple; never derive a replacement version or hash locally.
    const tuple = {
      expectedRevision: validation.revision,
      definitionVersion: validation.definitionVersion,
      contentHash: validation.contentHash
    };
    try {
      const confirmed = await dialogs.confirm({
        title: "Publish immutable ETL version?",
        message: `Publish ${definitionId} version ${tuple.definitionVersion} from revision ${tuple.expectedRevision} with content hash ${tuple.contentHash}? Published versions cannot be changed.`,
        confirmLabel: "Publish version"
      });
      if (!confirmed || !isCurrentMutation(requestContext, generation, request)) return;
      const result = await publishEtlDefinition(requestContext, definitionId, tuple, controller.signal);
      if (!isCurrentMutation(requestContext, generation, request) || selectedDefinition.current !== definitionId) return;
      if (result.version.definitionId !== definitionId || result.version.definitionVersion !== tuple.definitionVersion || result.version.contentHash !== tuple.contentHash) {
        setEditorFailure("The server returned a different published version than the validated tuple. The draft was kept; inspect the version list before retrying.");
        return;
      }
      setValidation(null);
      setDefinitions(previous => previous.map(item => item.definitionId === definitionId
        ? { ...item, lastPublishedVersion: result.version.definitionVersion }
        : item));
      setEditorNotice(`${result.status === "replay" ? "This version was already published" : "Published immutable version"} ${result.version.definitionVersion}. Selection: ${result.version.selection}`);
      void loadFirstVersions(requestContext, definitionId, generation);
    } catch (error) {
      if (isCurrentMutation(requestContext, generation, request)) {
        if (error instanceof EtlApiError && error.code === "etl.validation-failed") setValidation(null);
        setEditorFailure(errorMessage(error, "publish the validated version"));
      }
    } finally {
      finishMutation(request);
    }
  }

  async function loadPublishedArtifact(version: EtlPublishedVersionView) {
    const definitionId = selectedDefinition.current;
    if (!definitionId || !contextIsLoaded) return;
    const requestContext = context;
    const generation = scopeGeneration.current;
    const requestId = ++artifactGeneration.current;
    const controller = new AbortController();
    artifactController.current?.abort();
    artifactController.current = controller;
    setArtifactSelection(version.selection);
    setArtifact(null);
    setArtifactLoading(true);
    setArtifactFailure(null);
    try {
      const loaded = await getEtlVersion(requestContext, definitionId, version.definitionVersion, controller.signal);
      if (!isCurrentScope(requestContext, generation, controller) || selectedDefinition.current !== definitionId || requestId !== artifactGeneration.current) return;
      if (loaded.version.selection !== version.selection || loaded.version.definitionId !== definitionId || loaded.version.definitionVersion !== version.definitionVersion) {
        setArtifactFailure("The returned artifact does not match the selected immutable version.");
        return;
      }
      setArtifact(loaded);
    } catch (error) {
      if (isCurrentScope(requestContext, generation, controller) && selectedDefinition.current === definitionId && requestId === artifactGeneration.current)
        setArtifactFailure(errorMessage(error, "load the published artifact"));
    } finally {
      if (isCurrentScope(requestContext, generation, controller) && selectedDefinition.current === definitionId && requestId === artifactGeneration.current)
        setArtifactLoading(false);
    }
  }

  function clearArtifact() {
    artifactController.current?.abort();
    artifactGeneration.current++;
    setArtifact(null);
    setArtifactSelection(null);
    setArtifactLoading(false);
    setArtifactFailure(null);
  }

  const selectedVersion = artifactSelection ? versions.find(version => version.selection === artifactSelection) : null;

  return <section className="etl-shell etl-definitions-page" aria-labelledby={`${pageId}-heading`}>
    <header className="etl-toolbar">
      <div>
        <p className="etl-editor-eyebrow">Design</p>
        <h1 id={`${pageId}-heading`}>ETL definitions</h1>
        <p>Create a finite file pipeline, validate a saved draft, and publish an immutable version.</p>
      </div>
      <div className="etl-actions">
        <button type="button" className="etl-primary" disabled={!contextIsLoaded || mutation !== null} onClick={() => void createNewDefinition()}>
          {mutation === "creating" ? "Creating draft…" : "New definition"}
        </button>
        <button type="button" disabled={!contextIsLoaded || definitionsLoading} onClick={refreshDefinitions}>
          {definitionsLoading && visibleDefinitions.length > 0 ? "Refreshing…" : "Refresh definitions"}
        </button>
      </div>
    </header>

    <section className="etl-editor-section" aria-labelledby={`${pageId}-definitions`}>
      <div className="etl-editor-section-heading">
        <div><h2 id={`${pageId}-definitions`}>Definitions</h2><p>Open a draft to edit it. Published versions remain available as immutable artifacts.</p></div>
        {contextIsLoaded && !definitionsLoading ? <span>{visibleDefinitions.length} loaded</span> : null}
      </div>
      {!contextIsLoaded || (definitionsLoading && visibleDefinitions.length === 0) ? <p role="status" aria-live="polite">Loading ETL definitions…</p> : null}
      {definitionsFailure && contextIsLoaded ? <FailureNotice failure={definitionsFailure} subject="ETL definitions" onRetry={refreshDefinitions} /> : null}
      {contextIsLoaded && !definitionsLoading && !definitionsFailure && visibleDefinitions.length === 0 ? <p role="status">No ETL definitions are available yet. Create a draft to begin.</p> : null}
      {visibleDefinitions.length > 0 ? <ul className="etl-definition-list">
        {visibleDefinitions.map(item => <li key={item.definitionId}>
          <button type="button" aria-pressed={visibleDraft?.definitionId === item.definitionId} disabled={mutation !== null}
            onClick={() => void selectDefinition(item.definitionId)}>
            <span>{item.definitionId}</span><small>Revision {item.revision} · published through v{item.lastPublishedVersion}</small>
          </button>
        </li>)}
      </ul> : null}
      {definitionsCursor && contextIsLoaded && !definitionsFailure ? <div className="etl-actions">
        <button type="button" disabled={definitionsLoading || definitionsMoreLoading} onClick={() => void loadMoreDefinitions()}>
          {definitionsMoreLoading ? "Loading more definitions…" : "Load more definitions"}
        </button>
      </div> : null}
      {definitionsMoreFailure ? <FailureNotice failure={definitionsMoreFailure} subject="more definitions" onRetry={() => void loadMoreDefinitions()} /> : null}
    </section>

    {contextIsLoaded ? <>
      {draftLoadFailure ? <FailureNotice failure={draftLoadFailure.failure} subject={`draft ${draftLoadFailure.definitionId}`} onRetry={() => void selectDefinition(draftLoadFailure.definitionId)} /> : null}
      {editorFailure ? <div className="etl-error" role="alert">{editorFailure}</div> : null}
      {editorNotice ? <div className="etl-notice" role="status" aria-live="polite">{editorNotice}</div> : null}

      {visibleDraft && visiblePipeline ? <section className="etl-editor-section" aria-labelledby={`${pageId}-draft`}>
        <div className="etl-editor-section-heading">
          <div><h2 id={`${pageId}-draft`}>Draft: {visibleDraft.definitionId}</h2><p>Revision {visibleDraft.revision} · last updated {new Date(visibleDraft.updatedAt).toLocaleString()}</p></div>
          <div className="etl-actions">
            <button type="button" disabled={mutation !== null} onClick={() => void reloadSelectedDraft()}>Reload draft</button>
            <button type="button" disabled={!isDirty || mutation !== null} onClick={() => void saveDraft()}>
              {mutation === "saving" ? "Saving…" : "Save draft"}
            </button>
            <button type="button" disabled={!canValidate} onClick={() => void validateDraft()}>
              {mutation === "validating" ? "Validating…" : "Validate saved revision"}
            </button>
            <button type="button" className="etl-primary" disabled={!canPublish} onClick={() => void publishDraft()}>
              {mutation === "publishing" ? "Publishing…" : "Publish immutable version"}
            </button>
          </div>
        </div>
        {isDirty ? <p className="etl-notice" role="status">Unsaved edits. Save the draft before validation or publication.</p> : null}
        {validation && validation.revision === visibleDraft.revision ? <p role="status" aria-live="polite">
          Validation for revision {validation.revision}: version {validation.definitionVersion}{validation.contentHash ? <> · <code>{validation.contentHash}</code></> : " · no publishable artifact"}.
        </p> : null}
        <EtlPipelineEditor
          pipeline={visiblePipeline}
          onChange={next => { setPipeline(next); setValidation(null); setEditorNotice(null); setEditorFailure(null); }}
          bindings={visibleBindings}
          readOnly={mutation !== null}
          diagnostics={validation?.revision === visibleDraft.revision ? validation.diagnostics : []}
        />
        <div className="etl-editor-section-heading">
          <div><h3>Host-approved bindings</h3><p>Bindings are fetched in bounded pages. Load more to reach aliases outside the current page.</p></div>
          {contextIsLoaded && !bindingsLoading ? <span>{visibleBindings.length} loaded</span> : null}
        </div>
        {bindingsLoading && visibleBindings.length === 0 ? <p role="status" aria-live="polite">Loading approved binding aliases…</p> : null}
        {bindingsFailure ? <FailureNotice failure={bindingsFailure} subject="approved bindings" onRetry={retryBindings} /> : null}
        {bindingsCursor && !bindingsFailure ? <div className="etl-actions">
          <button type="button" disabled={bindingsLoading || bindingsMoreLoading} onClick={() => void loadMoreBindings()}>
            {bindingsMoreLoading ? "Loading more bindings…" : "Load more bindings"}
          </button>
        </div> : null}
        {bindingsMoreFailure ? <FailureNotice failure={bindingsMoreFailure} subject="more bindings" onRetry={() => void loadMoreBindings()} /> : null}
      </section> : null}

      {visibleDraft ? <section className="etl-editor-section" aria-labelledby={`${pageId}-versions`}>
        <div className="etl-editor-section-heading">
          <div><h2 id={`${pageId}-versions`}>Published versions</h2><p>Each version is immutable. View its artifact by the exact selection issued by the server.</p></div>
          {versions.length > 0 ? <span>{versions.length} loaded</span> : null}
        </div>
        {versionsLoading && versions.length === 0 ? <p role="status" aria-live="polite">Loading published versions…</p> : null}
        {versionsFailure ? <FailureNotice failure={versionsFailure} subject="published versions" onRetry={() => retryVersions(visibleDraft.definitionId)} /> : null}
        {!versionsLoading && !versionsFailure && versions.length === 0 ? <p role="status">No published versions for this definition yet.</p> : null}
        {versions.length > 0 ? <ul className="etl-definition-list etl-version-list">
          {versions.map(version => <li key={version.selection}>
            <button type="button" aria-pressed={artifactSelection === version.selection} disabled={mutation !== null}
              onClick={() => void loadPublishedArtifact(version)}>
              <span>View version {version.definitionVersion}</span><small>{version.contentHash} · {version.publishedAt ? new Date(version.publishedAt).toLocaleString() : "publication time unavailable"}</small>
            </button>
          </li>)}
        </ul> : null}
        {versionsCursor && !versionsFailure ? <div className="etl-actions">
          <button type="button" disabled={versionsLoading || versionsMoreLoading} onClick={() => void loadMoreVersions()}>
            {versionsMoreLoading ? "Loading more versions…" : "Load more versions"}
          </button>
        </div> : null}
        {versionsMoreFailure ? <FailureNotice failure={versionsMoreFailure} subject="more published versions" onRetry={() => void loadMoreVersions()} /> : null}
        {artifactFailure ? <div className="etl-error" role="alert"><p>{artifactFailure}</p>
          {selectedVersion ? <button type="button" disabled={artifactLoading || mutation !== null} onClick={() => retryArtifact(selectedVersion)}>Retry</button> : null}
        </div> : null}
        {artifactLoading ? <p role="status" aria-live="polite">Loading selected immutable artifact…</p> : null}
        {artifact && selectedVersion && artifact.version.selection === selectedVersion.selection ? <section className="etl-editor-section" aria-label="Published ETL artifact">
          <h3>Published artifact · version {artifact.version.definitionVersion}</h3>
          <p>Exact selection: <code>{artifact.version.selection}</code></p>
          <EtlPipelineEditor pipeline={artifact.pipeline} onChange={() => {}} bindings={visibleBindings} readOnly />
        </section> : null}
      </section> : null}
    </> : null}
  </section>;
}

function FailureNotice({ failure, subject, onRetry }: { failure: RequestFailure; subject: string; onRetry(): void }) {
  const message = failure.kind === "forbidden"
    ? `You do not have permission to read ${subject}.`
    : failure.kind === "unavailable"
      ? `The ETL API is unavailable for ${subject}.`
      : failure.kind === "not-found"
        ? `The requested ${subject} is no longer available.`
        : `Could not load ${subject}.`;
  return <div className="etl-error" role="alert"><p>{message}</p><button type="button" onClick={onRetry}>Retry</button></div>;
}

function failureOf(error: unknown): RequestFailure {
  return { error, kind: classifyEtlApiError(error) };
}

function errorMessage(error: unknown, action: string): string {
  if (error instanceof EtlApiError && error.code === "etl.validation-failed")
    return "The saved draft is no longer valid for publication. Validate again to see current diagnostics. Your edits were preserved.";
  if (error instanceof EtlApiError && (error.status === 409 || error.code === "etl.definition-conflict"))
    return `The draft changed on the server while you were trying to ${action}. Your local edits were preserved. Reload the definition explicitly to compare before continuing.`;
  if (error instanceof EtlApiError && error.status === 404)
    return `The selected definition is no longer available. Your current edits were preserved.`;
  const kind = classifyEtlApiError(error);
  if (kind === "forbidden") return `You do not have permission to ${action}. Your current edits were preserved.`;
  if (kind === "unavailable") return `The ETL API is unavailable while trying to ${action}. Your current edits were preserved.`;
  return `Could not ${action}. Your current edits were preserved${error instanceof EtlApiError && error.code ? ` (${error.code})` : ""}.`;
}
