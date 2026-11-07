import type { CSSProperties, ReactNode } from "react";
import { bpmnElementTypes, bpmnNotationType, isActivityBearingElementType, isEventElementType, isGatewayElementType, isSubProcessElementType } from "./bpmnTypes";
import { bpmnAppearanceLabelStyle, type BpmnDocumentAppearance } from "./bpmnDocumentAppearance";
import "./bpmnNotation.css";

/** Rendering facts only. Standard XML and its DI remain the authoring authority. */
export interface BpmnNotationDescriptor {
  type: string;
  name?: string;
  width?: number;
  height?: number;
  eventDefinitions?: readonly string[];
  parallelMultiple?: boolean;
  interrupting?: boolean;
  cancelActivity?: boolean;
  isExpanded?: boolean;
  eventSubProcess?: boolean;
  transaction?: boolean;
  loop?: "standard" | "parallel" | "sequential";
  compensation?: boolean;
  collection?: boolean;
  horizontal?: boolean;
  gatewayMarkerVisible?: boolean;
  eventGatewayType?: string;
  instantiate?: boolean;
  participantMultiplicity?: boolean;
  text?: string;
  appearance?: BpmnDocumentAppearance;
}

export interface BpmnNotationDiagram {
  width?: number;
  height?: number;
  isExpanded?: boolean;
  isHorizontal?: boolean;
  isMarkerVisible?: boolean;
  appearance?: BpmnDocumentAppearance;
}

const modelNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
const shapeTypes: ReadonlySet<string> = new Set(Object.values(bpmnElementTypes));
const dataTypes = new Set(["dataObject", "dataObjectReference", "dataInput", "dataOutput"]);

export { bpmnNotationType } from "./bpmnTypes";

function booleanAttribute(element: Element, name: string): boolean | undefined {
  const value = element.getAttribute(name);
  return value === "true" || value === "1" ? true : value === "false" || value === "0" ? false : undefined;
}

function directChildren(element: Element) {
  return [...element.children].filter(child => child.namespaceURI === modelNamespace);
}

/** Read semantic markers from actual standard XML, never from an independently persisted view model. */
export function bpmnNotationFromElement(element: Element, diagram: BpmnNotationDiagram = {}): BpmnNotationDescriptor {
  const children = directChildren(element);
  const loop = children.find(child => child.localName === "standardLoopCharacteristics" || child.localName === "multiInstanceLoopCharacteristics");
  let collection = booleanAttribute(element, "isCollection");
  if (element.localName === "dataObjectReference" && element.hasAttribute("dataObjectRef")) {
    const reference = element.getAttribute("dataObjectRef");
    const object = [...element.ownerDocument.getElementsByTagNameNS(modelNamespace, "dataObject")].find(candidate => candidate.getAttribute("id") === reference);
    if (object) collection = booleanAttribute(object, "isCollection");
  }
  let name = element.getAttribute("name") ?? undefined;
  if (element.localName === "group" && !name) {
    const reference = element.getAttribute("categoryValueRef");
    name = [...element.ownerDocument.getElementsByTagNameNS(modelNamespace, "categoryValue")]
      .find(candidate => candidate.getAttribute("id") === reference)?.getAttribute("value") ?? undefined;
  }
  return {
    type: element.namespaceURI === modelNamespace ? element.localName : `unknown:${element.nodeName}`,
    name,
    width: diagram.width, height: diagram.height,
    eventDefinitions: children.filter(child => child.localName.endsWith("EventDefinition")).map(child => child.localName),
    parallelMultiple: booleanAttribute(element, "parallelMultiple"),
    interrupting: booleanAttribute(element, "isInterrupting"), cancelActivity: booleanAttribute(element, "cancelActivity"),
    isExpanded: diagram.isExpanded ?? (element.localName === "participant" ? element.hasAttribute("processRef") : undefined), eventSubProcess: booleanAttribute(element, "triggeredByEvent"),
    transaction: element.localName === "transaction",
    loop: !loop ? undefined : loop.localName === "standardLoopCharacteristics" ? "standard" : booleanAttribute(loop, "isSequential") ? "sequential" : "parallel",
    compensation: booleanAttribute(element, "isForCompensation"), collection,
    horizontal: diagram.isHorizontal, gatewayMarkerVisible: diagram.isMarkerVisible,
    eventGatewayType: element.getAttribute("eventGatewayType") ?? undefined,
    instantiate: booleanAttribute(element, "instantiate"),
    participantMultiplicity: children.some(child => child.localName === "participantMultiplicity"),
    text: children.find(child => child.localName === "text")?.textContent ?? undefined,
    appearance: diagram.appearance
  };
}

