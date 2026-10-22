import React, { useId, useRef, useState } from "react";
import { ChevronDown, ChevronRight, GripVertical, Search } from "lucide-react";
import type { ActivityCatalogItem } from "../workflowTypes";
import { resolveActivityIcon, resolveActivityIconFallback, resolveActivityIconTone } from "../activityIcons";
import { renderActivityIcon } from "../activityIconView";
import { getActivityDisplay } from "../workflowAdapter";
import { formatActivityVersion } from "../workflowFormatting";
import type { ActivityPaletteGroup } from "./editorTypes";

interface ActivityPalettePanelProps {
  paletteSearch: string;
  onSearchChange(value: string): void;
  groups: ActivityPaletteGroup[];
  expandedCategories: Set<string>;
  disabled?: boolean;
  onToggleCategory(category: string): void;
  onActivityClick(activity: ActivityCatalogItem): void;
  onActivityDragStart(event: React.DragEvent<HTMLButtonElement>, activity: ActivityCatalogItem): void;
  onActivityDragEnd(event: React.DragEvent<HTMLButtonElement>, activity: ActivityCatalogItem): void;
  onActivityPointerDown(event: React.PointerEvent<HTMLButtonElement>, activity: ActivityCatalogItem): void;
}

// The left-hand activity palette: a searchable, category-grouped tree of draggable/clickable activities.
// Pure view — every add/drag interaction is delegated back to the canvas via the handler props.
export function ActivityPalettePanel({
  paletteSearch,
  onSearchChange,
  groups,
  expandedCategories,
  disabled = false,
  onToggleCategory,
  onActivityClick,
  onActivityDragStart,
  onActivityDragEnd,
  onActivityPointerDown
}: ActivityPalettePanelProps) {
  const searching = paletteSearch.trim().length > 0;
  const treeId = useId();
  const treeRef = useRef<HTMLDivElement | null>(null);
  const [focusedItemKey, setFocusedItemKey] = useState<string | null>(null);
  const activityChoiceCounts = new Map<string, number>();
  const versionsByActivityAndLabel = new Map<string, Map<string, string[]>>();
  for (const activity of groups.flatMap(group => group.activities)) {
    const identity = activity.activityDefinitionId ?? activity.activityTypeKey;
    activityChoiceCounts.set(identity, (activityChoiceCounts.get(identity) ?? 0) + 1);
    const version = formatActivityVersion(activity.activityDefinitionVersion ?? activity.version);
    const versionsByLabel = versionsByActivityAndLabel.get(identity) ?? new Map<string, string[]>();
    versionsByLabel.set(version.short, [...(versionsByLabel.get(version.short) ?? []), version.full]);
    versionsByActivityAndLabel.set(identity, versionsByLabel);
  }
  const visibleItemKeys = groups.flatMap(group => {
    const categoryKey = `category:${group.category}`;
    const expanded = searching || expandedCategories.has(group.category);
    return expanded
      ? [categoryKey, ...group.activities.map(activity => `activity:${activity.activityVersionId}`)]
      : [categoryKey];
  });
  const activeItemKey = focusedItemKey && visibleItemKeys.includes(focusedItemKey)
    ? focusedItemKey
    : visibleItemKeys[0];
  const treeItems = () => Array.from(treeRef.current?.querySelectorAll<HTMLButtonElement>("[role='treeitem']") ?? []);
  const focusTreeItem = (index: number) => {
    const items = treeItems();
    items[Math.max(0, Math.min(index, items.length - 1))]?.focus();
  };
  const onTreeKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const items = treeItems();
    const current = event.target as HTMLButtonElement;
    const currentIndex = items.indexOf(current);
    if (currentIndex < 0) return;

    if (event.key === "ArrowDown") {
      event.preventDefault();
      focusTreeItem(currentIndex + 1);
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      focusTreeItem(currentIndex - 1);
    } else if (event.key === "Home") {
      event.preventDefault();
      focusTreeItem(0);
    } else if (event.key === "End") {
      event.preventDefault();
      focusTreeItem(items.length - 1);
    } else if (current.classList.contains("wf-palette-category-toggle") && event.key === "ArrowLeft") {
      if (current.getAttribute("aria-expanded") !== "true") return;
      event.preventDefault();
      onToggleCategory(current.dataset.category ?? "");
    } else if (current.classList.contains("wf-palette-category-toggle") && event.key === "ArrowRight") {
      event.preventDefault();
      if (current.getAttribute("aria-expanded") === "false") onToggleCategory(current.dataset.category ?? "");
      else focusTreeItem(currentIndex + 1);
    } else if (current.classList.contains("wf-palette-activity") && event.key === "ArrowLeft") {
      event.preventDefault();
      items.find(candidate => candidate.classList.contains("wf-palette-category-toggle") && candidate.dataset.category === current.dataset.category)?.focus();
    }
  };
  return (
    <div className="wf-palette-body">
      <label className="wf-palette-search">
        <Search size={14} aria-hidden="true" />
        <input
          type="search"
          value={paletteSearch}
          placeholder="Search activities"
          aria-label="Search activity palette"
          aria-controls={treeId}
          onChange={event => onSearchChange(event.target.value)}
          onKeyDown={event => {
            if (event.key !== "ArrowDown") return;
            event.preventDefault();
            focusTreeItem(0);
          }}
        />
      </label>
      <div ref={treeRef} id={treeId} className="wf-palette-list" role="tree" aria-label="Available activities" onKeyDown={onTreeKeyDown}>
        {groups.length === 0 ? (
          <p className="wf-muted wf-palette-empty">No matching activities.</p>
        ) : groups.map(group => {
        const expanded = searching || expandedCategories.has(group.category);
        const categoryKey = `category:${group.category}`;
        return (
          <div className="wf-palette-category" key={group.category}>
            <button
              type="button"
              className="wf-palette-category-toggle"
              role="treeitem"
              aria-level={1}
              aria-expanded={expanded}
              data-category={group.category}
              tabIndex={activeItemKey === categoryKey ? 0 : -1}
              onFocus={() => setFocusedItemKey(categoryKey)}
              onClick={() => onToggleCategory(group.category)}>
              {expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
              <span>{group.category}</span>
              <small>{group.activities.length}</small>
            </button>
            {expanded ? (
              <div className="wf-palette-activities" role="group">
                {group.activities.map(activity => {
                  const description = activity.description?.trim();
                  const descriptionId = description ? `wf-palette-description-${activity.activityVersionId}` : undefined;
                  const displayName = getActivityDisplay(activity);
                  const icon = resolveActivityIcon(activity);
                  const tone = resolveActivityIconTone(activity);
                  const activityKey = `activity:${activity.activityVersionId}`;
                  const version = formatActivityVersion(
                    activity.activityDefinitionVersion ?? activity.version);
                  const activityIdentity = activity.activityDefinitionId ?? activity.activityTypeKey;
                  const showVersion = (activityChoiceCounts.get(activityIdentity) ?? 0) > 1;
                  const versionLabel = `Version ${version.short}`;
                  const exactVersionLabel = `Exact version ${version.full}`;
                  const collidingVersions = versionsByActivityAndLabel.get(activityIdentity)?.get(version.short) ?? [];
                  const hash = version.hasHash ? version.full.slice(version.short.length + 1) : "";
                  const distinctHashes = collidingVersions
                    .filter(candidate => candidate.startsWith(`${version.short}+`))
                    .map(candidate => candidate.slice(version.short.length + 1));
                  let hashPrefixLength = Math.min(7, hash.length);
                  while (hashPrefixLength < hash.length && distinctHashes.some(candidate => candidate !== hash && candidate.startsWith(hash.slice(0, hashPrefixLength)))) {
                    hashPrefixLength += 1;
                  }
                  const chipVersion = collidingVersions.length > 1 && version.hasHash
                    ? `${version.short}+${hash.slice(0, hashPrefixLength)}`
                    : version.short;
                  return (
                    <button
                      type="button"
                      className="wf-palette-activity"
                      role="treeitem"
                      aria-level={2}
                      data-category={group.category}
                      tabIndex={activeItemKey === activityKey ? 0 : -1}
                      key={activity.activityVersionId}
                      disabled={disabled}
                      draggable={!disabled}
                      title={[description || getActivityDisplay(activity), versionLabel].filter(Boolean).join(" — ")}
                      aria-label={`${displayName}, ${exactVersionLabel}`}
                      aria-describedby={descriptionId}
                      onClick={() => onActivityClick(activity)}
                      onDragStart={event => onActivityDragStart(event, activity)}
                      onDragEnd={event => onActivityDragEnd(event, activity)}
                      onPointerDown={event => onActivityPointerDown(event, activity)}
                      onFocus={() => setFocusedItemKey(activityKey)}
                      onKeyDown={event => {
                        if (event.key !== "Enter" && event.key !== " ") return;
                        event.preventDefault();
                        onActivityClick(activity);
                      }}
                    >
                      <span className="wf-activity-icon" data-icon={icon} data-tone={tone} aria-hidden="true">
                        {renderActivityIcon(icon, resolveActivityIconFallback(activity))}
                      </span>
                      <span className="wf-palette-activity-text">
                        <strong>{displayName}</strong>
                        {description ? <small id={descriptionId}>{description}</small> : null}
                      </span>
                      {showVersion ? (
                        <span className="wf-palette-version" title={collidingVersions.length > 1 ? exactVersionLabel : versionLabel} aria-label={exactVersionLabel}>
                          v{chipVersion}
                        </span>
                      ) : null}
                      <GripVertical className="wf-palette-activity-grip" size={14} aria-hidden="true" />
                    </button>
                  );
                })}
              </div>
            ) : null}
          </div>
        );
      })}
      </div>
    </div>
  );
}
