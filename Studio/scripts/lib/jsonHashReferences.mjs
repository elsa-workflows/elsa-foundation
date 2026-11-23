import ts from "typescript";
import { relative, resolve } from "node:path";

/** Resolved reference discovery. Recipe admission and source-closure receipts are separate checks. */
export function scanJsonHashReferences({ repoRoot, rootNames, options = {}, gateways = [], primitives = [] }) {
  repoRoot = resolve(repoRoot);
  const program = ts.createProgram(rootNames.map(file => resolve(file)), {
    target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.Bundler, lib: ["lib.es2022.d.ts", "lib.dom.d.ts"],
    skipLibCheck: true, strict: true, noEmit: true, ...options
  });
  const checker = program.getTypeChecker();
  const references = [], moduleEdges = [], errors = [], roles = new Map(), assignments = new Map();
  const rel = file => relative(repoRoot, resolve(file)).split("\\").join("/");
  const owned = file => { const path = rel(file); return path !== ".." && !path.startsWith("../") && !path.split("/").includes("node_modules"); };
  const sourceFiles = program.getSourceFiles().filter(file => owned(file.fileName) && !file.isDeclarationFile);
  const unalias = symbol => {
    const seen = new Set();
    while (symbol && symbol.flags & ts.SymbolFlags.Alias) {
      if (seen.has(symbol)) { errors.push({ code: "alias-cycle", file: "", message: "A symbol alias could not be resolved." }); return undefined; }
      seen.add(symbol); symbol = checker.getAliasedSymbol(symbol);
    }
    return symbol;
  };
  const id = symbol => {
    symbol = unalias(symbol);
    const declaration = symbol?.declarations?.[0];
    return declaration ? `${rel(declaration.getSourceFile().fileName)}::${symbol.getName()}@${declaration.pos}` : "unresolved";
  };
  const seedModule = (definition, role) => {
    const file = program.getSourceFile(resolve(definition.file));
    const symbol = file && checker.getSymbolAtLocation(file);
    if (!symbol) { errors.push({ code: "missing-hash-module", file: rel(definition.file), message: "A required hash module was not resolved." }); return; }
    const exports = checker.getExportsOfModule(symbol);
    for (const name of definition.exports) {
      const target = unalias(exports.find(item => item.getName() === name));
      if (!target) errors.push({ code: "missing-hash-export", file: rel(file.fileName), message: `Required hash export '${name}' was not resolved.` });
      else roles.set(target, role);
    }
  };
  for (const definition of gateways) seedModule(definition, "json-hash-input");
  for (const definition of primitives) seedModule(definition, "digest-primitive");
  const cryptoTypes = new Set();
  for (const file of program.getSourceFiles().filter(file => program.isSourceFileDefaultLibrary(file))) {
    const visit = node => {
      if (ts.isInterfaceDeclaration(node) && ["Crypto", "SubtleCrypto"].includes(node.name.text)) {
        cryptoTypes.add(checker.getSymbolAtLocation(node.name));
        if (node.name.text === "SubtleCrypto")
          for (const member of node.members)
            if (member.name && ts.isIdentifier(member.name) && member.name.text === "digest") roles.set(checker.getSymbolAtLocation(member.name), "digest-primitive");
      }
      ts.forEachChild(node, visit);
    };
    visit(file);
  }
  for (const diagnostic of ts.getPreEmitDiagnostics(program))
    if (diagnostic.category === ts.DiagnosticCategory.Error)
      errors.push({ code: `typescript-${diagnostic.code}`, file: diagnostic.file ? rel(diagnostic.file.fileName) : "", message: ts.flattenDiagnosticMessageText(diagnostic.messageText, " ") });

  const unwrap = expression => {
    while (ts.isParenthesizedExpression(expression) || ts.isAsExpression(expression) || ts.isTypeAssertionExpression(expression) || ts.isNonNullExpression(expression) || ts.isSatisfiesExpression(expression)) expression = expression.expression;
    return expression;
  };
  const constantKey = expression => {
    const type = checker.getTypeAtLocation(expression);
    return ts.isStringLiteralLike(expression) ? expression.text : type.isStringLiteral() ? type.value : undefined;
  };
  function initializers(symbol) {
    return (symbol.declarations ?? []).filter(declaration => ts.isVariableDeclaration(declaration) || ts.isPropertyDeclaration(declaration) || ts.isPropertyAssignment(declaration))
      .map(declaration => declaration.initializer).filter(Boolean);
  }
  function bindingSymbols(binding, seen = new Set()) {
    if (seen.has(binding)) return [];
    seen.add(binding);
    const container = binding.parent.parent;
    const types = [];
    if (ts.isVariableDeclaration(container) && container.initializer) types.push(...originTypes(container.initializer));
    else if (ts.isBindingElement(container)) {
      for (const symbol of bindingSymbols(container, seen)) {
        types.push(checker.getTypeOfSymbolAtLocation(symbol, container));
        for (const initializer of initializers(symbol)) types.push(...originTypes(initializer));
      }
    }
    const name = binding.propertyName ?? binding.name;
    if (!(ts.isIdentifier(name) || ts.isStringLiteralLike(name))) return [];
    return [...new Set(types.map(type => unalias(checker.getPropertyOfType(type, name.text))).filter(Boolean))];
  }
  function originTypes(expression, seen = new Set()) {
    expression = unwrap(expression);
    if (seen.has(expression)) return [];
    seen.add(expression);
    const type = checker.getTypeAtLocation(expression);
    const result = type.isUnion() ? [type, ...type.types] : [type];
    const symbol = unalias(checker.getSymbolAtLocation(expression));
    if (symbol) {
      for (const initializer of [...initializers(symbol), ...(assignments.get(symbol) ?? [])]) result.push(...originTypes(initializer, new Set(seen)));
      for (const declaration of symbol.declarations ?? []) {
        const bound = ts.isBindingElement(declaration) ? bindingSymbols(declaration) : ts.isShorthandPropertyAssignment(declaration)
          ? [unalias(checker.getShorthandAssignmentValueSymbol(declaration))].filter(Boolean) : [];
        for (const source of bound) {
          result.push(checker.getTypeOfSymbolAtLocation(source, declaration));
          for (const initializer of initializers(source)) result.push(...originTypes(initializer, new Set(seen)));
        }
      }
    }
    if (ts.isConditionalExpression(expression)) result.push(...originTypes(expression.whenTrue, new Set(seen)), ...originTypes(expression.whenFalse, new Set(seen)));
    if (ts.isPropertyAccessExpression(expression) || ts.isElementAccessExpression(expression)) {
      const name = ts.isPropertyAccessExpression(expression) ? expression.name.text : constantKey(expression.argumentExpression);
      if (name !== undefined) for (const parentType of originTypes(expression.expression, new Set(seen))) {
        const member = unalias(checker.getPropertyOfType(parentType, name));
        if (member) {
          result.push(checker.getTypeOfSymbolAtLocation(member, expression));
          for (const initializer of initializers(member)) result.push(...originTypes(initializer, new Set(seen)));
        }
      }
    }
    return [...new Set(result)];
  }
  function symbolsOf(expression) {
    expression = unwrap(expression);
    const result = [];
    const direct = unalias(checker.getSymbolAtLocation(expression));
    if (direct) result.push(direct);
    if (ts.isPropertyAccessExpression(expression) || ts.isElementAccessExpression(expression)) {
      const name = ts.isPropertyAccessExpression(expression) ? expression.name.text : constantKey(expression.argumentExpression);
      if (name !== undefined) for (const type of originTypes(expression.expression)) {
        const member = unalias(checker.getPropertyOfType(type, name));
        if (member) result.push(member);
      }
    }
    return [...new Set(result)];
  }
  for (const file of sourceFiles) {
    const visit = node => {
      if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.EqualsToken) {
        const symbol = unalias(checker.getSymbolAtLocation(node.left));
        if (symbol) { if (!assignments.has(symbol)) assignments.set(symbol, []); assignments.get(symbol).push(node.right); }
      }
      ts.forEachChild(node, visit);
    };
    visit(file);
  }
  function targets(expression, seen = new Set()) {
    expression = unwrap(expression);
    if (ts.isConditionalExpression(expression)) return [...targets(expression.whenTrue, new Set(seen)), ...targets(expression.whenFalse, new Set(seen))];
    return symbolsOf(expression).flatMap(symbol => symbolTargets(symbol, new Set(seen)));
  }
  function symbolTargets(symbol, seen) {
    symbol = unalias(symbol);
    if (!symbol || seen.has(symbol)) return [];
    seen.add(symbol);
    if (roles.has(symbol)) return [symbol];
    const result = [];
    for (const declaration of symbol.declarations ?? []) {
      if ((ts.isVariableDeclaration(declaration) || ts.isPropertyDeclaration(declaration) || ts.isPropertyAssignment(declaration)) && declaration.initializer &&
          checker.getTypeAtLocation(declaration.name).getCallSignatures().length)
        result.push(...targets(declaration.initializer, new Set(seen)));
      if (ts.isBindingElement(declaration)) {
        for (const bound of bindingSymbols(declaration)) result.push(...symbolTargets(bound, new Set(seen)));
      }
      if (ts.isShorthandPropertyAssignment(declaration)) result.push(...symbolTargets(checker.getShorthandAssignmentValueSymbol(declaration), new Set(seen)));
    }
    for (const assignment of assignments.get(symbol) ?? []) result.push(...targets(assignment, new Set(seen)));
    return result.length ? result : [symbol];
  }
  const owner = node => {
    for (let parent = node.parent; parent; parent = parent.parent) {
      if (ts.isFunctionDeclaration(parent) || ts.isMethodDeclaration(parent) || ts.isGetAccessorDeclaration(parent) || ts.isSetAccessorDeclaration(parent))
        return parent.name?.getText() ?? `<function@${parent.pos}>`;
      if ((ts.isArrowFunction(parent) || ts.isFunctionExpression(parent)) && ts.isVariableDeclaration(parent.parent)) return parent.parent.name.getText();
    }
    return "<module>";
  };
  const seenReferences = new Set();
  const add = (node, target, kind) => {
    target = unalias(target);
    if (!roles.has(target)) return;
    const file = node.getSourceFile(), position = node.getStart(), identity = id(target);
    const key = `${file.fileName}:${position}:${identity}:${kind}`;
    if (seenReferences.has(key)) return;
    seenReferences.add(key);
    const { line, character } = file.getLineAndCharacterOfPosition(position);
    references.push({ file: rel(file.fileName), owner: owner(node), target: identity, role: roles.get(target), kind, line: line + 1, column: character + 1 });
  };
  for (const file of sourceFiles) {
    const visit = node => {
      if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier && ts.isStringLiteralLike(node.moduleSpecifier)) {
        const module = checker.getSymbolAtLocation(node.moduleSpecifier);
        if (module) for (const exported of checker.getExportsOfModule(unalias(module))) {
          const target = unalias(exported);
          if (roles.has(target)) moduleEdges.push({ file: rel(file.fileName), module: node.moduleSpecifier.text, exported: exported.getName(), target: id(target), role: roles.get(target) });
        }
        return;
      }
      if (ts.isElementAccessExpression(node) && constantKey(node.argumentExpression) === undefined) {
        const sensitive = originTypes(node.expression).some(type => cryptoTypes.has(type.getSymbol()) ||
          type.getProperties().some(symbol => symbolTargets(symbol, new Set()).some(target => roles.has(target))));
        if (sensitive)
          errors.push({ code: "dynamic-hash-access", file: rel(file.fileName), message: "A computed hash or crypto member cannot be resolved statically." });
      }
      if (ts.isCallExpression(node)) {
        for (const target of targets(node.expression)) add(node, target, "call");
      } else if (ts.isIdentifier(node) || ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)) {
        const parent = node.parent;
        const definitionName = parent.name === node && (ts.isVariableDeclaration(parent) || ts.isFunctionDeclaration(parent) || ts.isFunctionExpression(parent) ||
          ts.isMethodDeclaration(parent) || ts.isGetAccessorDeclaration(parent) || ts.isSetAccessorDeclaration(parent) || ts.isParameter(parent) ||
          ts.isBindingElement(parent) || ts.isPropertyDeclaration(parent) || ts.isPropertyAssignment(parent));
        if (!(ts.isPropertyAccessExpression(parent) && parent.name === node) &&
            !(ts.isCallExpression(parent) && parent.expression === node) &&
            !definitionName)
          for (const target of targets(node)) add(node, target, "reference");
      }
      ts.forEachChild(node, visit);
    };
    visit(file);
  }
  return { references, moduleEdges, errors };
}