function words(type: string) { return type.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase(); }
function eventKind(value: string) {
  const kind = bpmnNotationType(value).replace(/EventDefinition$/, "");
  return kind === "compensate" ? "compensation" : kind;
}

export function bpmnNotationLabel(descriptor: BpmnNotationDescriptor) {
  const type = bpmnNotationType(descriptor.type);
  const definitions = descriptor.eventDefinitions ?? [];
  const details = [descriptor.name, words(type)];
  if (type === "textAnnotation" && descriptor.text) details.push(descriptor.text);
  if (isEventElementType(type)) {
    if (type === "boundaryEvent") details.push(descriptor.cancelActivity === false ? "non-interrupting" : "interrupting");
    if (type === "startEvent" && descriptor.interrupting === false) details.push("non-interrupting");
    if (definitions.length > 1) details.push(descriptor.parallelMultiple ? "parallel multiple" : "multiple");
    else if (definitions.length) details.push(words(eventKind(definitions[0])));
  }
  if (descriptor.eventSubProcess) details.push("event subprocess");
  if (descriptor.loop) details.push(descriptor.loop === "standard" ? "loop" : `${descriptor.loop} multi-instance`);
  if (descriptor.compensation) details.push("compensation activity");
  if (descriptor.collection) details.push("collection");
  if (type === "receiveTask" && descriptor.instantiate) details.push("instantiating");
  if (!shapeTypes.has(type) || descriptor.type.startsWith("unknown:")) details.push("unknown BPMN notation; content retained");
  return details.filter(Boolean).join(", ");
}

function size(descriptor: BpmnNotationDescriptor): [number, number] {
  const type = bpmnNotationType(descriptor.type);
  const defaults: [number, number] = isEventElementType(type) ? [40, 40] : isGatewayElementType(type) ? [52, 52]
    : type === "participant" || type === "lane" ? [400, 160] : dataTypes.has(type) ? [48, 64]
      : type === "dataStoreReference" ? [58, 60] : type === "textAnnotation" ? [140, 70] : [160, 90];
  return [descriptor.width && Number.isFinite(descriptor.width) && descriptor.width > 0 ? descriptor.width : defaults[0],
    descriptor.height && Number.isFinite(descriptor.height) && descriptor.height > 0 ? descriptor.height : defaults[1]];
}

function star(cx: number, cy: number, outer: number, inner: number, points: number) {
  return Array.from({ length: points * 2 }, (_, index) => {
    const angle = -Math.PI / 2 + index * Math.PI / points;
    const radius = index % 2 ? inner : outer;
    return `${cx + Math.cos(angle) * radius},${cy + Math.sin(angle) * radius}`;
  }).join(" ");
}

function regularPolygon(cx: number, cy: number, radius: number, sides: number) {
  return Array.from({ length: sides }, (_, index) => {
    const angle = -Math.PI / 2 + index * 2 * Math.PI / sides;
    return `${cx + Math.cos(angle) * radius},${cy + Math.sin(angle) * radius}`;
  }).join(" ");
}

