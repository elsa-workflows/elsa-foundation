import React, { Children, cloneElement, forwardRef, isValidElement, useId } from "react";

type ControlAttributes = {
  id: string;
  "aria-describedby"?: string;
  "aria-invalid"?: true;
  "data-studio-field-control": "";
  disabled?: boolean;
};

type FieldProps = Omit<React.HTMLAttributes<HTMLDivElement>, "children"> & {
  label: React.ReactNode;
  hint?: React.ReactNode;
  error?: React.ReactNode;
  meta?: React.ReactNode;
  controlId?: string;
  labelId?: string;
  disabled?: boolean;
  leading?: React.ReactNode;
  trailing?: React.ReactNode;
  after?: React.ReactNode;
  className?: string;
};

/** One native control. A real legend gap also works on transparent and textured surfaces. */
export const StudioFormField = forwardRef<HTMLDivElement, FieldProps & {
  children(attributes: ControlAttributes): React.ReactNode;
}>(function StudioFormField({ label, hint, error, meta, controlId, labelId, disabled, leading, trailing, after, className, children, ...rest }, ref) {
  const generatedId = useId();
  const id = controlId ?? generatedId;
  const descriptionId = hint || error ? `${id}-help` : undefined;
  return <div {...rest} ref={ref} className={`studio-field studio-form-field${className ? ` ${className}` : ""}`} data-invalid={Boolean(error)} data-disabled={Boolean(disabled)}>
    <fieldset className="studio-form-control" role="presentation">
      <legend className="studio-form-legend">
        <label id={labelId} htmlFor={id} className="studio-field-label">{label}</label>
        {meta ? <span className="studio-field-meta">{meta}</span> : null}
      </legend>
      <div className="studio-form-body" data-leading={Boolean(leading)} data-trailing={Boolean(trailing)}>
        {leading ? <div className="studio-form-adornment">{leading}</div> : null}
        <div className="studio-form-value">{children({ id, "aria-describedby": descriptionId, "aria-invalid": error ? true : undefined, "data-studio-field-control": "", disabled })}</div>
        {trailing ? <div className="studio-form-adornment">{trailing}</div> : null}
      </div>
    </fieldset>
    {descriptionId ? <small id={descriptionId} role={error ? "alert" : undefined} className={error ? "studio-field-error" : "studio-field-hint"}>{error || hint}</small> : null}
    {after}
  </div>;
});

type NativeProps = React.HTMLAttributes<HTMLElement> & { id?: string; type?: string; disabled?: boolean; children?: React.ReactNode; "data-studio-field-control"?: string };
type NativeControl = React.ReactElement<NativeProps>;
const choices = new Set(["checkbox", "radio", "hidden", "file", "range", "color", "button", "submit", "reset", "image"]);

function collectControls(children: React.ReactNode, result: NativeControl[]) {
  Children.forEach(children, child => {
    if (!isValidElement<NativeProps>(child)) return;
    if (typeof child.type === "string" && ["input", "select", "textarea", "output"].includes(child.type)) result.push(child);
    // Opaque contributed components and nested labels own their control tree.
    else if (child.type === React.Fragment || typeof child.type === "string" && child.type !== "label") collectControls(child.props.children, result);
  });
}

function containsControl(child: React.ReactNode, target: NativeControl): boolean {
  if (child === target) return true;
  if (!isValidElement<NativeProps>(child)) return false;
  let found = false;
  Children.forEach(child.props.children, item => { if (containsControl(item, target)) found = true; });
  return found;
}

function decorateControl(children: React.ReactNode, target: NativeControl, attributes: ControlAttributes): React.ReactNode {
  return Children.map(children, child => {
    if (child === target) {
      const descriptions = [...new Set(`${target.props["aria-describedby"] ?? ""} ${attributes["aria-describedby"] ?? ""}`.split(/\s+/).filter(Boolean))].join(" ") || undefined;
      return cloneElement(target, { ...attributes, "aria-describedby": descriptions, "aria-invalid": attributes["aria-invalid"] ?? target.props["aria-invalid"] });
    }
    if (isValidElement<NativeProps>(child) && containsControl(child, target)) return cloneElement(child, {}, decorateControl(child.props.children, target, attributes));
    return child;
  });
}

