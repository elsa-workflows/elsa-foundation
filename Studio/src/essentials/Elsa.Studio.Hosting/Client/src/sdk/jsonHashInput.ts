import { sha256Hex } from "./sha256";

/** The SDK projection of Core JsonHashInput. Its bytes freeze with the first stable Elsa 4 release. */
export function hashJson(value: unknown): string {
  return sha256Hex(canonicalJsonValue(value));
}

/** Canonicalize an SDK value after its JSON projection, refusing silent non-finite-to-null conversion. */
export function canonicalJsonValue(value: unknown): string {
  const json = JSON.stringify(value, (_key, projected: unknown) => {
    if ((typeof projected === "number" || projected instanceof Number) && !Number.isFinite(Number(projected)))
      throw new TypeError("A JSON hash requires finite numbers.");
    return projected;
  });
  if (json === undefined) throw new TypeError("A JSON hash requires a JSON value.");
  return canonicalJsonText(json);
}

/** Hash JSON text without narrowing its number tokens through JavaScript Number. */
export function hashJsonText(json: string): string {
  return sha256Hex(canonicalJsonText(json));
}

/** The same hash through Web Crypto for callers whose cancellation/generation guards span an await. */
export async function hashJsonTextAsync(json: string): Promise<string> {
  const canonical = canonicalJsonText(json);
  if (!globalThis.crypto?.subtle) return sha256Hex(canonical);
  const digest = await globalThis.crypto.subtle.digest("SHA-256", new TextEncoder().encode(canonical));
  return Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, "0")).join("");
}

/** Exact canonical JSON for SDK projections, whose object member names are unique. */
export function canonicalJsonText(json: string): string {
  let position = 0;
  const fail = (): never => { throw new SyntaxError("Invalid or unsupported JSON hash input."); };
  const whitespace = () => { while (position < json.length && /[\x20\t\r\n]/.test(json[position])) position++; };
  const string = () => {
    const start = position++;
    while (position < json.length) {
      const character = json[position++];
      if (character === "\\") position++;
      else if (character === '"') {
        const value = JSON.parse(json.slice(start, position)) as string;
        for (let index = 0; index < value.length; index++) {
          const code = value.charCodeAt(index);
          if (code >= 0xd800 && code <= 0xdbff) {
            const low = value.charCodeAt(++index);
            if (!(low >= 0xdc00 && low <= 0xdfff)) fail();
          } else if (code >= 0xdc00 && code <= 0xdfff) fail();
        }
        return value;
      }
    }
    return fail();
  };
  const read = (depth: number): string => {
    if (depth > 1000) return fail();
    whitespace();
    const first = json[position];
    if (depth >= 1000 && (first === "{" || first === "[")) return fail();
    if (first === '"') return quote(string());
    if (first === "{") {
      position++; whitespace();
      const members: { name: string; value: string }[] = [];
      const names = new Set<string>();
      if (json[position] !== "}") while (true) {
        whitespace(); if (json[position] !== '"') return fail();
        const name = string();
        // SDK projection objects cannot express duplicate members. Refuse ambiguous raw input rather
        // than choose a different duplicate-member ordering from Core's object writer.
        if (names.has(name)) return fail();
        names.add(name); whitespace();
        if (json[position++] !== ":") return fail();
        members.push({ name, value: read(depth + 1) });
        whitespace();
        if (json[position] !== ",") break;
        position++;
      }
      if (json[position++] !== "}") return fail();
      members.sort((left, right) => compareNames(left.name, right.name));
      return `{${members.map(member => `${quote(member.name)}:${member.value}`).join(",")}}`;
    }
    if (first === "[") {
      position++; whitespace();
      const items: string[] = [];
      if (json[position] !== "]") while (true) {
        items.push(read(depth + 1)); whitespace();
        if (json[position] !== ",") break;
        position++;
      }
      if (json[position++] !== "]") return fail();
      return `[${items.join(",")}]`;
    }
    for (const literal of ["null", "true", "false"]) if (json.startsWith(literal, position)) {
      position += literal.length; return literal;
    }
    const token = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/.exec(json.slice(position))?.[0];
    if (!token) return fail();
    position += token.length;
    const [mantissa, explicitExponent = "0"] = token.toLowerCase().split("e");
    const negative = mantissa.startsWith("-");
    const unsigned = negative ? mantissa.slice(1) : mantissa;
    const point = unsigned.indexOf(".");
    const fraction = point < 0 ? 0 : unsigned.length - point - 1;
    const digits = unsigned.replace(".", "").replace(/^0+/, "");
    if (!digits) return "0";
    const coefficient = digits.replace(/0+$/, "");
    const exponent = BigInt(explicitExponent) + BigInt(digits.length - coefficient.length - fraction);
    return `${negative ? "-" : ""}${coefficient}e${exponent}`;
  };
  const result = read(0); whitespace();
  if (position !== json.length) return fail();
  return result;
}

// For valid Unicode strings, scalar-value order is also lexicographic UTF-8 byte order.
function compareNames(left: string, right: string): number {
  let a = 0; let b = 0;
  while (a < left.length && b < right.length) {
    const first = left.codePointAt(a)!; const second = right.codePointAt(b)!;
    if (first !== second) return first - second;
    a += first > 0xffff ? 2 : 1; b += second > 0xffff ? 2 : 1;
  }
  return left.length - right.length;
}

// Core's canonical writer uses the explicit STJ default encoder, independently of the relaxed data encoder.
function quote(value: string): string {
  let result = '"';
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code === 92) result += "\\\\";
    else if (code === 8) result += "\\b";
    else if (code === 9) result += "\\t";
    else if (code === 10) result += "\\n";
    else if (code === 12) result += "\\f";
    else if (code === 13) result += "\\r";
    else if (code < 32 || code >= 127 || [34, 38, 39, 43, 60, 62, 96].includes(code))
      result += `\\u${code.toString(16).toUpperCase().padStart(4, "0")}`;
    else result += value[index];
  }
  return result + '"';
}
