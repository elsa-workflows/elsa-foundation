import React from "react";

/** @type {import("@elsa-workflows/studio-sdk").StudioTriggerConfigurationEditorContribution} */
const editor = {
  id: "acme.standalone-editor", typeKey: "acceptance-event", schemaVersion: "1",
  component: ({ configuration, disabled, findings, onChange }) => React.createElement("section", { "aria-label": "Acme Event configuration" },
    React.createElement("h3", null, "Acme Event configuration"),
    React.createElement("label", null, "Topic", React.createElement("input", { "aria-label": "Acme topic", disabled,
      value: typeof configuration.topic === "string" ? configuration.topic : "",
      onChange: event => onChange({ ...configuration, topic: event.currentTarget.value }) })),
    React.createElement("p", null, "A separately built module edits configuration through the public Studio SDK."),
    findings.length ? React.createElement("ul", null, ...findings.map((finding, index) => React.createElement("li", { key: index }, finding.message))) : null)
};

/** @param {import("@elsa-workflows/studio-sdk").ElsaStudioModuleApi} api */
export function register(api) {
  if (!api.triggerConfigurationEditors) throw new Error("This host does not provide the public trigger configuration editor Slot.");
  api.triggerConfigurationEditors.add(editor);
}
