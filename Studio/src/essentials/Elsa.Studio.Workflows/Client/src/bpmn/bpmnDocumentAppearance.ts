/** Namespace-aware presentation facts. Reading never normalizes or writes the source document. */
export const bpmnAppearanceNamespaces = {
  diagram: "http://www.omg.org/spec/BPMN/20100524/DI",
  bounds: "http://www.omg.org/spec/DD/20100524/DC",
  color: "http://www.omg.org/spec/BPMN/non-normative/color/1.0",
  legacyColor: "http://bpmn.io/schema/bpmn/biocolor/1.0"
} as const;

export interface BpmnAppearanceSource { elementId?: string; namespaceURI: string | null; localName: string; value: string }
export interface BpmnLabelFont {
  name?: string; size?: number; bold?: boolean; italic?: boolean; underline?: boolean; strikeThrough?: boolean;
}
export interface BpmnDocumentAppearance {
  fill?: string;
  stroke?: string;
  labelColor?: string;
  labelFont?: BpmnLabelFont;
  /** Includes invalid winning values; a shadowed legacy value never silently replaces them. */
  sources: Partial<Record<"fill" | "stroke" | "labelColor" | "labelStyle", BpmnAppearanceSource>>;
  issues: { source: BpmnAppearanceSource; reason: string }[];
}

const namedColors = /* @__PURE__ */ new Set(("aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown burlywood cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan darkgoldenrod darkgray darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid darkred darksalmon darkseagreen darkslateblue darkslategray darkslategrey darkturquoise darkviolet deeppink deepskyblue dimgray dimgrey dodgerblue firebrick floralwhite forestgreen fuchsia gainsboro ghostwhite gold goldenrod gray green greenyellow grey honeydew hotpink indianred indigo ivory khaki lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan lightgoldenrodyellow lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue lightslategray lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine mediumblue mediumorchid mediumpurple mediumseagreen mediumslateblue mediumspringgreen mediumturquoise mediumvioletred midnightblue mintcream mistyrose moccasin navajowhite navy oldlace olive olivedrab orange orangered orchid palegoldenrod palegreen paleturquoise palevioletred papayawhip peachpuff peru pink plum powderblue purple rebeccapurple red rosybrown royalblue saddlebrown salmon sandybrown seagreen seashell sienna silver skyblue slateblue slategray slategrey snow springgreen steelblue tan teal thistle tomato turquoise violet wheat white whitesmoke yellow yellowgreen transparent").split(" "));

