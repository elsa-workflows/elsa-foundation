import { describe, expect, it } from "vitest";
import { readSecretReference, type SecretReferenceView } from "../maskedInput";

// The secret picker's own reader, from the Secrets extension. Loaded at run time: a static import would pull that
// extension's sources, which declare the SDK only for themselves, into this package's type check. A module that
// fails to load leaves it missing, so a moved file fails the named assertion below instead of the whole file.
const secretPickerModule = "../../../../../extensions/Elsa.Studio.Secrets/Client/src/SecretPickerEditor";
const secretPicker = (await import(/* @vite-ignore */ secretPickerModule).catch(() => undefined)) as { toReference?: unknown } | undefined;
const pickerReader = () => {
  expect(typeof secretPicker?.toReference, `toReference exported by ${secretPickerModule}`).toBe("function");
  return secretPicker!.toReference as (value: unknown) => unknown;
};

const extraField = "extra-field-words";
const reference = (name: string, typeName: string | null = null, scope: string | null = null): SecretReferenceView => ({ name, typeName, scope });

describe("readSecretReference", () => {
  // Objects as the secret picker writes and reads them: both readers must agree on every case, so the masked views
  // show exactly what the picker would accept as a reference and nothing more.
  it.each([
    ["a picked reference", { name: "tokens", typeName: "text", scope: "tenant" }, reference("tokens", "text", "tenant")],
    ["a name alone", { name: "tokens" }, reference("tokens")],
    ["a padded name, kept as written", { name: "  tokens " }, reference("  tokens ")],
    ["a reference with another field, which is dropped", { name: "tokens", rawValue: extraField }, reference("tokens")],
    ["a type and scope that are not text", { name: "tokens", typeName: 3, scope: { region: "west" } }, reference("tokens")],
    ["a blank name", { name: "   " }, null],
    ["a name that is not text", { name: 7 }, null],
    ["no name", { typeName: "text" }, null],
    ["an array", ["tokens"], null],
    ["null", null, null],
    ["a number", 42, null],
    ["a plain string", "tokens", null]
  ] as Array<[string, unknown, SecretReferenceView | null]>)("matches the secret picker on %s", (_label, value, expected) => {
    expect(readSecretReference("Secret", value)).toEqual(expected);
    expect(pickerReader()(value)).toEqual(expected);
  });

  it.each([
    ["a reference as JSON text", "Secret", '{"name":"tokens","scope":"tenant"}', reference("tokens", null, "tenant")],
    ["a reference with another field as JSON text, which is dropped", "Secret", JSON.stringify({ name: "tokens", rawValue: extraField }), reference("tokens")],
    ["JSON text with a blank name", "Secret", '{"name":""}', null],
    ["JSON text that does not parse", "Secret", '{"name":', null],
    ["a reference under another syntax", "Literal", { name: "tokens" }, null],
    ["a reference under a casing variant of the Secret syntax", "secret", { name: "tokens" }, null],
    ["a reference without a syntax", undefined, { name: "tokens" }, null]
  ] as Array<[string, string | undefined, unknown, SecretReferenceView | null]>)("reads %s", (_label, expressionType, value, expected) => {
    expect(readSecretReference(expressionType, value)).toEqual(expected);
  });
});
