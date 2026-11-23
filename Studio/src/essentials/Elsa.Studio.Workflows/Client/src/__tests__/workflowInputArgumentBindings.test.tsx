import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, expect, it } from "vitest";
import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import type { WrappedActivityInputValue } from "../activityProperties";
import { WorkflowInputArgumentBindings } from "../workflowInputArgumentBindings";
import type { WorkflowInput } from "../workflowTypes";

const workflowInput = {
  referenceKey: "activity-input-first-name",
  name: "FirstName",
  displayName: "First name"
} as WorkflowInput;

const activityInput = {
  name: "Value",
  typeName: "System.String",
  displayName: "Value"
} as StudioActivityInputDescriptor;

let unmount: (() => void) | undefined;
afterEach(() => unmount?.());

it("keeps an activity input selection when binding it as a JavaScript argument", () => {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  unmount = () => { flushSync(() => root.unmount()); container.remove(); };
  let changedValue: WrappedActivityInputValue | undefined;
  const wrapped: WrappedActivityInputValue = {
    typeName: "System.String",
    expression: { type: "JavaScript", value: "args.FirstName" }
  };

  flushSync(() => root.render(
    <WorkflowInputArgumentBindings
      inputs={[workflowInput]}
      inputBindingTarget="referenceKey"
      bindingMode="arguments"
      value="args.FirstName"
      syntax="JavaScript"
      activityInput={activityInput}
      wrapped={wrapped}
      disabled={false}
      expanded
      bindingsId="activity-input-bindings"
      onChange={value => { changedValue = value; }}
      onExpandedChange={() => {}}
      onEscape={() => {}}
    />
  ));

  const select = container.querySelector<HTMLSelectElement>("select")!;
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set?.call(select, "FirstName");
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });

  expect(changedValue?.expression.value).toMatchObject({
    parameters: { FirstName: { kind: "WorkflowRequest", memberKey: "activity-input-first-name" } }
  });
});