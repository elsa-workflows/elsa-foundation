import React, { useId, useMemo, useRef, useState } from "react";
import { StudioButton } from "../forms/Button";
import { StudioSearchInput } from "../forms/SearchInput";
import { EmptyState } from "../feedback/FeedbackStates";
import { AnchoredPopover } from "../overlay/AnchoredPopover";
import { type DisclosureControlProps, type DisclosureOption, shouldForceReveal } from "./types";

const searchThreshold = 8;

export interface AddOptionItem extends DisclosureOption {
  added?: boolean;
}

export interface AddOptionProps extends DisclosureControlProps {
  options: AddOptionItem[];
  addedIds?: string[];
  onAdd?(id: string): void;
  onRemove?(id: string): void;
  renderField?(item: AddOptionItem): React.ReactNode;
  addLabel?: string;
  emptyLabel?: string;
  searchPlaceholder?: string;
  defaultPickerOpen?: boolean;
}

export function isAddOptionVisible(item: AddOptionItem, addedIds: ReadonlySet<string>, forceOpen?: boolean): boolean {
  return item.added === true
    || addedIds.has(item.id)
    || shouldForceReveal({ forceOpen, hasError: item.hasError, isNonDefault: item.isNonDefault, isBound: item.isBound });
}

export function AddOption({
  options,
  addedIds,
  onAdd,
  onRemove,
  renderField,
  addLabel = "+ Add option",
  emptyLabel = "No options set",
  searchPlaceholder = "Search options",
  defaultPickerOpen = false,
  forceOpen
}: AddOptionProps) {
  const labelId = useId();
  const listId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const [pickerOpen, setPickerOpen] = useState(defaultPickerOpen);
  const [query, setQuery] = useState("");
  const [internalAdded, setInternalAdded] = useState<string[]>([]);
  const resolvedAdded = useMemo(
    () => new Set(addedIds ?? internalAdded),
    [addedIds, internalAdded]
  );

  const visible = options.filter(item => isAddOptionVisible(item, resolvedAdded, forceOpen));
  const available = options.filter(item => !isAddOptionVisible(item, resolvedAdded, forceOpen));
  const searchable = available.length > searchThreshold;
  const filtered = available.filter(item => {
    if (!query.trim()) return true;
    const haystack = `${item.label} ${item.description ?? ""}`.toLowerCase();
    return haystack.includes(query.trim().toLowerCase());
  });

  function add(id: string) {
    if (!addedIds) setInternalAdded(current => current.includes(id) ? current : [...current, id]);
    onAdd?.(id);
    setPickerOpen(false);
    setQuery("");
    triggerRef.current?.focus();
  }

  function remove(id: string) {
    if (!addedIds) setInternalAdded(current => current.filter(item => item !== id));
    onRemove?.(id);
  }

  return (
    <div className="studio-add-option">
      {visible.length === 0 ? <EmptyState>{emptyLabel}</EmptyState> : (
        <ul className="studio-add-option-fields">
          {visible.map(item => (
            <li key={item.id} className="studio-add-option-field" data-error={item.hasError ? "true" : undefined}>
              <div className="studio-add-option-field-body">
                {renderField ? renderField(item) : <span className="studio-add-option-field-label">{item.label}</span>}
              </div>
              <button
                type="button"
                className="studio-add-option-remove"
                aria-label={`Remove ${item.label}`}
                onClick={() => remove(item.id)}
              >
                ×
              </button>
            </li>
          ))}
        </ul>
      )}

      {available.length > 0 ? (
        <>
          <StudioButton
            ref={triggerRef}
            variant="ghost"
            size="sm"
            className="studio-add-option-trigger"
            aria-haspopup="listbox"
            aria-expanded={pickerOpen}
            aria-controls={listId}
            onClick={() => setPickerOpen(current => !current)}
          >
            {addLabel}
          </StudioButton>
          <AnchoredPopover
            id={listId}
            anchorRef={triggerRef}
            open={pickerOpen}
            className="studio-add-option-popover"
            minWidth={220}
            onDismiss={() => {
              setPickerOpen(false);
              setQuery("");
            }}
          >
            <div className="studio-add-option-menu" role="listbox" aria-labelledby={labelId}>
              <span id={labelId} className="studio-add-option-menu-title">Add option</span>
              {searchable ? (
                <StudioSearchInput
                  value={query}
                  placeholder={searchPlaceholder}
                  aria-label={searchPlaceholder}
                  onChange={event => setQuery(event.target.value)}
                />
              ) : null}
              {filtered.length === 0 ? <p className="studio-add-option-empty">No matching options</p> : filtered.map(item => (
                <button
                  key={item.id}
                  type="button"
                  role="option"
                  className="studio-add-option-choice"
                  onClick={() => add(item.id)}
                >
                  <span>{item.label}</span>
                  {item.description ? <small>{item.description}</small> : null}
                </button>
              ))}
            </div>
          </AnchoredPopover>
        </>
      ) : null}
    </div>
  );
}
