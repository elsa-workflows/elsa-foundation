import type { ReactNode } from "react";

/**
 * Shared disclosure contract from the progressive-disclosure plan (§1.2).
 *
 * `persistKey` is stored under `elsa-studio.ui.*` via {@link useDisclosureState}.
 * `forceOpen` is the never-hide-state switch: a non-default value, binding or
 * validation error must stay visible regardless of the user's last collapse.
 */
export interface DisclosureControlProps {
  persistKey?: string;
  forceOpen?: boolean;
}

export function shouldForceReveal(flags: {
  forceOpen?: boolean;
  hasError?: boolean;
  isNonDefault?: boolean;
  isBound?: boolean;
}): boolean {
  return Boolean(flags.forceOpen || flags.hasError || flags.isNonDefault || flags.isBound);
}

export interface DisclosureOption {
  id: string;
  label: string;
  description?: string;
  hasError?: boolean;
  isNonDefault?: boolean;
  isBound?: boolean;
}

export interface OverflowMenuAction {
  id: string;
  label: ReactNode;
  onSelect(): void;
  disabled?: boolean;
  destructive?: boolean;
  group?: string;
  title?: string;
  describedBy?: string;
  icon?: ReactNode;
  detail?: ReactNode;
  selection?: { type: "checkbox" | "radio"; checked: boolean };
  /** Toggle-style item rendered as `menuitemcheckbox`; unlike `selection`, activating it closes the menu. */
  checked?: boolean;
  /** Trailing current-value text shown alongside a `checked` item. */
  value?: ReactNode;
}

export type OverflowMenuItem = OverflowMenuAction | { type: "separator" };
