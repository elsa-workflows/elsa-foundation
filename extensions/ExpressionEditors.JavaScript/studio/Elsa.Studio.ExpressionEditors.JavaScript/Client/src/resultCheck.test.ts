import { expect, it, vi } from "vitest";
import { javaScriptResultCheck } from "./resultCheck";

const expectedType = { alias: "System.String" };
const type = (alias: string, collectionKind = "Single") => ({ alias, collectionKind });

it("uses bound type information and wraps just the returned source", async () => {
  const resolveParameterType = vi.fn(async () => type("Double"));
  const source = 'return (args["sum"]["gross"]); // reuse binding';
  const diagnostic = await javaScriptResultCheck.check({ source, expectedType, resolveParameterType });
  expect(resolveParameterType).toHaveBeenCalledTimes(1);
  expect(resolveParameterType).toHaveBeenCalledWith("sum", ["gross"]);
  expect(diagnostic?.message).toBe("This expression returns Double. This field expects String.");
  const { from, to, replacement, label } = diagnostic!.fix!;
  expect(source.slice(0, from) + replacement + source.slice(to)).toBe('return String((args["sum"]["gross"])); // reuse binding');
  expect(label).toBe("Wrap in String(…)");
});

it("compares primitive types independently of the field and only offers a supported conversion", async () => {
  const resolveParameterType = vi.fn(async () => type("Boolean"));
  const diagnosis = await javaScriptResultCheck.check({ source: "args.flag", expectedType: type("Double"), resolveParameterType });
  expect(diagnosis).toEqual({ message: "This expression returns Boolean. This field expects Double.", fix: undefined });
  expect(await javaScriptResultCheck.check({ source: "false", expectedType, resolveParameterType })).toMatchObject({
    fix: { replacement: "String(false)" }
  });
  expect(await javaScriptResultCheck.check({ source: "String(args.data)", expectedType: type("Boolean"), resolveParameterType })).toMatchObject({
    message: "This expression returns string. This field expects Boolean."
  });
});

it.each(["String(args.data)", '`Sum: ${args.data}`', "args[alias]", "args?.data", "args.data + 1",
  "const String = fn; args.data", "args.data; doWork()", "args.data(", "3n"])("leaves %s alone with a text destination", async source => {
  const resolveParameterType = vi.fn(async () => type("Double"));
  expect(await javaScriptResultCheck.check({ source, expectedType, resolveParameterType })).toBeUndefined();
  expect(resolveParameterType).not.toHaveBeenCalled();
});

it("leaves missing facts, matching types and non-scalar contracts alone", async () => {
  const check = (result: ReturnType<typeof type> | undefined, expected = expectedType) => javaScriptResultCheck.check({
    source: "args.data", expectedType: expected, resolveParameterType: async () => result
  });
  expect(await check(undefined)).toBeUndefined();
  expect(await check(type("String"))).toBeUndefined();
  expect(await check(type("Double", "Array"))).toBeUndefined();
  expect(await check(type("Double"), type("String", "Array"))).toBeUndefined();
});
