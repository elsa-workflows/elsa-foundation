import React from "react";
import { ChevronDown, ChevronRight, GripVertical, Search } from "lucide-react";
import type { ActivityCatalogItem } from "../workflowTypes";
import { getActivityDisplay, resolveActivityIcon } from "../workflowAdapter";
import { renderActivityIcon } from "../workflowFormatting";
import type { ActivityPaletteGroup } from "./editorTypes";

interface ActivityPalettePanelProps {
  paletteSearch: string;
  onSearchChange(value: string): void;
  groups: ActivityPaletteGroup[];
  expandedCategories: Set<string>;
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
  onToggleCategory,
  onActivityClick,
  onActivityDragStart,
  onActivityDragEnd,
  onActivityPointerDown
}: ActivityPalettePanelProps) {
  const searching = paletteSearch.trim().length > 0;
  return (
    <div className="wf-palette-body">
      <label className="wf-palette-search">
        <Search size={14} aria-hidden="true" />
        <input
          type="search"
          value={paletteSearch}
          placeholder="Search activities"
          aria-label="Search activity palette"
          onChange={event => onSearchChange(event.target.value)}
        />
      </label>
      <div className="wf-palette-list" role="tree" aria-label="Available activities">
        {groups.length === 0 ? (
          <p className="wf-muted wf-palette-empty">No matching activities.</p>
        ) : groups.map(group => {
        const expanded = searching || expandedCategories.has(group.category);
        return (
          <div className="wf-palette-category" key={group.category}>
            <button
              type="button"
              className="wf-palette-category-toggle"
              role="treeitem"
              aria-expanded={expanded}
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
                  return (
                    <button
                      type="button"
                      className="wf-palette-activity"
                      role="treeitem"
                      key={activity.activityVersionId}
                      draggable
                      title={description || getActivityDisplay(activity)}
                      aria-describedby={descriptionId}
                      onClick={() => onActivityClick(activity)}
                      onDragStart={event => onActivityDragStart(event, activity)}
                      onDragEnd={event => onActivityDragEnd(event, activity)}
                      onPointerDown={event => onActivityPointerDown(event, activity)}
                    >
                      <span className="wf-activity-icon" data-icon={icon} aria-hidden="true">
                        {renderActivityIcon(icon)}
                      </span>
                      <span className="wf-palette-activity-text">
                        <strong>{displayName}</strong>
                        {description ? <small id={descriptionId}>{description}</small> : null}
                      </span>
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
