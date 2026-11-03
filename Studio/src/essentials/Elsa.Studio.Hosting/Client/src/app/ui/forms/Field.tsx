import React from "react";

export function StudioField({
  label,
  description,
  meta,
  children
}: {
  label: React.ReactNode;
  description?: React.ReactNode;
  meta?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <label className="studio-field">
      <span className="studio-field-label">
        <strong>{label}</strong>
        {meta}
      </span>
      {description ? <small>{description}</small> : null}
      {children}
    </label>
  );
}
