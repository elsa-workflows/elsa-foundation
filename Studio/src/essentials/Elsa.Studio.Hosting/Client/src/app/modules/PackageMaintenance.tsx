import React, { useEffect, useRef, useState } from "react";
import type { ElsaStudioModuleApi } from "../../sdk";
import { StudioAlert } from "../ui";
import { getErrorMessage, type HostModel, type ModuleManagementRegistryResponse } from "./moduleManagementApi";

interface MaintenanceDiagnostic { code: string; message: string }
interface MaintenanceCandidate {
  install: { packageId: string; version: string };
  classification: string;
  reasons: string[];
  attempt: string;
  physicalOutcome: string | null;
  journalPhase: string | null;
  diagnostic?: MaintenanceDiagnostic | null;
}

// Detached Nuplane observations. Only root/epoch/policy form confirmation; candidate paths are never sent back.
export interface PackageMaintenanceResult {
  rootLabel: string;
  rootIdentity: Record<string, unknown> | null;
  enrollmentEpoch: number | null;
  mode: string;
  status: string;
  protectionKnowledge: string;
  inventoryComplete: boolean;
  retention: { keepNewestInactiveVersionsPerPackage: number } | null;
  diagnostics: MaintenanceDiagnostic[];
  inventoryIssues: { relativePath: string; reason: string }[];
  candidates: MaintenanceCandidate[];
}

export function PackageMaintenance({ api, host, registry, disabled }: {
  api: ElsaStudioModuleApi;
  host: HostModel;
  registry: ModuleManagementRegistryResponse;
  disabled: boolean;
}) {
  const [rootLabel, setRootLabel] = useState("default");
  const [result, setResult] = useState<PackageMaintenanceResult | null>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const lifetime = useRef<AbortController | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    return () => controller.abort();
  }, []);

  const available = !disabled && host.mutationsAvailable && registry.capabilities.canPrunePackages && registry.retentionPolicy.status === "Supported";
  const recoverable = result?.protectionKnowledge === "Known" || (result?.diagnostics.length !== 0 &&
    result?.diagnostics.every(diagnostic => diagnostic.code === "StaleGraphUseRequiresRecovery"));
  const canConfirm = available && result?.status === "Preview" && result.mode === "Preview" && result.inventoryComplete &&
    result.rootIdentity !== null && result.rootLabel === rootLabel && Number.isSafeInteger(result.enrollmentEpoch) && recoverable;

  async function request(execute: boolean) {
    if (!available || pending || (execute && !canConfirm)) return;
    const observed = result;
    const signal = lifetime.current?.signal;
    setPending(true);
    setError(null);
    try {
      if (execute) {
        const confirmed = await api.dialogs.confirm({
          title: "Execute package cleanup",
          message: `Execute cleanup for ${host.label}, store "${rootLabel}", epoch ${observed!.enrollmentEpoch}? Protection and eligible versions will be checked again. The result can differ from this preview.`,
          confirmLabel: "Execute cleanup",
          tone: "danger"
        });
        if (!confirmed || signal?.aborted) return;
      }
      const next = await host.context.http.postJson<PackageMaintenanceResult>("/_elsa/module-management/prune", {
        dryRun: !execute,
        rootLabel,
        ...(execute ? { confirmation: {
          rootLabel: observed!.rootLabel,
          rootIdentity: observed!.rootIdentity,
          enrollmentEpoch: observed!.enrollmentEpoch,
          retention: observed!.retention
        } } : {})
      }, { signal });
      if (signal?.aborted) return;
      if (!next || !Array.isArray(next.candidates) || !Array.isArray(next.diagnostics) || typeof next.status !== "string")
        throw new Error("The host did not return a package-maintenance report. Preview again before executing.");
      setResult(next);
    } catch (failure) {
      if (!signal?.aborted) {
        setResult(null);
        setError(getErrorMessage(failure));
      }
    } finally {
      if (!signal?.aborted) setPending(false);
    }
  }

  return <div className="modules-form" aria-label={`${host.label} package maintenance`}>
    <label>
      <span>Package-store label</span>
      <input value={rootLabel} disabled={!available || pending} onChange={event => { setRootLabel(event.target.value); setResult(null); setError(null); }} />
    </label>
    <button type="button" className="studio-button" disabled={!available || pending || !rootLabel.trim()} onClick={() => void request(false)}>
      Preview cleanup
    </button>
    {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
    {result ? <div aria-live="polite" aria-label="Package maintenance report">
      <StudioAlert tone={result.status === "Completed" ? "success" : result.status === "Preview" ? "info" : "warning"}>
        {result.mode}: {result.status}. Protection: {result.protectionKnowledge}. Inventory: {result.inventoryComplete ? "complete" : "incomplete"}.
        {result.mode === "Execute" ? ` Observed deleted installs: ${result.candidates.filter(candidate => candidate.physicalOutcome === "Deleted").length}.` : " No files were deleted by this preview."}
      </StudioAlert>
      <p>Store: {result.rootLabel}; epoch: {result.enrollmentEpoch ?? "unavailable"}. {result.retention === null ? "Retain all installs." : `Keep ${result.retention.keepNewestInactiveVersionsPerPackage} inactive versions per package.`}</p>
      {result.protectionKnowledge !== "Known" ? <p>Protection is uncertain. Execution must establish complete protection and may refuse cleanup.</p> : null}
      <ul>
        {result.diagnostics.map((diagnostic, index) => <li key={index}>{diagnostic.code}: {diagnostic.message}</li>)}
        {result.inventoryIssues?.map((issue, index) => <li key={`inventory-${index}`}>{issue.relativePath}: {issue.reason}</li>)}
        {result.candidates.map((candidate, index) => <li key={`candidate-${index}`}>
          {candidate.install.packageId} {candidate.install.version}: {candidate.classification}; attempt {candidate.attempt}; physical outcome {candidate.physicalOutcome ?? "not observed"}.
          {candidate.reasons.length ? `${candidate.reasons.join(", ")}. ` : ""}{candidate.journalPhase ? `Journal: ${candidate.journalPhase}.` : ""} {candidate.diagnostic?.message}
        </li>)}
      </ul>
      {result.mode === "Preview" ? <button type="button" className="studio-button" disabled={!canConfirm || pending} onClick={() => void request(true)}>
        Execute cleanup
      </button> : null}
    </div> : null}
  </div>;
}