// Independently drawn SVG primitives. BPMN notation and the pinned upstream renderer are the
// reference for rings, markers and fill semantics; no upstream path artwork is embedded here.
function EventSymbol({ kind, throwing, backgroundColor }: { kind: string; throwing: boolean; backgroundColor?: string }) {
  const fill = throwing ? "currentColor" : "none";
  const inverse = throwing ? "wf-bpmn-symbol-inverse" : undefined;
  let symbol: ReactNode;
  switch (kind) {
    case "message": symbol = <><rect x="25" y="33" width="50" height="34" fill={fill} /><path d="M26 34 50 52 74 34" className={inverse} style={throwing ? { stroke: backgroundColor ?? "var(--studio-surface)" } : undefined} /></>; break;
    case "timer": symbol = <><circle cx="50" cy="50" r="26" />{Array.from({ length: 12 }, (_, index) => <line key={index}
      x1={50 + Math.sin(index * Math.PI / 6) * 21} y1={50 - Math.cos(index * Math.PI / 6) * 21}
      x2={50 + Math.sin(index * Math.PI / 6) * 25} y2={50 - Math.cos(index * Math.PI / 6) * 25} />)}<path d="M50 31V50L64 57" /></>; break;
    case "signal": symbol = <path d="M50 24 77 72H23Z" fill={fill} />; break;
    case "error": symbol = <path d="M34 75 42 48 30 45 65 22 57 49 70 52Z" fill={fill} />; break;
    case "escalation": symbol = <path d="M27 74 50 24 73 74 50 57Z" fill={fill} />; break;
    case "cancel": symbol = <path d="m31 23 19 19 19-19 8 8-19 19 19 19-8 8-19-19-19 19-8-8 19-19-19-19Z" fill={fill} />; break;
    case "compensation": symbol = <path d="M49 29 24 50 49 71ZM75 29 50 50 75 71Z" fill={fill} />; break;
    case "conditional": symbol = <><rect x="30" y="23" width="40" height="54" /><path d="M37 34H63M37 44H63M37 54H63M37 64H63" /></>; break;
    case "link": symbol = <path d="M23 40H54V27L79 50 54 73V60H23Z" fill={fill} />; break;
    case "terminate": symbol = <circle cx="50" cy="50" r="29" fill="currentColor" />; break;
    case "multiple": symbol = <polygon points={regularPolygon(50, 50, 28, 5)} fill={fill} />; break;
    case "parallelMultiple": symbol = <path d="M44 22H56V44H78V56H56V78H44V56H22V44H44Z" />; break;
    default: symbol = <text x="50" y="65" textAnchor="middle" stroke="none" fill="currentColor" fontSize="48">?</text>;
  }
  return <g data-bpmn-marker={kind} data-bpmn-throwing={throwing || undefined}>{symbol}</g>;
}

function TaskSymbol({ type, instantiate, backgroundColor }: { type: string; instantiate?: boolean; backgroundColor?: string }) {
  switch (type) {
    case "userTask": return <g data-bpmn-marker="user"><circle cx="12" cy="6" r="4" /><path d="M3 23V18C3 10 21 10 21 18V23M8 15V23M16 15V23" /></g>;
    case "manualTask": return <path data-bpmn-marker="manual" d="M5 22 2 13Q1 9 4 10L8 15V3Q8 0 11 3V11 1Q13-1 14 2V11 3Q17 1 17 4V12 7Q20 5 20 9V17L17 23Z" />;
    case "serviceTask": return <g data-bpmn-marker="service"><polygon points={star(10, 12, 10, 7, 8)} /><circle cx="10" cy="12" r="3" /><polygon points={star(23, 22, 7, 5, 8)} /><circle cx="23" cy="22" r="2" /></g>;
    case "scriptTask": return <g data-bpmn-marker="script"><path d="M3 2H17L23 8V27H3ZM17 2V8H23M7 12H18M7 17H18M7 22H15" /></g>;
    case "businessRuleTask": return <g data-bpmn-marker="business-rule"><rect x="1" y="3" width="27" height="21" /><path d="M1 9H28M1 16H28M9 9V24" /><path d="M2 4H27V8H2Z" fill="currentColor" stroke="none" /></g>;
    case "sendTask": case "receiveTask": return <g data-bpmn-marker={type === "sendTask" ? "send" : "receive"}>
      {type === "receiveTask" && instantiate ? <g data-bpmn-marker="instantiating-receive"><circle cx="14" cy="14" r="13" /><rect x="6" y="9" width="16" height="11" /><path d="M6 9 14 15 22 9" /></g>
        : <><rect x="1" y="4" width="27" height="18" fill={type === "sendTask" ? "currentColor" : "none"} /><path d="M2 5 14.5 15 27 5" className={type === "sendTask" ? "wf-bpmn-symbol-inverse" : undefined}
          style={type === "sendTask" ? { stroke: backgroundColor ?? "var(--studio-surface)" } : undefined} /></>}
    </g>;
    default: return null;
  }
}