/** Deliberately bounded literal colors; CSS resource URLs, variables and executable/style text stay source-only. */
export function isBpmnAppearanceColor(value: string): boolean {
  const color = value.trim().toLowerCase();
  if (namedColors.has(color) || /^#(?:[\da-f]{3,4}|[\da-f]{6}|[\da-f]{8})$/.test(color)) return true;
  const match = /^(rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch)\(([^()]+)\)$/.exec(color);
  if (!match) return false;
  const number = "[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+)(?:e[+-]?\\d+)?";
  const channel = `${number}%?`, angle = `${number}(?:deg|rad|grad|turn)?`;
  const components = match[1].startsWith("hsl") || ["hwb", "lch", "oklch"].includes(match[1])
    ? match[1].startsWith("hsl") || match[1] === "hwb" ? [angle, `${number}%`, `${number}%`] : [channel, channel, angle]
    : [channel, channel, channel];
  const space = new RegExp(`^${components.join("\\s+")}(?:\\s*\\/\\s*${channel})?$`);
  const comma = new RegExp(`^${components.join("\\s*,\\s*")}(?:\\s*,\\s*${channel})?$`);
  return space.test(match[2].trim()) || /^(?:rgb|rgba|hsl|hsla)$/.test(match[1]) && comma.test(match[2].trim());
}

function source(element: Element, namespaceURI: string | null, localName: string): BpmnAppearanceSource | undefined {
  const value = element.getAttributeNS(namespaceURI, localName);
  return value === null ? undefined : { elementId: element.getAttribute("id") ?? undefined, namespaceURI, localName, value };
}
function child(element: Element, namespaceURI: string, localName: string) {
  return [...element.children].find(item => item.namespaceURI === namespaceURI && item.localName === localName);
}

/** Pass the actual BPMNShape/BPMNEdge instance, not its semantic element (which may have several DI instances). */
export function readBpmnDocumentAppearance(di: Element): BpmnDocumentAppearance {
  const result: BpmnDocumentAppearance = { sources: {}, issues: [] };
  if (di.namespaceURI !== bpmnAppearanceNamespaces.diagram || !["BPMNShape", "BPMNEdge"].includes(di.localName)) return result;
  const paint = (property: "fill" | "stroke" | "labelColor", element: Element, modern: string, legacy?: string) => {
    const candidates = [source(element, bpmnAppearanceNamespaces.color, modern), legacy ? source(element, bpmnAppearanceNamespaces.legacyColor, legacy) : undefined];
    const selected = candidates.find(candidate => candidate?.value);
    if (!selected) return;
    result.sources[property] = selected;
    if (isBpmnAppearanceColor(selected.value)) result[property] = selected.value.trim();
    else result.issues.push({ source: selected, reason: "Unsupported literal color; retained in source." });
  };
  if (di.localName === "BPMNShape") paint("fill", di, "background-color", "fill");
  paint("stroke", di, "border-color", "stroke");
  const label = child(di, bpmnAppearanceNamespaces.diagram, "BPMNLabel");
  if (!label) return result;
  paint("labelColor", label, "color");
  const reference = source(label, null, "labelStyle");
  if (!reference?.value.trim()) return result;
  result.sources.labelStyle = reference;
  // BPMNLabel.labelStyle is an IDREF. Do not guess QName/URI semantics for malformed draft references.
  const styles = [...di.ownerDocument.getElementsByTagNameNS(bpmnAppearanceNamespaces.diagram, "BPMNLabelStyle")]
    .filter(style => style.getAttribute("id") === reference.value.trim()
      && style.parentElement?.namespaceURI === bpmnAppearanceNamespaces.diagram && style.parentElement.localName === "BPMNDiagram");
  if (styles.length !== 1) {
    result.issues.push({ source: reference, reason: "Label style reference is missing or ambiguous; retained in source." });
    return result;
  }
  const fonts = [...styles[0].children].filter(element => element.namespaceURI === bpmnAppearanceNamespaces.bounds && element.localName === "Font");
  if (fonts.length !== 1) {
    result.issues.push({ source: reference, reason: "Label style needs exactly one standard font; retained in source." });
    return result;
  }
  const font = fonts[0];
  const values: BpmnLabelFont = {};
  const name = source(font, null, "name"), size = source(font, null, "size");
  if (name) {
    if (name.value.trim() && ![...name.value].some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127)) values.name = name.value;
    else result.issues.push({ source: name, reason: "Invalid font family; retained in source." });
  }
  if (size) {
    const number = Number(size.value);
    // BPMN2.0.2 B.3.3.3: non-negative, in diagram length units (screen pixels), rather than points.
    if (/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/i.test(size.value.trim()) && Number.isFinite(number) && number >= 0) values.size = number;
    else result.issues.push({ source: size, reason: "Invalid font size; retained in source." });
  }
  for (const [attribute, property] of [["isBold", "bold"], ["isItalic", "italic"], ["isUnderline", "underline"], ["isStrikeThrough", "strikeThrough"]] as const) {
    const value = source(font, null, attribute);
    if (!value) continue;
    if (["true", "1", "false", "0"].includes(value.value.trim())) values[property] = ["true", "1"].includes(value.value.trim());
    else result.issues.push({ source: value, reason: "Invalid font flag; retained in source." });
  }
  if (Object.keys(values).length) result.labelFont = values;
  return result;
}

/** Safe CSS values for SVG text and its HTML counterpart. The font is a family name, never a resource URL. */
export function bpmnAppearanceLabelStyle(appearance?: BpmnDocumentAppearance) {
  const font = appearance?.labelFont;
  return {
    color: appearance?.labelColor,
    fontFamily: font?.name ? `"${font.name.replace(/\\/g, "\\\\").replace(/"/g, '\\"')}", var(--studio-font-ui)` : undefined,
    fontSize: font?.size === undefined ? undefined : `${font.size}px`,
    fontWeight: font?.bold === undefined ? undefined : font.bold ? 700 : 400,
    fontStyle: font?.italic === undefined ? undefined : font.italic ? "italic" as const : "normal" as const,
    textDecorationLine: font?.underline === undefined && font?.strikeThrough === undefined ? undefined
      : [font.underline ? "underline" : "", font.strikeThrough ? "line-through" : ""].filter(Boolean).join(" ") || "none"
  };
}
