// @vitest-environment node

import { describe, expect, it } from "vitest";
import { read, ruleDeclarations } from "./themeTestUtils";

const tokensCss = read("../app/ui/tokens.css");
const consumers = ["../app/ui/tokens.css", "../app/styles.css", "../app/agent/agent.css", "../app/weaver/weaver.css"].map(read);

const referenced = (css: string, prefix: string) =>
  [...new Set([...css.matchAll(new RegExp(`var\\((${prefix}[\\w-]+)`, "g"))].map(match => match[1]))].sort();

describe("material token contract", () => {
  // No built-in theme supplies material recipes, so the shared block is the only definition.
  it("defines every --studio-material-* token the host stylesheets consume", () => {
    const defined = Object.keys(ruleDeclarations(tokensCss, "html[data-theme-material]"));

    expect(referenced(consumers.join("\n"), "--studio-material-").filter(name => !defined.includes(name))).toEqual([]);
  });

  it("no longer carries the retired glass vocabulary", () => {
    expect(referenced(consumers.join("\n"), "--studio-(?:glass|blueprint)-")).toEqual([]);
    expect(tokensCss).not.toMatch(/--studio-(?:glass|blueprint)-/);
  });
});
