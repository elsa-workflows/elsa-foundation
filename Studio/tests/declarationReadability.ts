/** Browser-side measurement shared by the composed journey and isolated regression/control. */
export function measureDeclarationControl(element: Element) {
  if (!(element instanceof HTMLInputElement || element instanceof HTMLSelectElement)) throw new Error("Expected a native declaration control");
  const style = getComputedStyle(element);
  const context = document.createElement("canvas").getContext("2d");
  if (!context) throw new Error("Text measurement is unavailable");
  context.font = style.font;
  const select = element instanceof HTMLSelectElement;
  const value = select ? element.selectedOptions[0]?.label ?? "" : element.value;
  const available = element.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight) - (select ? 24 : 0);
  const textWidth = context.measureText(value).width;
  return { value, available, textWidth, fits: value.length > 0 && Math.ceil(textWidth) <= Math.floor(available) };
}
