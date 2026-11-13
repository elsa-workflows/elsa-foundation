import { describe, expect, it, vi } from "vitest";
import EventBus from "diagram-js/lib/core/EventBus";
import CommandStack from "diagram-js/lib/command/CommandStack";
import { create } from "diagram-js/lib/model";
import BpmnFactory from "bpmn-js/lib/features/modeling/BpmnFactory";
import LabelEditingProvider from "bpmn-js/lib/features/label-editing/LabelEditingProvider";
import UpdateLabelHandler from "bpmn-js/lib/features/label-editing/cmd/UpdateLabelHandler";
import { collectBpmnNativeCommandGroups, mapBpmnNativeCommandGroup, type NativeCommandEvents, type NativeCommandGroup } from "../bpmnDocumentCommands";
import { authoring, modeled, owner, rawXml } from "./bpmnDocumentFixture";

describe("pinned nullable label intent", () => {
  it.each(["work", "note", "group"])("distinguishes real editor removal from an explicit empty %s label", async elementId => {
    const source = owner(rawXml.replace('<bpmn:message id="message"', '<bpmn:category id="category"><bpmn:categoryValue id="category-value" value="Before"/></bpmn:category><bpmn:message id="message"')
      .replace('</bpmn:process>', '<bpmn:textAnnotation id="note"><bpmn:text>Before</bpmn:text></bpmn:textAnnotation><bpmn:group id="group" categoryValueRef="category-value"/></bpmn:process>'));
    for (const [text, editor] of [["", true], ["   ", true], ["", false]] as const) {
      const h = await modeled(source), events = new EventBus(), groups: NativeCommandGroup[] = [], refused = vi.fn();
      const stack = new CommandStack(events, {} as ConstructorParameters<typeof CommandStack>[1]);
      const modeling = { updateLabel(element: unknown, newLabel: string | null, newBounds?: unknown) { stack.execute("element.updateLabel", { element, newLabel, newBounds }); }, resizeShape: vi.fn() };
      stack.register("element.updateLabel", new UpdateLabelHandler(modeling as unknown as ConstructorParameters<typeof UpdateLabelHandler>[0], {} as ConstructorParameters<typeof UpdateLabelHandler>[1], new BpmnFactory(h.moddle)));
      const businessObject = h.definitions.rootElements.find((element: { id: string }) => element.id === "process").artifacts?.find((element: { id: string }) => element.id === elementId)
        ?? h.definitions.rootElements.find((element: { id: string }) => element.id === "process").flowElements.find((element: { id: string }) => element.id === elementId);
      const shape = create("shape", { id: elementId, type: businessObject.$type, businessObject, x: 10, y: 10, width: 100, height: 60 });
      const detach = collectBpmnNativeCommandGroups(events as unknown as NativeCommandEvents, group => groups.push(group), refused);
      try {
        if (editor) Reflect.apply(LabelEditingProvider.prototype.update, { _modeling: modeling, _canvas: { getAbsoluteBBox: () => ({ width: 100, height: 60 }) } }, [shape, text, "Before", { width: 100, height: 60 }]);
        else modeling.updateLabel(shape, text, null);
        expect(refused).not.toHaveBeenCalled(); expect(groups).toHaveLength(1);
        expect(groups[0].actions[0].newLabel).toBe(editor ? null : "");
        const next = (await h.moddle.toXML(h.definitions)).xml, commands = mapBpmnNativeCommandGroup(authoring, h.baseline, groups[0], next);
        const candidate = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(source, commands));
        const target = candidate.byId.get(elementId === "group" ? "category-value" : elementId)!;
        expect(elementId === "note" ? target.children.length ? target.children[0].textContent : null : target.getAttribute(elementId === "group" ? "value" : "name")).toBe(editor ? null : "");
        for (const newLabel of [undefined, editor ? "" : null]) expect(() => mapBpmnNativeCommandGroup(authoring, h.baseline, { ...groups[0], actions: [{ ...groups[0].actions[0], newLabel }] }, next)).toThrow(/intent|text/);
      } finally { detach(); }
    }
  });
});
