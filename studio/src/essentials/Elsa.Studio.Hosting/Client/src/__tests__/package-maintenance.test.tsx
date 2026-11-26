import React from "react";
import { createRoot } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PackageMaintenance, type PackageMaintenanceResult } from "../app/modules/PackageMaintenance";
import { normalizeModuleManagementRegistry, type HostModel } from "../app/modules/moduleManagementApi";
import { createEndpointContext, type ElsaStudioModuleApi } from "../sdk";

const mounted: Array<() => void> = [];
afterEach(() => { mounted.splice(0).forEach(dispose => dispose()); vi.restoreAllMocks(); });
const tick = () => new Promise(resolve => setTimeout(resolve, 0));

function report(overrides: Partial<PackageMaintenanceResult> = {}): PackageMaintenanceResult {
  return {
    rootLabel: "default", rootIdentity: { volume: "owned-volume", file: "owned-root" }, enrollmentEpoch: 7,
    mode: "Preview", status: "Preview", protectionKnowledge: "Known", inventoryComplete: true, retention: null,
    diagnostics: [], inventoryIssues: [],
    candidates: [{ install: { packageId: "Acme.Widget", version: "1.0.0" }, classification: "Eligible", reasons: [], attempt: "NotAttempted", physicalOutcome: null, journalPhase: null }],
    ...overrides
  };
}

function render(next: PackageMaintenanceResult = report(), confirm = vi.fn(async () => true)) {
  const context = createEndpointContext("https://studio.example/");
  const post = vi.spyOn(context.http, "postJson").mockResolvedValue(next);
  const host: HostModel = { id: "studio", label: "Studio", runtime: "Acme", mutationsAvailable: true, context, readRegistry: vi.fn() };
  const registry = normalizeModuleManagementRegistry({ retentionPolicy: { status: "Supported", keepNewestInactiveVersionsPerPackage: null }, capabilities: { canUploadPackages: false, canManageFeeds: false, canReconcile: false, canPrunePackages: true, feedChangesRequireRestart: false } });
  const container = document.createElement("div"); document.body.append(container);
  const root = createRoot(container);
  let key = "initial";
  function draw() { flushSync(() => root.render(<PackageMaintenance key={key} host={host} registry={registry} api={{ dialogs: { confirm } } as unknown as ElsaStudioModuleApi} disabled={false} />)); }
  draw();
  mounted.push(() => { flushSync(() => root.unmount()); container.remove(); });
  async function click(label: string) {
    const button = Array.from(container.querySelectorAll("button")).find(candidate => candidate.textContent?.trim() === label)!;
    expect(button).toBeTruthy();
    flushSync(() => button.click()); await tick(); await tick();
  }
  return { container, post, confirm, click, reset: () => { key = "changed-policy"; draw(); } };
}

describe("native package maintenance", () => {
  it("previews first, honors cancel and sends only exact observed confirmation on explicit execution", async () => {
    const ui = render(report(), vi.fn(async () => false));
    expect(ui.container.textContent).not.toContain("Execute cleanup");
    await ui.click("Preview cleanup");
    expect(ui.post.mock.calls[0][1]).toEqual({ dryRun: true, rootLabel: "default" });
    expect(ui.container.textContent).toContain("No files were deleted by this preview");
    await ui.click("Execute cleanup");
    expect(ui.confirm).toHaveBeenCalledOnce(); expect(ui.post).toHaveBeenCalledTimes(1);
    ui.confirm.mockResolvedValue(true);
    ui.post.mockResolvedValue(report({ mode: "Execute", status: "Refused", diagnostics: [{ code: "EpochChanged", message: "Preview again" }] }));
    await ui.click("Execute cleanup");
    expect(ui.post.mock.calls[1][1]).toEqual({ dryRun: false, rootLabel: "default", confirmation: {
      rootLabel: "default", rootIdentity: report().rootIdentity, enrollmentEpoch: 7, retention: null
    } });
    expect(ui.container.textContent).toContain("Execute: Refused");
    expect(ui.container.textContent).toContain("EpochChanged: Preview again");
    expect(ui.container.textContent).not.toContain("Pruned eligible");
  });

  it.each(["Failed", "Cancelled", "Completed"])("retains observed physical facts for a %s response", async status => {
    const ui = render(); await ui.click("Preview cleanup");
    ui.post.mockResolvedValue(report({ mode: "Execute", status, candidates: [{ ...report().candidates[0], attempt: "Completed", physicalOutcome: "Deleted", journalPhase: "Complete" }] }));
    await ui.click("Execute cleanup");
    expect(ui.container.textContent).toContain(`Execute: ${status}`);
    expect(ui.container.textContent).toContain("Observed deleted installs: 1");
    expect(ui.container.textContent).toContain("physical outcome Deleted");
    expect(ui.container.textContent).not.toContain("No files were deleted");
  });

  it.each([
    { inventoryComplete: false }, { enrollmentEpoch: null }, { rootIdentity: null },
    { protectionKnowledge: "Unknown", diagnostics: [{ code: "EnrollmentRequired", message: "No complete enrollment" }] }
  ])("does not treat Preview alone as execution permission: %j", async overrides => {
    const ui = render(report(overrides)); await ui.click("Preview cleanup");
    const execute = Array.from(ui.container.querySelectorAll("button")).find(button => button.textContent?.trim() === "Execute cleanup")!;
    expect(execute.disabled).toBe(true);
  });

  it("allows explicit recovery confirmation while showing stale-use uncertainty", async () => {
    const ui = render(report({ protectionKnowledge: "Unknown", diagnostics: [{ code: "StaleGraphUseRequiresRecovery", message: "Fresh recovery required" }] }));
    await ui.click("Preview cleanup");
    expect(ui.container.textContent).toContain("Protection is uncertain");
    expect(Array.from(ui.container.querySelectorAll("button")).find(button => button.textContent?.trim() === "Execute cleanup")!.disabled).toBe(false);
  });

  it("clears plans on policy/host remount and does not execute a confirmation resolved after unmount", async () => {
    let resolve!: (confirmed: boolean) => void;
    const ui = render(report(), vi.fn(() => new Promise<boolean>(done => { resolve = done; })));
    await ui.click("Preview cleanup"); await ui.click("Execute cleanup");
    ui.reset(); resolve(true); await tick(); await tick();
    expect(ui.post).toHaveBeenCalledTimes(1);
    expect(ui.container.querySelector("[aria-label='Package maintenance report']")).toBeNull();
  });

  it("keeps older count/age payloads without inventing a supported retention policy", () => {
    const legacy = normalizeModuleManagementRegistry({ retentionPolicy: { retainLastNVersions: 3, retainYoungerThanDays: 30, mode: "Automatic" } });
    expect(legacy.retentionPolicy.status).toBe("LegacyUnsupported");
    expect(legacy.retentionPolicy.retainLastNVersions).toBe(3);
    expect(legacy.capabilities.canManageRetentionPolicy).toBe(false);
    expect(normalizeModuleManagementRegistry().retentionPolicy.status).toBe("Unavailable");
  });
});