function ActivityMarkers({ descriptor, width, height }: { descriptor: BpmnNotationDescriptor; width: number; height: number }) {
  const type = bpmnNotationType(descriptor.type);
  const markers: string[] = [];
  if (descriptor.loop) markers.push(descriptor.loop);
  if (descriptor.compensation) markers.push("compensation");
  if (type === "adHocSubProcess") markers.push("ad-hoc");
  if (isSubProcessElementType(type) && !descriptor.isExpanded) markers.push("collapsed");
  return <>{markers.map((marker, index) => <g key={marker} data-bpmn-marker={marker} transform={`translate(${width / 2 + (index - (markers.length - 1) / 2) * 18 - 6} ${height - 17})`}>
    {marker === "standard" ? <><path d="M11 4A5 5 0 1 0 11 9M11 0V5H6" /></> : marker === "parallel" ? <path d="M2 1V13M6 1V13M10 1V13" />
      : marker === "sequential" ? <path d="M1 2H13M1 6H13M1 10H13" /> : marker === "compensation" ? <path d="M6 1 0 7 6 13ZM12 1 6 7 12 13Z" />
        : marker === "ad-hoc" ? <path d="M0 9C3 0 9 16 13 4" /> : <><rect x="0" y="0" width="12" height="12" /><path d="M2 6H10M6 2V10" /></>}
  </g>)}</>;
}

function labelLines(text: string, width: number) {
  const limit = Math.max(5, Math.floor(width / 7));
  const lines: string[] = [];
  for (const paragraph of text.split("\n")) {
    let line = "";
    for (const word of paragraph.split(/\s+/)) {
      if (line && line.length + word.length + 1 > limit) { lines.push(line); line = word; }
      else line = line ? `${line} ${word}` : word;
    }
    lines.push(line);
  }
  return lines;
}

export interface BpmnNotationProps {
  descriptor: BpmnNotationDescriptor;
  className?: string;
  selected?: boolean;
  faulted?: boolean;
}

