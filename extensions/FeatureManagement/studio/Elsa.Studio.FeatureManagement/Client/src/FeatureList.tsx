import { useEffect, useRef, useState } from "react";
import { ChevronDown, FlaskConical, Settings2, ShieldAlert } from "lucide-react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import type { CategoryFilterItem, DraftFeature } from "./featureModel";
import { CategoryChips, FeatureBadge, SourceBadge } from "./FeatureBadges";

export function BulkFeatureActions({
  disabled,
  selectedCount,
  visibleCount,
  visibleSelectedCount,
  selectedEnabledCount,
  allVisibleChecked,
  onToggleVisible,
  onClearSelection,
  onEnableSelected,
  onDisableSelected
}: {
  disabled: boolean;
  selectedCount: number;
  visibleCount: number;
  visibleSelectedCount: number;
  selectedEnabledCount: number;
  allVisibleChecked: boolean;
  onToggleVisible(): void;
  onClearSelection(): void;
  onEnableSelected(): void;
  onDisableSelected(): void;
}) {
  return (
    <div className="feature-management-bulk-actions" aria-label="Bulk feature actions">
      <label>
        <input
          type="checkbox"
          checked={allVisibleChecked}
          disabled={disabled}
          onChange={onToggleVisible}
          aria-label={allVisibleChecked ? "Clear visible feature selection" : "Select visible features"}
        />
        <span>{visibleSelectedCount}/{visibleCount} visible</span>
      </label>
      <div>
        <span title={`${selectedEnabledCount} enabled`}>{selectedCount} selected</span>
        <StudioButton
          aria-label="Enable selected features"
          disabled={disabled || selectedCount === 0 || selectedEnabledCount === selectedCount}
          onClick={onEnableSelected}
        >
          Enable
        </StudioButton>
        <StudioButton
          aria-label="Disable selected features"
          disabled={disabled || selectedCount === 0 || selectedEnabledCount === 0}
          onClick={onDisableSelected}
        >
          Disable
        </StudioButton>
        <StudioButton aria-label="Clear selected features" disabled={disabled || selectedCount === 0} onClick={onClearSelection}>Clear</StudioButton>
      </div>
    </div>
  );
}

/**
 * Searchable category picker embedded in the feature list toolbar. Replaces the former
 * dedicated categories column: after picking a category the full-width list and inspector
 * get the reclaimed space, while the popover keeps every category (with counts) one click
 * away and filterable by text.
 */
export function CategorySelect({
  categories,
  selectedCategory,
  onSelect
}: {
  categories: CategoryFilterItem[];
  selectedCategory: string;
  onSelect(categoryId: string): void;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const containerRef = useRef<HTMLDivElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const selected = categories.find(category => category.id === selectedCategory) ?? categories[0];
  const normalizedQuery = query.trim().toLowerCase();
  const visibleCategories = normalizedQuery
    ? categories.filter(category => category.label.toLowerCase().includes(normalizedQuery))
    : categories;

  useEffect(() => {
    if (!open) {
      return;
    }

    function onPointerDown(event: PointerEvent) {
      if (containerRef.current && event.target instanceof Node && !containerRef.current.contains(event.target)) {
        setOpen(false);
        setQuery("");
      }
    }

    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open]);

  useEffect(() => {
    if (open) {
      searchRef.current?.focus();
    }
  }, [open]);

  function close() {
    triggerRef.current?.focus();
    setOpen(false);
    setQuery("");
  }

  return (
    <div
      className="feature-management-category-select"
      ref={containerRef}
      onKeyDown={event => {
        if (event.key === "Escape" && open) {
          event.preventDefault();
          close();
        }
      }}
    >
      <StudioButton
        ref={triggerRef}
        className="feature-management-category-trigger"
        aria-label="Filter by category"
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={() => (open ? close() : setOpen(true))}
      >
        <span>{selected?.label ?? "All categories"}</span>
        <em>{selected?.count ?? 0}</em>
        <ChevronDown size={14} aria-hidden="true" />
      </StudioButton>
      {open ? (
        <div className="feature-management-category-popover">
          <input
            ref={searchRef}
            type="search"
            aria-label="Search categories"
            placeholder="Search categories"
            value={query}
            onChange={event => setQuery(event.target.value)}
          />
          <div className="feature-management-category-options" role="listbox" aria-label="Feature categories">
            {visibleCategories.length === 0 ? <p className="feature-management-muted">No categories match.</p> : null}
            {visibleCategories.map(category => (
              <StudioButton
                key={category.id}
                type="button"
                role="option"
                aria-selected={category.id === selectedCategory}
                className={category.id === selectedCategory ? "active" : ""}
                onClick={() => {
                  onSelect(category.id);
                  close();
                }}
              >
                <span>{category.label}</span>
                <em>{category.count}</em>
              </StudioButton>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}

export function FeatureCard({
  feature,
  pending,
  selected,
  checked,
  disabled,
  onSelect,
  onToggle,
  onCheckedChange
}: {
  feature: DraftFeature;
  pending: boolean;
  selected: boolean;
  checked: boolean;
  disabled: boolean;
  onSelect(): void;
  onToggle(): void;
  onCheckedChange(checked: boolean): void;
}) {
  const displayName = feature.displayName || feature.id;

  return (
    <article className={selected ? "feature-management-card selected" : "feature-management-card"}>
      <input
        type="checkbox"
        className="feature-management-row-checkbox"
        checked={checked}
        disabled={disabled}
        aria-label={`Select ${feature.displayName || feature.id}`}
        onChange={event => onCheckedChange(event.target.checked)}
      />
      <StudioButton
        className={feature.enabled ? "feature-management-switch enabled" : "feature-management-switch"}
        onClick={onToggle}
        disabled={disabled}
        role="switch"
        aria-checked={feature.enabled}
        aria-label={`${feature.enabled ? "Disable" : "Enable"} ${feature.displayName || feature.id}`}
      />
      <StudioButton className="feature-management-feature-button" onClick={onSelect}>
        <span className="feature-management-card-title">
          <strong>{displayName}</strong>
          {feature.experimental ? <FeatureBadge tone="experimental"><FlaskConical size={11} />Experimental</FeatureBadge> : null}
          {feature.advanced ? <FeatureBadge tone="advanced"><ShieldAlert size={11} />Advanced</FeatureBadge> : null}
          {pending ? <FeatureBadge tone="info">Pending changes</FeatureBadge> : null}
        </span>
        {feature.description ? <small>{feature.description}</small> : null}
        <span className="feature-management-card-meta">
          <SourceBadge sourceKind={feature.sourceKind} />
          <CategoryChips categories={feature.categories} max={2} />
        </span>
      </StudioButton>
      <span className="feature-management-card-count" title={`${feature.settings.length} setting${feature.settings.length === 1 ? "" : "s"}`}>
        <Settings2 size={13} />
        {feature.settings.length}
      </span>
    </article>
  );
}
