import React, { useCallback, useEffect, useMemo, useRef, useState, type MouseEvent } from "react";
import { ArrowRight, CircleAlert, LoaderCircle, Plus, Workflow } from "lucide-react";
import { useAuthSession, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { isHomeWidgetFeatureEnabled } from "./homeLayout";
import { useHomeDialogFocus } from "./useHomeDialogFocus";
import { loadRecentWorkflows, navigateStudioPath, type HomeWorkflowSummary } from "./widgets/homeData";

const designRead = "workflows/design:read";
const designManage = "workflows/design:manage";
const runtimeRead = "workflows/runtime:read";

type DiscoveryState =
  | { kind: "loading" }
  | { kind: "empty" }
  | { kind: "failed"; message: string; denied: boolean }
  | { kind: "ready"; candidates: HomeWorkflowSummary[]; total: number };

type ActiveDialog = { authorityKey: string; discovery: DiscoveryState };

export function HomeTaskGroup({ api }: { api: ElsaStudioModuleApi }) {
  const session = useAuthSession();
  const workflowsEnabled = isHomeWidgetFeatureEnabled({ requiresFeature: "workflows" }, api.runtime.enabledFeatures);
  const authenticated = session.status === "authenticated";
  const canRead = !authenticated || hasEffectivePermission(session.permissions, designRead);
  const canManage = !authenticated || hasEffectivePermission(session.permissions, designManage);
  const canReadRuntime = !authenticated || hasEffectivePermission(session.permissions, runtimeRead);
  const accessLimited = authenticated && (!canRead || !canManage);
  const authorityKey = useMemo(() => JSON.stringify([
    api.backend.baseUrl,
    api.runtime.hostId,
    workflowsEnabled,
    session.status,
    session.subject ?? "",
    session.tenantId ?? "",
    [...session.permissions].sort()
  ]), [api, session.permissions, session.status, session.subject, session.tenantId, workflowsEnabled]);
  const authorityRef = useRef(authorityKey);
  authorityRef.current = authorityKey;
  const generationRef = useRef(0);
  const controllerRef = useRef<AbortController | null>(null);
  const continueButtonRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useRef<HTMLElement>(null);
  const homeFallbackRef = useRef<HTMLElement>(null);
  const [dialogState, setDialogState] = useState<ActiveDialog | null>(null);
  const activeDialog = workflowsEnabled && dialogState?.authorityKey === authorityKey ? dialogState : null;

  const closeDialog = useCallback(() => {
    controllerRef.current?.abort();
    controllerRef.current = null;
    generationRef.current++;
    setDialogState(null);
  }, []);
  const updateDiscovery = useCallback((requestedAuthority: string, discovery: DiscoveryState) => {
    setDialogState(current => authorityRef.current === requestedAuthority && current?.authorityKey === requestedAuthority
      ? { ...current, discovery }
      : current);
  }, []);
  useHomeDialogFocus(dialogRef, activeDialog ? closeDialog : null, homeFallbackRef);

  useEffect(() => () => {
    controllerRef.current?.abort();
    generationRef.current++;
  }, []);

  const previousAuthorityRef = useRef(authorityKey);
  useEffect(() => {
    if (previousAuthorityRef.current === authorityKey) return;
    previousAuthorityRef.current = authorityKey;
    closeDialog();
  }, [authorityKey, closeDialog]);

  const startDiscovery = useCallback(() => {
    if (!canRead) return;
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    const generation = ++generationRef.current;
    const requestedAuthority = authorityKey;
    setDialogState({ authorityKey: requestedAuthority, discovery: { kind: "loading" } });

    void loadRecentWorkflows(api, controller.signal).then(workflows => {
      if (controller.signal.aborted || generation !== generationRef.current || authorityRef.current !== requestedAuthority) return;
      const candidates = workflows.filter(workflow => Boolean(workflow.draftId?.trim()));
      if (candidates.length === 0) {
        updateDiscovery(requestedAuthority, { kind: "empty" });
        return;
      }
      if (candidates.length === 1 && navigateStudioPath(workflowDefinitionHref(candidates[0].id))) {
        closeDialog();
        return;
      }
      updateDiscovery(requestedAuthority, { kind: "ready", candidates: candidates.slice(0, 8), total: candidates.length });
    }).catch(error => {
      if (controller.signal.aborted || generation !== generationRef.current || authorityRef.current !== requestedAuthority) return;
      const status = errorStatus(error);
      updateDiscovery(requestedAuthority, {
        kind: "failed",
        denied: status === 401 || status === 403,
        message: status === 401 || status === 403
          ? "You can’t read workflow definitions with your current access."
          : "Current drafts couldn’t be checked. Retry or browse workflows."
      });
    });
  }, [api, authorityKey, canRead, closeDialog, updateDiscovery]);

  if (!workflowsEnabled) return null;

  const createHref = taskHref("/workflows/definitions", { homeAction: "create" });
  const browseHref = taskHref("/workflows/definitions");
  const attentionHref = taskHref("/workflows/instances", { incidentHealth: "active" });
  const handleLinkClick = (event: MouseEvent<HTMLAnchorElement>, href: string, afterNavigate?: () => void) => {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.currentTarget.target || event.currentTarget.hasAttribute("download")) return;
    event.preventDefault();
    if (navigateStudioPath(href)) afterNavigate?.();
  };

  return (
    <section ref={homeFallbackRef} className="home-task-group" aria-labelledby="home-task-group-title" tabIndex={-1}>
      <div className="home-task-group-heading">
        <div>
          <h3 id="home-task-group-title">Workflow tasks</h3>
          <p>Start a workflow, continue a draft, or review active incidents.</p>
        </div>
      </div>
      <div className="home-task-group-actions">
        {canManage ? (
          <a className="home-task-card home-task-card--primary" href={createHref} onClick={event => handleLinkClick(event, createHref)}>
            <span className="home-task-card-icon"><Plus size={19} aria-hidden="true" /></span>
            <span className="home-task-card-copy"><strong>Create workflow</strong><span>Choose a starting point for your workflow.</span></span>
            <ArrowRight size={16} aria-hidden="true" />
          </a>
        ) : (
          <div className="home-task-card home-task-card--disabled" aria-disabled="true">
            <span className="home-task-card-icon"><Plus size={19} aria-hidden="true" /></span>
            <span className="home-task-card-copy"><strong>Create workflow</strong><span>Requires workflow design access.</span></span>
          </div>
        )}
        <button ref={continueButtonRef} className="home-task-card" type="button" onClick={startDiscovery} disabled={!canRead} aria-describedby={authenticated && !canRead ? "home-task-access" : undefined}>
          <span className="home-task-card-icon"><Workflow size={19} aria-hidden="true" /></span>
          <span className="home-task-card-copy"><strong>Continue editing</strong><span>Open the current draft for a workflow.</span></span>
          <ArrowRight size={16} aria-hidden="true" />
        </button>
        {canReadRuntime ? (
          <a className="home-task-card" href={attentionHref} onClick={event => handleLinkClick(event, attentionHref)}>
            <span className="home-task-card-icon"><CircleAlert size={19} aria-hidden="true" /></span>
            <span className="home-task-card-copy"><strong>Needs attention</strong><span>Review runs with active incidents.</span></span>
            <ArrowRight size={16} aria-hidden="true" />
          </a>
        ) : (
          <div className="home-task-card home-task-card--disabled" aria-disabled="true">
            <span className="home-task-card-icon"><CircleAlert size={19} aria-hidden="true" /></span>
            <span className="home-task-card-copy"><strong>Needs attention</strong><span>Requires access to read workflow runs.</span></span>
          </div>
        )}
      </div>
      <div className="home-task-group-footer">
        {canRead
          ? <a href={browseHref} onClick={event => handleLinkClick(event, browseHref)}>Browse workflows<ArrowRight size={14} aria-hidden="true" /></a>
          : <span aria-disabled="true">Browse workflows · requires workflow design access</span>}
        {accessLimited ? <span id="home-task-access" role="note">Create requires workflow design access. Continue and Browse require access to read workflow definitions.</span> : null}
      </div>
      {activeDialog ? (
        <div className="dashboard-settings-backdrop">
          <section ref={dialogRef} className="dashboard-settings-dialog home-continue-dialog" role="dialog" aria-modal="true" aria-labelledby="home-continue-title" tabIndex={-1}>
            <h4 id="home-continue-title">Continue editing</h4>
            {activeDialog.discovery.kind === "loading" ? <p role="status"><LoaderCircle size={16} aria-hidden="true" /> Checking current workflow drafts…</p> : null}
            {activeDialog.discovery.kind === "empty" ? <div>
              <p>No workflows have a current draft to continue.</p>
              {canManage
                ? <a href={createHref} onClick={event => handleLinkClick(event, createHref, closeDialog)}>Create workflow</a>
                : <p role="note">Creating a workflow requires workflow design access.</p>}
              <a href={browseHref} onClick={event => handleLinkClick(event, browseHref, closeDialog)}>Browse workflows</a>
            </div> : null}
            {activeDialog.discovery.kind === "failed" ? <div>
              <p role={activeDialog.discovery.denied ? "alert" : "status"}>{activeDialog.discovery.message}</p>
              {!activeDialog.discovery.denied ? <button type="button" onClick={startDiscovery}>Retry</button> : null}
              <a href={browseHref} onClick={event => handleLinkClick(event, browseHref, closeDialog)}>Browse workflows</a>
            </div> : null}
            {activeDialog.discovery.kind === "ready" ? <div>
              <p role="status">{activeDialog.discovery.total > activeDialog.discovery.candidates.length
                ? `Showing the 8 most recently changed of ${activeDialog.discovery.total} current drafts.`
                : `${activeDialog.discovery.total} current ${activeDialog.discovery.total === 1 ? "draft" : "drafts"}.`}</p>
              <ul aria-label="Current workflow drafts">
                {activeDialog.discovery.candidates.map(workflow => {
                  const href = workflowDefinitionHref(workflow.id);
                  return <li key={workflow.id}>
                    <a href={href} onClick={event => handleLinkClick(event, href, closeDialog)}>
                      <strong>{workflow.name}</strong><code>{workflow.id}</code>
                    </a>
                  </li>;
                })}
              </ul>
              <a href={browseHref} onClick={event => handleLinkClick(event, browseHref, closeDialog)}>Browse all workflows</a>
            </div> : null}
            <div className="home-continue-dialog-actions"><button type="button" onClick={closeDialog}>Cancel</button></div>
          </section>
        </div>
      ) : null}
    </section>
  );
}

