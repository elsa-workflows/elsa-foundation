import { describe, expect, it, vi } from "vitest";
import EventBus from "diagram-js/lib/core/EventBus";
import ElementRegistry from "diagram-js/lib/core/ElementRegistry";
import CommandStack from "diagram-js/lib/command/CommandStack";
import UpdateModdlePropertiesHandler from "bpmn-js/lib/features/modeling/cmd/UpdateModdlePropertiesHandler";
import { collectBpmnNativeCommandGroups, mapBpmnNativeCommandGroup, type NativeCommandEvents, type NativeCommandGroup } from "../bpmnDocumentCommands";
import { authoring, modeled, owner, rawXml } from "./bpmnDocumentFixture";

async function commands(source = owner()) {
  const native = await modeled(source), events = new EventBus();
  const stack = new CommandStack(events, {} as ConstructorParameters<typeof CommandStack>[1]);
  stack.register("element.updateModdleProperties", new UpdateModdlePropertiesHandler(new ElementRegistry(events)));
  const groups: NativeCommandGroup[] = [], refused = vi.fn();
  const detach = collectBpmnNativeCommandGroups(events as unknown as NativeCommandEvents, group => groups.push(group), refused);
  const process = native.definitions.rootElements.find((element: { id: string }) => element.id === "process");
  const work = process.flowElements.find((element: { id: string }) => element.id === "work");
  const end = process.flowElements.find((element: { id: string }) => element.id === "end");
  const execute = (element: typeof end, target: typeof end, properties: Record<string, unknown>) => {
    stack.execute("element.updateModdleProperties", { element: { id: element.id, businessObject: element }, moddleElement: target, properties });
    expect(refused).not.toHaveBeenCalled();
    return groups.at(-1)!;
  };
  const saved = async () => (await native.moddle.toXML(native.definitions)).xml as string;
  const map = async (group: NativeCommandGroup) => mapBpmnNativeCommandGroup(authoring, native.baseline, group, await saved());
  return { ...native, source, work, end, execute, groups, refused, stack, detach, saved, map };
}

