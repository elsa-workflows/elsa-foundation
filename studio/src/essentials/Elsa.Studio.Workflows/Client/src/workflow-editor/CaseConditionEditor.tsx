import { useEffect, useRef } from "react";
import type { StudioActivityDescriptor } from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel, type ActivityPropertiesPanelProps } from "../ActivityPropertiesPanel";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { caseConditionInputKey, setCollectionCondition, type CollectionSlot } from "../collectionSlots";

/** Uses the same wire bridge, typed input row and expression tools as ordinary activity inputs. */
export function CaseConditionEditor({ collection, item, ...props }: Omit<ActivityPropertiesPanelProps, "descriptor"> & {
  collection: CollectionSlot;
  item: Record<string, unknown>;
}) {
  const live = useRef(true);
  const latest = useRef(props);
  latest.current = props;
  useEffect(() => { live.current = true; return () => { live.current = false; }; }, []);
  const caseId = item[collection.itemIdProperty!] as string;
  const key = caseConditionInputKey(caseId);
  const wire = { ...props.activity, inputs: [{ ...(item[collection.conditionProperty!] as Record<string, unknown>), referenceKey: key }], outputs: [], structure: null };
  const expanded = expandStateFromWire({ rootActivity: wire }).rootActivity!;
  const activity = { ...props.activity, [key]: expanded[key] };
  const descriptor: StudioActivityDescriptor = {
    typeName: "Switch", inputs: [{ name: key, referenceKey: key, displayName: "Condition", typeName: "Boolean",
      isWrapped: true, isBrowsable: true, tier: "Essential", defaultValue: false, defaultSyntax: "Literal",
      description: "Run this case when the expression evaluates to true." }], outputs: [], ports: []
  };
  return <ActivityPropertiesPanel {...props} activity={activity} descriptor={descriptor} showHeading={false}
    expressionDescriptors={props.expressionDescriptors.filter(syntax => syntax.type.toLowerCase() !== "secret")}
    onChange={next => {
      if (!live.current || latest.current.readOnly || latest.current.activity.nodeId !== next.nodeId) return;
      // Only the projected property belongs to the case; never persist a synthetic ordinary input.
      const serialized = canonicalizeStateForWire({ rootActivity: { nodeId: next.nodeId, activityVersionId: next.activityVersionId,
        inputs: [], outputs: [], [key]: next[key] } }).rootActivity!;
      const state = serialized.inputs.find(input => !!input && typeof input === "object" && "referenceKey" in input && input.referenceKey === key);
      const updated = setCollectionCondition(latest.current.activity, collection, caseId, state);
      latest.current = { ...latest.current, activity: updated };
      latest.current.onChange(updated);
    }} />;
}