export function BpmnNotation({ descriptor, className, selected, faulted }: BpmnNotationProps) {
  const type = bpmnNotationType(descriptor.type);
  const [width, height] = size(descriptor);
  const cx = width / 2; const cy = height / 2;
  const known = shapeTypes.has(type) && !descriptor.type.startsWith("unknown:");
  const labelStyle = bpmnAppearanceLabelStyle(descriptor.appearance);
  const paintStyle = { color: descriptor.appearance?.stroke,
    "--wf-bpmn-fill": descriptor.appearance?.fill, "--wf-bpmn-label-color": descriptor.appearance?.labelColor } as CSSProperties;
  let body: ReactNode;
  if (!known) body = <><rect className="wf-bpmn-notation-outline" x="2" y="2" width={width - 4} height={height - 4} strokeDasharray="5 4" /><text x={cx} y={cy + 8} textAnchor="middle" stroke="none" fill="currentColor" fontSize="28">?</text></>;
  else if (isEventElementType(type)) {
    const radius = Math.min(width, height) / 2 - 2;
    const intermediate = type === "intermediateCatchEvent" || type === "intermediateThrowEvent" || type === "boundaryEvent";
    const nonInterrupting = type === "boundaryEvent" ? descriptor.cancelActivity === false : type === "startEvent" && descriptor.interrupting === false;
    const throwing = type === "intermediateThrowEvent" || type === "endEvent";
    const definitions = descriptor.eventDefinitions ?? [];
    const marker = definitions.length > 1 ? descriptor.parallelMultiple ? "parallelMultiple" : "multiple" : definitions.length ? eventKind(definitions[0]) : null;
    body = <><circle className="wf-bpmn-notation-outline" cx={cx} cy={cy} r={radius} strokeWidth={type === "endEvent" ? 3.5 : 1.5} strokeDasharray={nonInterrupting ? "4 3" : undefined} />
      {intermediate ? <circle cx={cx} cy={cy} r={Math.max(1, radius - 3.5)} strokeDasharray={nonInterrupting ? "4 3" : undefined} /> : null}
      {marker ? <g transform={`translate(${cx - radius} ${cy - radius}) scale(${radius / 50})`}><EventSymbol kind={marker} throwing={throwing} backgroundColor={descriptor.appearance?.fill} /></g> : null}</>;
  } else if (isGatewayElementType(type)) {
    const marker = type === "exclusiveGateway" ? descriptor.gatewayMarkerVisible === false ? null : <path d="M35 34 65 66M65 34 35 66" strokeWidth="6" />
      : type === "parallelGateway" ? <path d="M50 28V72M28 50H72" strokeWidth="7" /> : type === "inclusiveGateway" ? <circle cx="50" cy="50" r="20" strokeWidth="5" />
        : type === "complexGateway" ? <path d="M50 25V75M25 50H75M32 32 68 68M68 32 32 68" strokeWidth="5" />
          : <><circle cx="50" cy="50" r="22" />{descriptor.eventGatewayType !== "Parallel" && !descriptor.instantiate ? <circle cx="50" cy="50" r="18" /> : null}
            {descriptor.eventGatewayType === "Parallel" ? <path d="M50 36V64M36 50H64" strokeWidth="3" /> : <polygon points={regularPolygon(50, 50, 12, 5)} />}</>;
    body = <><polygon className="wf-bpmn-notation-outline" points={`${cx},2 ${width - 2},${cy} ${cx},${height - 2} 2,${cy}`} /><g transform={`scale(${width / 100} ${height / 100})`} data-bpmn-marker={type}>{marker}</g></>;
  } else if (isActivityBearingElementType(type)) {
    body = <><rect className="wf-bpmn-notation-outline" x="2" y="2" width={width - 4} height={height - 4} rx="10" strokeWidth={type === "callActivity" ? 3.5 : 1.5} strokeDasharray={descriptor.eventSubProcess ? "2 3" : undefined} />
      {type === "transaction" || descriptor.transaction ? <rect x="6" y="6" width={width - 12} height={height - 12} rx="7" /> : null}
      <g transform="translate(9 9) scale(.65)"><TaskSymbol type={type} instantiate={descriptor.instantiate} backgroundColor={descriptor.appearance?.fill} /></g>
      <ActivityMarkers descriptor={descriptor} width={width} height={height} /></>;
  } else if (dataTypes.has(type)) {
    const folded = Math.min(12, width / 4);
    body = <><path className="wf-bpmn-notation-outline" d={`M2 2H${width - folded - 2}L${width - 2} ${folded + 2}V${height - 2}H2ZM${width - folded - 2} 2V${folded + 2}H${width - 2}`} />
      {type === "dataInput" || type === "dataOutput" ? <path data-bpmn-marker={type} d="M8 13H17V9L23 16 17 23V19H8Z" fill={type === "dataOutput" ? "currentColor" : "none"} /> : null}
      {descriptor.collection ? <path data-bpmn-marker="collection" d={`M${cx - 5} ${height - 14}V${height - 5}M${cx} ${height - 14}V${height - 5}M${cx + 5} ${height - 14}V${height - 5}`} /> : null}</>;
  } else if (type === "dataStoreReference") {
    body = <><path className="wf-bpmn-notation-outline" d={`M2 10C2 0 ${width - 2} 0 ${width - 2} 10V${height - 10}C${width - 2} ${height + 1} 2 ${height + 1} 2 ${height - 10}Z`} />
      <path d={`M2 10C2 21 ${width - 2} 21 ${width - 2} 10M2 15C2 26 ${width - 2} 26 ${width - 2} 15`} /></>;
  } else if (type === "participant" || type === "lane") {
    const horizontal = descriptor.horizontal !== false;
    const expanded = descriptor.isExpanded !== false;
    const rotated = horizontal && expanded || !horizontal && !expanded;
    body = <><rect className="wf-bpmn-notation-outline" x="1" y="1" width={width - 2} height={height - 2} />
      {type === "participant" && expanded ? <path d={horizontal ? `M30 1V${height - 1}` : `M1 30H${width - 1}`} /> : null}
      {descriptor.name ? <text className="wf-bpmn-notation-label" style={labelStyle} stroke="none" textAnchor="middle"
        transform={rotated ? `translate(${expanded ? 19 : cx} ${cy}) rotate(-90)` : undefined}
        x={rotated ? 0 : cx} y={rotated ? 0 : expanded ? 20 : cy}>{descriptor.name}</text> : null}
      {descriptor.participantMultiplicity ? <path data-bpmn-marker="participant-multiplicity" d={`M${cx - 5} ${height - 15}V${height - 5}M${cx} ${height - 15}V${height - 5}M${cx + 5} ${height - 15}V${height - 5}`} /> : null}</>;
  } else if (type === "textAnnotation") {
    body = <><path data-bpmn-marker="annotation-bracket" d={`M10 2H2V${height - 2}H10`} /><text className="wf-bpmn-notation-label" style={labelStyle} x="15" y="17" stroke="none">
      {labelLines(descriptor.text ?? descriptor.name ?? "", width - 20).map((line, index) => <tspan key={index} x="15" dy={index === 0 ? 0 : 15}>{line}</tspan>)}</text></>;
  } else body = <><rect className="wf-bpmn-notation-group" x="2" y="2" width={width - 4} height={height - 4} rx="10" strokeDasharray="8 3 2 3" />
    {descriptor.name ? <text className="wf-bpmn-notation-label" style={labelStyle} x="10" y="18" stroke="none">{descriptor.name}</text> : null}</>;
  // Status is a separate outline, never a replacement for authored paint or semantic marker fill.
  const halo = isEventElementType(type) ? <circle cx={cx} cy={cy} r={Math.min(width, height) / 2 + 3} />
    : isGatewayElementType(type) ? <polygon points={`${cx},-4 ${width + 4},${cy} ${cx},${height + 4} -4,${cy}`} />
      : <rect x="-3" y="-3" width={width + 6} height={height + 6} rx={isActivityBearingElementType(type) ? 13 : 2} />;
  return <svg className={["wf-bpmn-notation", className].filter(Boolean).join(" ")} role="img" aria-label={bpmnNotationLabel(descriptor)}
    style={paintStyle} data-bpmn-notation-type={type} data-bpmn-notation-known={known} width={width} height={height} viewBox={`0 0 ${width} ${height}`} stroke="currentColor" fill="none" strokeWidth="1.5">
    <title>{bpmnNotationLabel(descriptor)}</title><g data-bpmn-primary="true">{body}</g>
    {selected ? <g className="wf-bpmn-notation-selection" data-bpmn-overlay="selection" aria-hidden="true" pointerEvents="none">{halo}</g> : null}
    {faulted ? <g className="wf-bpmn-notation-fault" data-bpmn-overlay="fault" aria-hidden="true" pointerEvents="none">{halo}</g> : null}
  </svg>;
}