describe("pinned modeled property intent snapshots", () => {
  it("admits the actual timer command and atomically refuses PT99M against captured PT5M", async () => {
    const h = await commands(), before = structuredClone(h.source);
    try {
      const expression = h.moddle.create("bpmn:FormalExpression", { body: "PT5M" });
      const timer = h.moddle.create("bpmn:TimerEventDefinition", { timeDuration: expression });
      const group = h.execute(h.end, h.end, { eventDefinitions: [timer] });
      const candidate = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(h.source, await h.map(group)));
      expect(candidate.byId.get("end")!.textContent).toBe("PT5M");
      expect(Object.isFrozen(group.actions[0].propertyIntents)).toBe(true);
      expect(Object.isFrozen(group.actions[0].propertyIntents![0])).toBe(true);
      expression.body = "PT99M";
      expect(group.actions[0].propertyIntents![0].xml).toContain("PT5M");
      await expect(h.map(group)).rejects.toThrow("completed command intent");
      expect(h.source).toEqual(before);
    } finally { h.detach(); }
  });

  it("qualifies nested loop aliases, formal expressions, default omission and neighboring changes", async () => {
    const h = await commands();
    try {
      const cardinality = h.moddle.create("bpmn:FormalExpression", { body: "3" });
      const loop = h.moddle.create("bpmn:MultiInstanceLoopCharacteristics", { isSequential: false, loopCardinality: cardinality });
      const group = h.execute(h.work, h.work, { loopCharacteristics: loop });
      expect(group.actions[0].propertyIntents![0].xml).toContain("loopCardinality");
      expect(await h.saved()).not.toContain('isSequential="false"');
      await expect(h.map(group)).resolves.toEqual(expect.any(Array));
      cardinality.body = "4";
      await expect(h.map(group)).rejects.toThrow("completed command intent");
      cardinality.body = "3"; h.end.name = "Unrequested";
      await expect(h.map(group)).rejects.toThrow(/unaccounted/);
    } finally { h.detach(); }
  });

  it("captures ordered reference lists on the actual idless owning property path", async () => {
    const source = owner(rawXml.replace('<bpmn:outgoing>q:flow</bpmn:outgoing>', '<bpmn:outgoing>q:flow</bpmn:outgoing><bpmn:ioSpecification><bpmn:dataInput id="a"/><bpmn:dataInput id="b"/><bpmn:inputSet><bpmn:dataInputRefs>a</bpmn:dataInputRefs></bpmn:inputSet><bpmn:outputSet/></bpmn:ioSpecification>'));
    const h = await commands(source);
    try {
      const [a, b] = h.work.ioSpecification.dataInputs, target = h.work.ioSpecification.inputSets[0];
      const values = [b, a], group = h.execute(h.work, target, { dataInputRefs: values });
      expect(group.actions[0].moddlePath).toEqual(["ioSpecification", "inputSets"]);
      await expect(h.map(group)).resolves.toEqual(expect.any(Array));
      values.reverse();
      await expect(h.map(group)).rejects.toThrow("completed command intent");
    } finally { h.detach(); }
  });

  it("qualifies the complete newly referenced global before admitting its insertion", async () => {
    const h = await commands();
    try {
      const message = h.moddle.create("bpmn:Message", { id: "new-message", name: "Expected" });
      h.definitions.rootElements.push(message); message.$parent = h.definitions;
      const definition = h.moddle.create("bpmn:MessageEventDefinition", { messageRef: message });
      const group = h.execute(h.end, h.end, { eventDefinitions: [definition] });
      const candidate = authoring.readBpmnDocument(authoring.applyBpmnDocumentCommands(h.source, await h.map(group)));
      expect(candidate.byId.get("new-message")!.getAttribute("name")).toBe("Expected");
      message.name = "Unexpected";
      await expect(h.map(group)).rejects.toThrow("new referenced global");
    } finally { h.detach(); }
  });

  it.each(["q", "previouslyUnbound"])("retains captured namespace meaning for an aliased nested formal expression (%s)", async prefix => {
    const h = await commands();
    try {
      const expression = h.moddle.create("bpmn:FormalExpression", { body: `${prefix}:Decision` });
      if (prefix === "q") expression.$attrs["xmlns:q"] = "urn:expression";
      const definition = h.moddle.create("bpmn:TimerEventDefinition", { timeDuration: expression });
      const group = h.execute(h.end, h.end, { eventDefinitions: [definition] });
      await expect(h.map(group)).resolves.toEqual(expect.any(Array));
      expression.$attrs[`xmlns:${prefix}`] = "urn:changed";
      await expect(h.map(group)).rejects.toThrow(/intent|namespace/);
    } finally { h.detach(); }
  });

  it("requires captured payload for object, nullable child and property-name-only direct contracts", async () => {
    const h = await commands();
    try {
      const timer = h.moddle.create("bpmn:TimerEventDefinition", { timeDuration: h.moddle.create("bpmn:FormalExpression", { body: "PT5M" }) });
      const group = h.execute(h.end, h.end, { eventDefinitions: [timer] });
      for (const properties of [{ eventDefinitions: null }, { eventDefinitions: [timer] }, undefined]) {
        const unqualified = { ...group, actions: [{ ...group.actions[0], properties, propertyIntents: undefined }] };
        await expect(h.map(unqualified)).rejects.toThrow(/intent snapshot/);
      }
      const cleared = h.execute(h.end, h.end, { eventDefinitions: null });
      await expect(h.map(cleared)).resolves.toEqual(expect.any(Array));
    } finally { h.detach(); }
  });

  it("refuses opaque modeled objects at the real command boundary", async () => {
    const h = await commands();
    try {
      const timer = h.moddle.create("bpmn:TimerEventDefinition"); timer.$attrs["vendor:flag"] = "unqualified";
      h.stack.execute("element.updateModdleProperties", { element: { id: "end", businessObject: h.end }, moddleElement: h.end, properties: { eventDefinitions: [timer] } });
      expect(h.refused).toHaveBeenCalledOnce(); expect(h.groups).toHaveLength(0);
      expect(h.refused.mock.calls[0][0].message).toContain("opaque property intent");
    } finally { h.detach(); }
  });
});