export function hasEffectivePermission(grants: readonly string[], requirement: string) {
  const [requiredResource, requiredVerb] = requirement.normalize("NFC").toUpperCase().split(":");
  return grants.some(grant => {
    const canonical = grant.normalize("NFC").toUpperCase();
    if (!canonical || canonical !== canonical.trim() || containsWhitespaceOrControl(canonical)) return false;
    if (canonical === "*") return true;
    const separator = canonical.indexOf(":");
    if (separator <= 0 || separator !== canonical.lastIndexOf(":") || separator === canonical.length - 1) return false;
    const resource = canonical.slice(0, separator);
    const verb = canonical.slice(separator + 1);
    if (verb.includes("/") || (verb.includes("*") && verb !== "*")) return false;
    const wholeResource = resource === "*";
    const resourceSegments = resource.split("/");
    const subtree = resourceSegments.length > 1 && resourceSegments.at(-1) === "*";
    const namedSegments = subtree ? resourceSegments.slice(0, -1) : resourceSegments;
    if (!wholeResource && namedSegments.some(segment => !segment || segment.includes("*"))) return false;
    if (!subtree && !wholeResource && resource.includes("*")) return false;
    if (wholeResource && resourceSegments.length !== 1) return false;
    const resourceMatches = wholeResource
      || resource === requiredResource
      || (subtree && (requiredResource === resource.slice(0, -2) || requiredResource.startsWith(resource.slice(0, -1))));
    const verbMatches = verb === "*" || verb === requiredVerb || (verb === "MANAGE" && requiredVerb === "READ");
    return resourceMatches && verbMatches;
  });
}

function containsWhitespaceOrControl(value: string) {
  return Array.from(value).some(character => {
    const codePoint = character.codePointAt(0)!;
    return /\s/u.test(character) || codePoint <= 0x1f || (codePoint >= 0x7f && codePoint <= 0x9f);
  });
}

function taskHref(path: string, parameters: Record<string, string> = {}) {
  const url = new URL(window.location.href);
  url.pathname = path;
  url.searchParams.delete("definition");
  url.searchParams.delete("homeAction");
  url.searchParams.delete("incidentHealth");
  for (const [key, value] of Object.entries(parameters)) url.searchParams.set(key, value);
  return `${url.pathname}${url.search}${url.hash}`;
}

function workflowDefinitionHref(id: string) {
  return taskHref("/workflows/definitions", { definition: id });
}

function errorStatus(error: unknown) {
  return error && typeof error === "object" && "status" in error && typeof error.status === "number"
    ? error.status
    : undefined;
}
