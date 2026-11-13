/** XML Schema booleans collapse XML whitespace; reading never normalizes authored bytes. */
export function readBpmnBooleanAttribute(element: Element, name: string): boolean | undefined {
  const value = element.getAttribute(name)?.replace(/^[\t\n\r ]+|[\t\n\r ]+$/g, "");
  return value === "true" || value === "1" ? true : value === "false" || value === "0" ? false : undefined;
}
