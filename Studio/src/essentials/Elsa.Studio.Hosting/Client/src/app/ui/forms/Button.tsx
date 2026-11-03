import React from "react";
import "./Button.css";

export type StudioButtonVariant = "primary" | "default" | "ghost";
export type StudioButtonSize = "sm" | "md";

/**
 * Thin wrapper over a native `<button>` that applies the shared `.studio-button`
 * treatment. `variant` and `size` are surfaced as data-attributes so all styling stays
 * in `Button.css` against `--studio-*` tokens; under material themes the `primary`
 * variant automatically adopts the per-theme `--studio-material-send-*` recipe. The
 * stylesheet is imported here so designer and fixture entries that never load the
 * host `styles.css` still get the filled primary.
 */
export function StudioButton({
  variant = "default",
  size = "md",
  className,
  type = "button",
  ...rest
}: {
  variant?: StudioButtonVariant;
  size?: StudioButtonSize;
} & React.ComponentPropsWithRef<"button">) {
  return (
    <button
      type={type}
      className={className ? `studio-button ${className}` : "studio-button"}
      data-variant={variant}
      data-size={size}
      {...rest}
    />
  );
}
