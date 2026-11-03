import React, { useId } from "react";
import { useTablistKeyboard } from "../layout/Tabs";
import { type DisclosureControlProps } from "./types";

export type ExpressionMode = "fixed" | "expression";

export interface ExpressionToggleProps extends DisclosureControlProps {
  value: ExpressionMode;
  onChange(value: ExpressionMode): void;
  isNonDefault?: boolean;
  label?: React.ReactNode;
  children?: React.ReactNode;
}

const modes: { id: ExpressionMode; label: string }[] = [
  { id: "fixed", label: "Fixed" },
  { id: "expression", label: "Expression" }
];

export function isExpressionToggleVisible(props: Pick<ExpressionToggleProps, "value" | "forceOpen" | "isNonDefault">): boolean {
  return props.value === "expression" || Boolean(props.forceOpen || props.isNonDefault);
}

export function ExpressionToggle({
  value,
  onChange,
  isNonDefault,
  forceOpen,
  label,
  children
}: ExpressionToggleProps) {
  const baseId = useId();
  const visible = isExpressionToggleVisible({ value, forceOpen, isNonDefault });
  const onKeyDown = useTablistKeyboard(modes.map(mode => mode.id), value, id => onChange(id as ExpressionMode));

  return (
    <div className="studio-expression-field" data-mode={value} data-visible={visible ? "true" : "false"}>
      <div className="studio-expression-field-header">
        {label ? <span id={`${baseId}-label`} className="studio-expression-field-label">{label}</span> : null}
        <div
          className="studio-expression-toggle"
          role="tablist"
          aria-label="Value mode"
          data-visible={visible ? "true" : "false"}
          onKeyDown={onKeyDown}
        >
          {modes.map((mode, index) => {
            const selected = mode.id === value;
            return (
              <button
                key={mode.id}
                id={`${baseId}-tab-${index}`}
                data-tab-id={mode.id}
                type="button"
                role="tab"
                aria-selected={selected}
                tabIndex={selected ? 0 : -1}
                className={selected ? "active" : undefined}
                onClick={() => onChange(mode.id)}
              >
                {mode.label}
              </button>
            );
          })}
        </div>
      </div>
      {React.Children.map(children, child => {
        if (!React.isValidElement<{ "aria-label"?: string; "aria-labelledby"?: string }>(child)) return child;
        if (!label || child.props["aria-label"] || child.props["aria-labelledby"]) return child;
        return React.cloneElement(child, { "aria-labelledby": `${baseId}-label` });
      })}
    </div>
  );
}
