import { useState, type ReactNode } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { Copy, Workflow } from "lucide-react";
import { useWorkflowActivities } from "./api/activityDesign";
import { findContractType } from "./activityContractAuthoring";
import type {
  ActivityAuthoringCapabilities,
  ActivityContract,
  ActivityDefinitionVersionView,
  ActivityInputContract,
  ActivityOutcomeContract,
  ActivityOutputContract,
  ActivityTypeReference
} from "./activityDefinitionTypes";
import { copyTextToClipboard } from "./workflow-editor/editorHelpers";
import "./clrActivityDefinitionOverview.css";

export type ClrOverviewPort = {
  kind: "input" | "output" | "outcome";
  referenceKey: string;
};

type ValuePort = ActivityInputContract | ActivityOutputContract;

export function ClrActivityDefinitionOverview({ context, version, contract, capabilities, onOpenPort }: {
  context: StudioEndpointContext;
  version: ActivityDefinitionVersionView;
  contract: ActivityContract;
  capabilities?: ActivityAuthoringCapabilities;
  onOpenPort(port: ClrOverviewPort): void;
}) {
  const [copyFeedback, setCopyFeedback] = useState("");
  const { definition } = version;
  const definitionDescription = definition.description?.trim();
  const catalog = useWorkflowActivities(context, !definitionDescription);
  const catalogEntry = catalog.data?.activities.find(item => item.activityVersionId === version.versionId);
  const description = definitionDescription || catalogEntry?.description?.trim() || catalogEntry?.paletteHints?.description?.trim();
  const activityName = definition.displayName.trim() || definition.activityTypeKey;
  const copySignature = async () => {
    setCopyFeedback("");
    try {
      await copyTextToClipboard(formatSignature(definition.activityTypeKey, contract));
      setCopyFeedback("Signature copied.");
    } catch {
      setCopyFeedback("Could not copy the signature.");
    }
  };
  const renderValue = (port: ValuePort) => {
    const alias = findContractType(capabilities, port.type.alias)?.displayName ?? port.type.alias;
    return <>{formatType(port.type, alias, port.isNullable)}{port.isRequired ? " · Required" : ""}</>;
  };

  return <div className="ad-clr-overview">
    <header className="ad-clr-overview-header">
      <div>
        {description ? <p>{description}</p> : null}
        <p className="ad-clr-source">Implemented in .NET · Read-only in Studio</p>
      </div>
      <StudioButton type="button" variant="default" size="sm" onClick={() => void copySignature()}>
        <Copy size={14} aria-hidden="true" /> Copy signature
      </StudioButton>
    </header>
    <div className="ad-clr-schematic" role="group" aria-label="Activity public interface">
      <PortGroup title="Inputs" kind="input" members={contract.inputs} renderAnnotation={renderValue} onOpenPort={onOpenPort} />
      <div className="ad-clr-activity">
        <Workflow size={22} aria-hidden="true" />
        <strong>{activityName}</strong>
      </div>
      <div className="ad-clr-outgoing">
        <PortGroup title="Outputs" kind="output" members={contract.outputs} renderAnnotation={renderValue} onOpenPort={onOpenPort} />
        <PortGroup title="Outcomes" kind="outcome" members={contract.outcomes} renderAnnotation={outcome => outcome.isEmitted ? null : "Not emitted"} onOpenPort={onOpenPort} />
      </div>
    </div>
    <p className="ad-clr-copy-feedback" role="status">{copyFeedback}</p>
  </div>;
}

function PortGroup<T extends ValuePort | ActivityOutcomeContract>({ title, kind, members, renderAnnotation, onOpenPort }: {
  title: string;
  kind: ClrOverviewPort["kind"];
  members: T[];
  renderAnnotation(member: T): ReactNode;
  onOpenPort(port: ClrOverviewPort): void;
}) {
  return <section className={`ad-clr-port-group is-${kind}`} aria-label={title}>
    <h2>{title}</h2>
    {members.length ? <ul>{members.map(member => {
      const name = member.name || member.referenceKey;
      return <li key={member.referenceKey}>
        <button type="button" onClick={() => onOpenPort({ kind, referenceKey: member.referenceKey })}>
          <code>{name}</code>
          <span>{renderAnnotation(member)}</span>
        </button>
      </li>;
    })}</ul> : <p>None</p>}
  </section>;
}

function formatType(type: ActivityTypeReference, alias: string, nullable: boolean) {
  const kind = type.collectionKind?.toLowerCase();
  const collection = !kind || kind === "single" ? alias
    : kind === "dictionary" ? `Dictionary<string, ${alias}>`
      : `${type.collectionKind}<${alias}>`;
  return nullable && !collection.endsWith("?") ? `${collection}?` : collection;
}

function formatSignature(activityTypeKey: string, contract: ActivityContract) {
  const value = (port: ValuePort) => `${port.name || port.referenceKey}: ${formatType(port.type, port.type.alias, port.isNullable)}${port.isRequired ? " (required)" : ""}`;
  const input = (port: ActivityInputContract) => {
    if (!port.default) return value(port);
    const { syntax, value: defaultValue } = port.default;
    const literal = JSON.stringify(defaultValue);
    return `${value(port)} = ${syntax.toLowerCase() === "literal" ? literal : `${syntax}(${literal})`}`;
  };
  return [
    `activity ${activityTypeKey}`,
    "  inputs",
    ...contract.inputs.map(port => `    ${input(port)}`),
    "  outputs",
    ...contract.outputs.map(port => `    ${value(port)}`),
    "  outcomes",
    ...contract.outcomes.map(port => `    ${port.name || port.referenceKey}${port.isEmitted ? "" : " (not emitted)"}`)
  ].join("\n");
}