/** Compatible native-child API; composite groups retain their individually named controls. */
export const StudioField = forwardRef<HTMLDivElement, Omit<FieldProps, "hint"> & {
  description?: React.ReactNode;
  children: React.ReactNode;
}>(function StudioField({ label, description, meta, children, controlId, labelId, disabled, error, className, leading, trailing, after, ...rest }, ref) {
  const generatedId = useId();
  const controls: NativeControl[] = [];
  collectControls(children, controls);
  if (controls.length === 1 && controls[0].type === "input" && choices.has(controls[0].props.type ?? "text")) {
    const choice = controls[0];
    const id = choice.props.id ?? controlId ?? generatedId;
    const descriptionId = description || error ? `${id}-help` : undefined;
    const attributes: ControlAttributes = { id, "aria-describedby": descriptionId, "aria-invalid": error ? true : undefined, "data-studio-field-control": "", disabled: disabled ?? choice.props.disabled };
    return <div {...rest} ref={ref} className={`studio-field studio-choice-field${className ? ` ${className}` : ""}`}>
      <div className="studio-choice-control">
        {Children.map(children, child => containsControl(child, choice) ? decorateControl(child, choice, attributes) : null)}
        <label id={labelId} htmlFor={id}>{label}</label>{meta}
      </div>
      {Children.map(children, child => containsControl(child, choice) ? null : child)}
      {descriptionId ? <small id={descriptionId} role={error ? "alert" : undefined}>{error || description}</small> : null}
      {after}
    </div>;
  }
  const control = controls.length === 1 && !(controls[0].type === "input" && choices.has(controls[0].props.type ?? "text")) ? controls[0] : undefined;
  if (!control && controls.length === 0 && controlId) {
    const isMessage = (child: React.ReactNode) => isValidElement(child) && (child.type === "small" || child.type === "p");
    const messages = Children.map(children, child => isMessage(child) ? child : null);
    return <StudioFormField {...rest} ref={ref} label={label} hint={description} error={error} meta={meta} controlId={controlId} labelId={labelId}
      disabled={disabled} className={className} leading={leading} trailing={trailing} after={<>{messages}{after}</>}>
      {() => Children.map(children, child => isMessage(child) ? null : child)}
    </StudioFormField>;
  }
  if (control) {
    const messageIds: string[] = [];
    const messages = Children.map(children, (child, index) => {
      if (containsControl(child, control)) return null;
      if (isValidElement<NativeProps>(child) && (child.type === "small" || child.type === "p")) {
        const id = child.props.id ?? `${generatedId}-message-${index}`;
        messageIds.push(id);
        return cloneElement(child, { id });
      }
      return child;
    });
    return <StudioFormField {...rest} ref={ref} label={label} hint={description} error={error} meta={meta} controlId={control.props.id ?? controlId} labelId={labelId}
      disabled={disabled ?? control.props.disabled} className={className} leading={leading} trailing={trailing} after={<>{messages}{after}</>}>
      {attributes => Children.map(children, child => containsControl(child, control) ? decorateControl(child, control, { ...attributes, "aria-describedby": [attributes["aria-describedby"], ...messageIds].filter(Boolean).join(" ") || undefined }) : null)}
    </StudioFormField>;
  }
  const descriptionId = description || error ? `${generatedId}-help` : undefined;
  return <div {...rest} ref={ref} className={`studio-field studio-field-group${className ? ` ${className}` : ""}`}>
    <fieldset className="studio-form-control" aria-describedby={descriptionId}>
      <legend id={labelId} className="studio-form-legend"><span className="studio-field-label">{label}</span>{meta}</legend>
      <div className="studio-form-group-content">{children}</div>
    </fieldset>
    {descriptionId ? <small id={descriptionId} role={error ? "alert" : undefined} className={error ? "studio-field-error" : "studio-field-hint"}>{error || description}</small> : null}
    {after}
  </div>;
});
