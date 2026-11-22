import { PeerViewSwitch, SkeletonRows, StatusChip, StudioAlert, StudioCollectionEmptyState, StudioField } from "@elsa-workflows/studio-ui";
import React, { useEffect, useMemo, useState } from "react";
import { qualifyStudioBackendPath, useAuthSession, type ElsaStudioModuleApi, type StudioDashboardWidgetBodyProps } from "@elsa-workflows/studio-sdk";
import { attentionScopeKey, groupAttentionItems, type AttentionSnapshot } from "./attentionApi";
import { AttentionPreferenceStore, isSnoozed, snooze, type AttentionPreferences } from "./attentionPreferences";
const views = [{ id: "all", label: "All" }, { id: "critical", label: "Critical" }, { id: "snoozed", label: "Snoozed" }];
const severities = {
  critical: { label: "Critical", tone: "danger" },
  warning: { label: "Warning", tone: "warning" },
  info: { label: "Info", tone: "accent" }
} as const;
export function AttentionWidget({ api, snapshot, size }: { api: ElsaStudioModuleApi } & StudioDashboardWidgetBodyProps<AttentionSnapshot, unknown>) {
  const [filter, setFilter] = useState("all"); const [preferences, setPreferences] = useState<AttentionPreferences>({ snoozes: [] });
  const [preferenceError, setPreferenceError] = useState<string | null>(null);
  const session = useAuthSession();
  const store = useMemo(() => new AttentionPreferenceStore(api, api.runtime.hostId ?? "default", attentionScopeKey(api, session.subject ?? "anonymous", session.tenantId ?? "default"), session.status !== "authenticated"), [api, session]);
  useEffect(() => { let current = true; setPreferenceError(null); void store.load().then(value => { if (current) setPreferences(value); }).catch(() => { if (current) setPreferenceError("Attention preferences are unavailable."); }); return () => { current = false; }; }, [store]);
  if (!snapshot) return <SkeletonRows rows={3} label="Loading attention" />;
  const visible = snapshot.items
    .filter(item => filter === "snoozed" ? isSnoozed(item, preferences.snoozes) : !isSnoozed(item, preferences.snoozes) && (filter !== "critical" || item.severity === "critical"))
    .slice(0, size === "full" ? 20 : 5);
  const applySnooze = (items: typeof snapshot.items, duration: number) => { const keys = new Set(items.map(item => `${item.contributorId}:${item.id}`)); const next = { snoozes: [...preferences.snoozes.filter(value => !keys.has(`${value.contributorId}:${value.itemId}`)), ...items.map(item => snooze(item, duration))] }; setPreferences(next); setPreferenceError(null); void store.save(next).catch(() => setPreferenceError("Snooze could not be saved.")); };
  const emptyTitle = filter === "snoozed" ? "No snoozed items" : snapshot.failures.length ? "No items from available sources"
    : filter === "critical" ? "No critical items" : "All clear";
  return <div className="attention-widget">
    <PeerViewSwitch ariaLabel="Attention filters" views={views} value={filter} onChange={setFilter} />
    {snapshot.failures.length ? <StudioAlert tone="warning">Dashboard data incomplete: {snapshot.failures.map(value => value.displayName).join(", ")}</StudioAlert> : null}
    {preferenceError ? <StudioAlert tone="warning">{preferenceError}</StudioAlert> : null}
    {!visible.length ? <StudioCollectionEmptyState headingLevel={3} title={emptyTitle}
      description={snapshot.failures.length ? "Attention data is incomplete." : filter === "all" ? "No items need attention." : undefined} /> : groupAttentionItems(visible).map(group =>
      <section key={`${group[0].contributorId}:${group[0].id}`}>
        <h4>{group[0].title}{group.length > 1 ? ` (${group.length} related)` : ""}</h4>
        {group.map(item => <article key={`${item.contributorId}:${item.id}`} data-severity={item.severity}>
          <div className="attention-widget-source">
            <StatusChip tone={severities[item.severity].tone}>{severities[item.severity].label}</StatusChip>
            <span>{item.contributorName}</span>
          </div>
          <p>{item.summary}</p>
          <a href={qualifyStudioBackendPath(item.destination.path)}>{item.destination.label}</a>
        </article>)}
        <StudioField label="Snooze"><select defaultValue="" onChange={event => { if (event.target.value) applySnooze(group, Number(event.target.value)); }}>
          <option value="">Choose…</option><option value="3600000">1 hour</option><option value="86400000">1 day</option><option value="604800000">1 week</option>
        </select></StudioField>
      </section>)}
  </div>;
}
