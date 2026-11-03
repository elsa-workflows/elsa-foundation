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

export type OverflowMenuItem =
  | {
      id: string;
      label: ReactNode;
      onSelect(): void;
      disabled?: boolean;
      destructive?: boolean;
    }
  | { type: "separator" };
