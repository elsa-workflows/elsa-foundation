import React, { useId } from "react";
import { useTablistKeyboard } from "../layout/Tabs";
import { type DisclosureControlProps } from "./types";
import { isDisclosureString, useDisclosureState } from "./useDisclosureState";

export interface PeerViewItem {
  id: string;
  label: React.ReactNode;
}

export interface PeerViewSwitchProps extends DisclosureControlProps {
  views: PeerViewItem[];
  value?: string;
  defaultValue?: string;
  onChange?(id: string): void;
  ariaLabel?: string;
}

export function PeerViewSwitch({
  views,
  value,
  defaultValue,
  onChange,
  persistKey,
  ariaLabel = "View"
}: PeerViewSwitchProps) {
  const baseId = useId();
  const fallback = defaultValue ?? views[0]?.id ?? "";
  const [stored, setStored] = useDisclosureState(persistKey, fallback, isDisclosureString);
  const active = value ?? stored;
  const ids = views.map(view => view.id);
  const onKeyDown = useTablistKeyboard(ids, active, select);

  function select(id: string) {
    setStored(id);
    onChange?.(id);
  }

  return (
    <div
      className="studio-peer-view-switch"
      role="tablist"
      aria-label={ariaLabel}
      onKeyDown={onKeyDown}
    >
      {views.map((view, index) => {
        const selected = view.id === active;
        return (
          <button
            key={view.id}
            id={`${baseId}-tab-${index}`}
            data-tab-id={view.id}
            type="button"
            role="tab"
            aria-selected={selected}
            tabIndex={selected ? 0 : -1}
            className={selected ? "active" : undefined}
            onClick={() => select(view.id)}
          >
            {view.label}
          </button>
        );
      })}
    </div>
  );
}
