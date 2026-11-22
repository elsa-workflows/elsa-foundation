import React from "react";
import "./Button.css";

export type StudioButtonVariant = "primary" | "default" | "ghost" | "danger";
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

/** A controlled toggle with a stable accessible name in both states. */
export function StudioToggleButton({
  pressed,
  ...props
}: Omit<React.ComponentPropsWithRef<"button">, "aria-pressed"> & { pressed: boolean; size?: StudioButtonSize }) {
  return <StudioButton {...props} data-studio-toggle="" aria-pressed={pressed} />;
}

type StudioIconButtonProps = Omit<React.ComponentPropsWithoutRef<"button">, "aria-label" | "children"> & {
  "aria-label": string;
  children: React.ReactElement;
  variant?: Extract<StudioButtonVariant, "default" | "danger">;
};

export const StudioIconButton = React.forwardRef<HTMLButtonElement, StudioIconButtonProps>(function StudioIconButton({
  "aria-label": label,
  children,
  variant = "default",
  className,
  type = "button",
  title = label,
  ...rest
}, ref) {
  return (
    <button
      {...rest}
      ref={ref}
      type={type}
      className={className ? `studio-icon-button ${className}` : "studio-icon-button"}
      data-variant={variant}
      aria-label={label}
      title={title}
    >
      {children}
    </button>
  );
});
