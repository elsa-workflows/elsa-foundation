import ts from "typescript";
import { relative, resolve } from "node:path";

/** Resolved reference discovery. Recipe admission and source-closure receipts are separate checks. */
export function scanJsonHashReferences(input) {
  try { return scanResolvedReferences(input); }
  catch (error) {
    if (!(error instanceof ResolutionBudgetError)) throw error;
    return { references: [], moduleEdges: [], errors: [{ code: "hash-analysis-budget", file: error.file, message: error.message }] };
  }
}

class ResolutionBudgetError extends Error {
  constructor(file, detail) { super("Hash origin resolution exceeded its work budget; this inventory is incomplete and cannot be admitted. " + detail); this.file = file; }
}

function scanResolvedReferences({ repoRoot, rootNames, options = {}, gateways = [], primitives = [], maxResolutionSteps = 1_000_000 }) {
  repoRoot = resolve(repoRoot);
  if (!Number.isSafeInteger(maxResolutionSteps) || maxResolutionSteps < 1) throw new RangeError("A finite positive hash resolution budget is required.");
  const program = ts.createProgram(rootNames.map(file => resolve(file)), {
    target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.Bundler, lib: ["lib.es2022.d.ts", "lib.dom.d.ts"],
    skipLibCheck: true, strict: true, noEmit: true, ...options
  });
  const checker = program.getTypeChecker();
  const references = [], moduleEdges = [], errors = [], roles = new Map(), assignments = new Map(), memberAssignments = new Map(), selections = new Map(), unresolvedWrites = [];
  const rel = file => relative(repoRoot, resolve(file)).split("\\").join("/");
  let steps = 0, collected = false, lastFile = "", lastLine = 0;
  const work = {};
  const originCache = new WeakMap(), symbolOriginCache = new WeakMap(), pathCache = new WeakMap(), targetCache = new WeakMap(), symbolTargetCache = new WeakMap(), sensitiveCache = new WeakMap();
  const step = (node, phase) => {
    work[phase] = (work[phase] ?? 0) + 1;
    if (node?.getSourceFile) { const file = node.getSourceFile(); lastFile = rel(file.fileName); lastLine = file.getLineAndCharacterOfPosition(node.getStart()).line + 1; }
    if (++steps > maxResolutionSteps) throw new ResolutionBudgetError(lastFile, `At line ${lastLine}; work: ${JSON.stringify(work)}.`);
  };
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
  const alternatives = expression => ts.isBinaryExpression(expression) && [ts.SyntaxKind.QuestionQuestionToken,
    ts.SyntaxKind.BarBarToken, ts.SyntaxKind.AmpersandAmpersandToken].includes(expression.operatorToken.kind);
  const constantKey = expression => {
    const type = checker.getTypeAtLocation(expression);
    return ts.isStringLiteralLike(expression) || ts.isNumericLiteral(expression) ? expression.text
      : type.isStringLiteral() || type.isNumberLiteral() ? String(type.value) : undefined;
  };
  function initializers(symbol) {
    return (symbol.declarations ?? []).filter(declaration => ts.isVariableDeclaration(declaration) || ts.isParameter(declaration) || ts.isPropertyDeclaration(declaration) || ts.isPropertyAssignment(declaration))
      .map(declaration => declaration.initializer).filter(Boolean);
  }
  function accessPaths(expression, seen = new Set()) {
    expression = unwrap(expression);
    step(expression, "paths");
    const root = collected && seen.size === 0;
    if (root && pathCache.has(expression)) return pathCache.get(expression);
    const finish = result => { if (root) pathCache.set(expression, result); return result; };
    if (seen.has(expression)) return [];
    seen.add(expression);
    if (expression.kind === ts.SyntaxKind.ThisKeyword || expression.kind === ts.SyntaxKind.SuperKeyword)
      return finish([{ root: undefined, keys: [] }]);
    if (ts.isConditionalExpression(expression)) return finish([...accessPaths(expression.whenTrue, new Set(seen)), ...accessPaths(expression.whenFalse, new Set(seen))]);
    if (alternatives(expression)) return finish([...accessPaths(expression.left, new Set(seen)), ...accessPaths(expression.right, new Set(seen))]);
    if (ts.isPropertyAccessExpression(expression) || ts.isElementAccessExpression(expression)) {
      const key = ts.isPropertyAccessExpression(expression) ? expression.name.text : constantKey(expression.argumentExpression);
      return finish(key === undefined ? [] : [
        ...accessPaths(expression.expression, new Set(seen)).map(path => ({ root: path.root, keys: [...path.keys, key] })),
        ...memberValues(expression.expression, key, new Set(), new Set(seen)).flatMap(value => accessPaths(value, new Set(seen)))]);
    }
    if (!ts.isIdentifier(expression)) return finish([]);
    const symbol = unalias(ts.isShorthandPropertyAssignment(expression.parent)
      ? checker.getShorthandAssignmentValueSymbol(expression.parent) : checker.getSymbolAtLocation(expression));
    const result = symbol ? [{ root: symbol, keys: [] },
      ...[...initializers(symbol), ...(assignments.get(symbol) ?? [])].flatMap(value => accessPaths(value, seen)),
      ...(selections.get(symbol) ?? []).flatMap(selection => selectedPaths(selection.expression, selection.keys, seen)),
      ...(symbol.declarations ?? []).filter(ts.isBindingElement).flatMap(binding => bindingPaths(binding, seen))] : [];
    return finish(result);
  }
  function selectedPaths(expression, keys, seen) {
    if (!keys.length) return accessPaths(expression, new Set(seen));
    const [key, ...rest] = keys;
    return [...accessPaths(expression, new Set(seen)).map(path => ({ root: path.root, keys: [...path.keys, ...keys] })),
      ...memberValues(expression, key, new Set(), new Set(seen)).flatMap(value => selectedPaths(value, rest, new Set(seen)))];
  }
  function bindingPaths(binding, seen) {
    step(binding, "binding-paths");
    if (seen.has(binding)) return [];
    seen.add(binding);
    const container = binding.parent.parent, name = binding.propertyName ?? binding.name;
    const key = ts.isArrayBindingPattern(binding.parent) ? String(binding.parent.elements.indexOf(binding))
      : ts.isIdentifier(name) || ts.isStringLiteralLike(name) ? name.text
      : ts.isComputedPropertyName(name) ? constantKey(name.expression) : undefined;
    const parents = ts.isVariableDeclaration(container) && container.initializer ? accessPaths(container.initializer, new Set(seen))
      : ts.isBindingElement(container) ? bindingPaths(container, new Set(seen)) : [];
    const sources = ts.isVariableDeclaration(container) && container.initializer ? [container.initializer]
      : ts.isBindingElement(container) ? bindingValues(container, new Set(seen)) : [];
    return [...(key === undefined ? [] : parents.map(path => ({ root: path.root, keys: [...path.keys, key] }))),
      ...(key !== undefined ? sources.flatMap(source => memberValues(source, key, new Set(), new Set(seen))).flatMap(value => accessPaths(value, new Set(seen))) : []),
      ...(binding.initializer ? accessPaths(binding.initializer, seen) : [])];
  }
  function bindingValues(binding, seen) {
    step(binding, "binding-values");
    if (seen.has(binding)) return [];
    seen.add(binding);
    const container = binding.parent.parent, name = binding.propertyName ?? binding.name;
    const key = ts.isArrayBindingPattern(binding.parent) ? String(binding.parent.elements.indexOf(binding))
      : ts.isIdentifier(name) || ts.isStringLiteralLike(name) ? name.text
      : ts.isComputedPropertyName(name) ? constantKey(name.expression) : undefined;
    const parents = ts.isVariableDeclaration(container) && container.initializer ? [container.initializer]
      : ts.isBindingElement(container) ? bindingValues(container, seen) : [];
    return [...(key === undefined ? [] : parents.flatMap(parent => memberValues(parent, key, new Set(), seen))),
      ...(binding.initializer ? [binding.initializer] : [])];
  }
  function assignedMembers(expression, key, seen = new Set()) {
    return accessPaths(expression, seen).flatMap(path => memberAssignments.get(path.root)?.get(JSON.stringify(key === undefined ? path.keys : [...path.keys, key])) ?? []);
  }
  function sensitiveType(type, seen = new Set()) {
    step(undefined, "sensitive-types");
    const root = seen.size === 0;
    if (sensitiveCache.has(type)) return sensitiveCache.get(type);
    if (seen.has(type)) return false;
    seen.add(type);
    const result = cryptoTypes.has(type.getSymbol()) || roles.has(unalias(type.getSymbol())) ||
      type.getProperties().some(symbol => symbolTargets(symbol, new Set()).some(target => roles.has(target))) ||
      (type.isUnion() && type.types.some(member => sensitiveType(member, seen))) ||
      (type.getNumberIndexType() !== undefined && sensitiveType(type.getNumberIndexType(), seen));
    if (root) sensitiveCache.set(type, Boolean(result));
    return result;
  }
  const resolution = () => ({ expressions: new Set(), symbols: new Set(), bindings: new Set() });
  function bindingOrigins(binding, context) {
    step(binding, "binding-origins");
    if (context.bindings.has(binding)) return { symbols: [], types: [], expressions: [] };
    context.bindings.add(binding);
    const container = binding.parent.parent;
    const parents = [], parentExpressions = [];
    if (ts.isVariableDeclaration(container) && container.initializer) {
      parents.push(...originTypes(container.initializer, context)); parentExpressions.push(container.initializer);
    }
    else if (ts.isBindingElement(container)) {
      const origin = bindingOrigins(container, context);
      parents.push(...origin.types); parentExpressions.push(...origin.expressions);
    }
    const name = binding.propertyName ?? binding.name;
    const array = ts.isArrayBindingPattern(binding.parent);
    const key = array ? String(binding.parent.elements.indexOf(binding))
      : ts.isIdentifier(name) || ts.isStringLiteralLike(name) ? name.text
      : ts.isComputedPropertyName(name) ? constantKey(name.expression) : undefined;
    const symbols = [], types = [], expressions = [];
    if (key !== undefined) for (const parent of parentExpressions) expressions.push(...memberValues(parent, key));
    if (key !== undefined) for (const parent of parents) {
      const symbol = unalias(checker.getPropertyOfType(parent, key));
      if (symbol) { symbols.push(symbol); types.push(...symbolOriginTypes(symbol, binding, context)); }
      else if (array) {
        const element = parent.getNumberIndexType();
        if (element) { types.push(element); if (element.getSymbol()) symbols.push(unalias(element.getSymbol())); }
      }
    }
    if (binding.initializer) expressions.push(binding.initializer);
    for (const expression of expressions) types.push(...originTypes(expression, context));
    return { symbols: [...new Set(symbols)], types: [...new Set(types)], expressions: [...new Set(expressions)] };
  }
  function memberValues(expression, key, seen = new Set(), seenPaths = new Set()) {
    expression = unwrap(expression);
    step(expression, "member-values");
    if (seen.has(expression)) return [];
    seen.add(expression);
    const assigned = assignedMembers(expression, key, seenPaths);
    if (ts.isConditionalExpression(expression)) return [...assigned, ...memberValues(expression.whenTrue, key, new Set(seen), seenPaths), ...memberValues(expression.whenFalse, key, new Set(seen), seenPaths)];
    if (alternatives(expression)) return [...assigned, ...memberValues(expression.left, key, new Set(seen), seenPaths), ...memberValues(expression.right, key, new Set(seen), seenPaths)];
    if (ts.isArrayLiteralExpression(expression)) {
      const elements = [];
      const expand = (array, visited = new Set()) => {
        array = unwrap(array);
        if (visited.has(array)) return false;
        visited.add(array);
        if (!ts.isArrayLiteralExpression(array)) {
          const symbol = unalias(checker.getSymbolAtLocation(array));
          const values = symbol ? initializers(symbol) : [];
          return values.length === 1 && expand(values[0], visited);
        }
        for (const element of array.elements) {
          if (ts.isSpreadElement(element)) { if (!expand(element.expression, visited)) return false; }
          else elements.push(element);
        }
        return true;
      };
      if (expand(expression)) {
        const element = elements[Number(key)];
        return [...assigned, ...(element && !ts.isOmittedExpression(element) ? [element] : [])];
      }
      // An unknown spread length makes the selected slot uncertain; retain every possible origin.
      return [...assigned, ...expression.elements.flatMap(element => ts.isSpreadElement(element)
        ? memberValues(element.expression, key, seen, seenPaths) : ts.isOmittedExpression(element) ? [] : [element])];
    }
    if (ts.isObjectLiteralExpression(expression)) {
      const result = [...assigned];
      for (const property of [...expression.properties].reverse()) {
        if (ts.isSpreadAssignment(property)) {
          result.push(...memberValues(property.expression, key, new Set(seen), seenPaths));
          if (definiteMember(property.expression, key)) break;
          continue;
        }
        const name = property.name;
        const propertyKey = name && (ts.isIdentifier(name) || ts.isStringLiteralLike(name) || ts.isNumericLiteral(name) ? name.text
          : ts.isComputedPropertyName(name) ? constantKey(name.expression) : undefined);
        if (propertyKey === key) {
          if (ts.isPropertyAssignment(property)) result.push(property.initializer);
          else if (ts.isShorthandPropertyAssignment(property)) result.push(property.name);
          break;
        }
      }
      return result;
    }
    const symbol = unalias(checker.getSymbolAtLocation(expression));
    return [...assigned, ...(symbol ? [...initializers(symbol), ...(assignments.get(symbol) ?? [])].flatMap(value => memberValues(value, key, seen, seenPaths)) : [])];
  }
  function definiteMember(expression, key, seen = new Set()) {
    expression = unwrap(expression);
    step(expression, "definite-member");
    if (seen.has(expression)) return false;
    seen.add(expression);
    if (ts.isObjectLiteralExpression(expression)) return expression.properties.some(property => {
      if (ts.isSpreadAssignment(property)) return definiteMember(property.expression, key, new Set(seen));
      const name = property.name;
      return name && (ts.isIdentifier(name) || ts.isStringLiteralLike(name) || ts.isNumericLiteral(name) ? name.text
        : ts.isComputedPropertyName(name) ? constantKey(name.expression) : undefined) === key;
    });
    if (ts.isConditionalExpression(expression)) return definiteMember(expression.whenTrue, key, new Set(seen)) && definiteMember(expression.whenFalse, key, new Set(seen));
    const symbol = unalias(checker.getSymbolAtLocation(expression));
    const values = symbol ? [...initializers(symbol), ...(assignments.get(symbol) ?? [])] : [];
    return values.length > 0 && values.every(value => definiteMember(value, key, new Set(seen)));
  }
  function selectedOrigins(expression, keys, context) {
    let types = originTypes(expression, context), symbols = [], expressions = [expression];
    for (const key of keys) {
      symbols = types.map(type => unalias(checker.getPropertyOfType(type, key))).filter(Boolean);
      const indexed = /^(0|[1-9]\d*)$/.test(key) ? types.map(type => type.getNumberIndexType()).filter(Boolean) : [];
      expressions = expressions.flatMap(value => memberValues(value, key));
      types = [...symbols.flatMap(symbol => symbolOriginTypes(symbol, expression, context)), ...indexed,
        ...expressions.flatMap(value => originTypes(value, context))];
    }
    return { types, symbols, expressions };
  }
  function symbolOriginTypes(symbol, location, context) {
    step(location, "symbol-origins");
    symbol = unalias(symbol);
    if (!symbol || context.symbols.has(symbol)) return [];
    const root = collected && context.symbols.size === 0;
    context.symbols.add(symbol);
    const type = checker.getTypeOfSymbolAtLocation(symbol, location);
    if (collected && symbolOriginCache.get(symbol)?.has(type)) return symbolOriginCache.get(symbol).get(type);
    const result = type.isUnion() ? [type, ...type.types] : [type];
    for (const initializer of [...initializers(symbol), ...(assignments.get(symbol) ?? [])]) result.push(...originTypes(initializer, context));
    for (const selection of selections.get(symbol) ?? []) result.push(...selectedOrigins(selection.expression, selection.keys, context).types);
    for (const declaration of symbol.declarations ?? []) {
      if (ts.isBindingElement(declaration)) result.push(...bindingOrigins(declaration, context).types);
      if (ts.isShorthandPropertyAssignment(declaration))
        result.push(...symbolOriginTypes(checker.getShorthandAssignmentValueSymbol(declaration), declaration, context));
    }
    if (root) { if (!symbolOriginCache.has(symbol)) symbolOriginCache.set(symbol, new WeakMap()); symbolOriginCache.get(symbol).set(type, result); }
    return result;
  }
  function originTypes(expression, context) {
    expression = unwrap(expression);
    step(expression, "origins");
    const root = collected && context === undefined;
    if (root && originCache.has(expression)) return originCache.get(expression);
    context ??= resolution();
    if (context.expressions.has(expression)) return [];
    context.expressions.add(expression);
    const type = checker.getTypeAtLocation(expression);
    const result = type.isUnion() ? [type, ...type.types] : [type];
    const symbol = unalias(checker.getSymbolAtLocation(expression));
    if (symbol) result.push(...symbolOriginTypes(symbol, expression, context));
    if (ts.isConditionalExpression(expression)) result.push(...originTypes(expression.whenTrue, context), ...originTypes(expression.whenFalse, context));
    if (alternatives(expression)) result.push(...originTypes(expression.left, context), ...originTypes(expression.right, context));
    if (ts.isPropertyAccessExpression(expression) || ts.isElementAccessExpression(expression)) {
      const name = ts.isPropertyAccessExpression(expression) ? expression.name.text : constantKey(expression.argumentExpression);
      if (name !== undefined) for (const value of memberValues(expression.expression, name)) result.push(...originTypes(value, context));
      if (name !== undefined) for (const parentType of originTypes(expression.expression, context)) {
        const member = unalias(checker.getPropertyOfType(parentType, name));
        if (member) {
          result.push(...symbolOriginTypes(member, expression, context));
        } else if (/^(0|[1-9]\d*)$/.test(name) && parentType.getNumberIndexType()) result.push(parentType.getNumberIndexType());
      }
    }
    const distinct = [...new Set(result)];
    if (root) originCache.set(expression, distinct);
    return distinct;
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
  const assignment = node => ts.isBinaryExpression(node) && [ts.SyntaxKind.EqualsToken, ts.SyntaxKind.QuestionQuestionEqualsToken,
    ts.SyntaxKind.BarBarEqualsToken, ts.SyntaxKind.AmpersandAmpersandEqualsToken].includes(node.operatorToken.kind);
  function destructure(target, expression, keys = []) {
    if (ts.isIdentifier(target)) {
      const symbol = unalias(ts.isShorthandPropertyAssignment(target.parent)
        ? checker.getShorthandAssignmentValueSymbol(target.parent) : checker.getSymbolAtLocation(target));
      if (symbol) { if (!selections.has(symbol)) selections.set(symbol, []); selections.get(symbol).push({ expression, keys }); }
    } else if (ts.isObjectLiteralExpression(target)) {
      for (const property of target.properties) {
        const name = property.name;
        const key = name && (ts.isIdentifier(name) || ts.isStringLiteralLike(name) ? name.text : ts.isComputedPropertyName(name) ? constantKey(name.expression) : undefined);
        if (key !== undefined) {
          if (ts.isShorthandPropertyAssignment(property)) {
            destructure(property.name, expression, [...keys, key]);
            if (property.objectAssignmentInitializer) {
              const symbol = unalias(checker.getShorthandAssignmentValueSymbol(property));
              if (symbol) { if (!assignments.has(symbol)) assignments.set(symbol, []); assignments.get(symbol).push(property.objectAssignmentInitializer); }
            }
          }
          else if (ts.isPropertyAssignment(property)) destructure(property.initializer, expression, [...keys, key]);
        }
      }
    } else if (ts.isArrayLiteralExpression(target)) {
      target.elements.forEach((element, index) => destructure(element, expression, [...keys, String(index)]));
    }
  }
  for (const file of sourceFiles) {
    const visit = node => {
      if (assignment(node)) {
        const symbol = unalias(checker.getSymbolAtLocation(node.left));
        // Properties and instance methods share checker symbols across unrelated receivers.
        // Their replacement writes belong to receiver paths, not the global symbol.
        if (symbol && ts.isIdentifier(node.left)) { if (!assignments.has(symbol)) assignments.set(symbol, []); assignments.get(symbol).push(node.right); }
        if (ts.isObjectLiteralExpression(node.left) || ts.isArrayLiteralExpression(node.left)) destructure(node.left, node.right);
      }
      ts.forEachChild(node, visit);
    };
    visit(file);
  }
  // Collect member writes after ordinary aliases, so a receiver copied before a write keeps its origin.
  for (const file of sourceFiles) {
    const visit = node => {
      if (assignment(node) &&
          (ts.isPropertyAccessExpression(node.left) || ts.isElementAccessExpression(node.left))) {
        const paths = accessPaths(node.left);
        if (paths.length === 0 || paths.some(path => path.root === undefined)) unresolvedWrites.push({ file, value: node.right });
        for (const path of paths.filter(path => path.root !== undefined)) {
          if (!memberAssignments.has(path.root)) memberAssignments.set(path.root, new Map());
          const values = memberAssignments.get(path.root), key = JSON.stringify(path.keys);
          if (!values.has(key)) values.set(key, []);
          values.get(key).push(node.right);
        }
      }
      ts.forEachChild(node, visit);
    };
    visit(file);
  }
  collected = true;
  for (const write of unresolvedWrites)
    if (originTypes(write.value).some(type => sensitiveType(type)))
      errors.push({ code: "unresolved-hash-receiver", file: rel(write.file.fileName), message: "A hash or crypto receiver write cannot be bound to its source owner." });
  function targets(expression, seen) {
    expression = unwrap(expression);
    step(expression, "targets");
    const root = seen === undefined;
    if (root && targetCache.has(expression)) return targetCache.get(expression);
    seen ??= new Set();
    if (seen.has(expression)) return [];
    seen.add(expression);
    // Target discovery is graph reachability. A query-wide visited set preserves its
    // union of authorities without expanding every route through a diamond alias graph.
    if (ts.isConditionalExpression(expression)) return [...targets(expression.whenTrue, seen), ...targets(expression.whenFalse, seen)];
    if (alternatives(expression)) return [...targets(expression.left, seen), ...targets(expression.right, seen)];
    const values = [];
    if (ts.isPropertyAccessExpression(expression) || ts.isElementAccessExpression(expression)) {
      const key = ts.isPropertyAccessExpression(expression) ? expression.name.text : constantKey(expression.argumentExpression);
      if (key !== undefined) values.push(...memberValues(expression.expression, key));
    }
    const result = [...symbolsOf(expression).flatMap(symbol => symbolTargets(symbol, seen)),
      ...new Set(values.flatMap(value => targets(value, seen)))];
    if (root) targetCache.set(expression, result);
    return result;
  }
  function symbolTargets(symbol, seen) {
    step(undefined, "symbol-targets");
    symbol = unalias(symbol);
    if (!symbol || seen.has(symbol)) return [];
    if (symbolTargetCache.has(symbol)) return symbolTargetCache.get(symbol);
    const root = [...seen].every(item => typeof item.kind === "number");
    seen.add(symbol);
    if (roles.has(symbol)) return [symbol];
    const result = [];
    for (const selection of selections.get(symbol) ?? []) {
      const origin = selectedOrigins(selection.expression, selection.keys, resolution());
      for (const bound of origin.symbols) result.push(...symbolTargets(bound, seen));
      for (const expression of origin.expressions) result.push(...targets(expression, seen));
    }
    for (const declaration of symbol.declarations ?? []) {
      if ((ts.isVariableDeclaration(declaration) || ts.isParameter(declaration) || ts.isPropertyDeclaration(declaration) || ts.isPropertyAssignment(declaration)) && declaration.initializer)
        result.push(...targets(declaration.initializer, seen));
      if (ts.isBindingElement(declaration)) {
        const origin = bindingOrigins(declaration, resolution());
        for (const bound of origin.symbols) result.push(...symbolTargets(bound, seen));
        for (const expression of origin.expressions) result.push(...targets(expression, seen));
      }
      if (ts.isShorthandPropertyAssignment(declaration)) result.push(...symbolTargets(checker.getShorthandAssignmentValueSymbol(declaration), seen));
    }
    for (const assignment of assignments.get(symbol) ?? []) result.push(...targets(assignment, seen));
    const distinct = result.length ? [...new Set(result)] : [symbol];
    if (root) symbolTargetCache.set(symbol, distinct);
    return distinct;
  }
  const owner = node => {
    for (let parent = node.parent; parent; parent = parent.parent) {
      if (ts.isFunctionDeclaration(parent) || ts.isMethodDeclaration(parent) || ts.isGetAccessorDeclaration(parent) || ts.isSetAccessorDeclaration(parent))
        return parent.name?.getText() ?? `<function@${parent.pos}>`;
      if ((ts.isArrowFunction(parent) || ts.isFunctionExpression(parent)) && ts.isVariableDeclaration(parent.parent)) return parent.parent.name.getText();
    }
    return "<module>";
  };
  const runtimeValueUse = node => {
    const parent = node.parent;
    if (parent.name === node && (ts.isVariableDeclaration(parent) || ts.isParameter(parent) || ts.isBindingElement(parent) ||
        ts.isPropertyDeclaration(parent) || ts.isPropertyAssignment(parent) || ts.isFunctionDeclaration(parent) ||
        ts.isMethodDeclaration(parent) || ts.isGetAccessorDeclaration(parent) || ts.isSetAccessorDeclaration(parent))) return false;
    for (let ancestor = parent; ancestor; ancestor = ancestor.parent) {
      if (ts.isInterfaceDeclaration(ancestor) || ts.isTypeAliasDeclaration(ancestor) ||
          ts.isExportDeclaration(ancestor) && ancestor.isTypeOnly || ts.isExportSpecifier(ancestor) && ancestor.isTypeOnly) return false;
      if (ts.isTypeNode(ancestor)) {
        const heritage = ts.isExpressionWithTypeArguments(ancestor) ? ancestor.parent : undefined;
        if (heritage && ts.isHeritageClause(heritage) && heritage.token === ts.SyntaxKind.ExtendsKeyword &&
            (ts.isClassDeclaration(heritage.parent) || ts.isClassExpression(heritage.parent))) continue;
        return false;
      }
    }
    return true;
  };
  const seenReferences = new Set();
  const add = (node, target, kind, role) => {
    target = unalias(target);
    role ??= roles.get(target);
    if (!role) return;
    const file = node.getSourceFile(), position = node.getStart(), identity = id(target);
    const key = `${file.fileName}:${position}:${identity}:${kind}`;
    if (seenReferences.has(key)) return;
    seenReferences.add(key);
    const { line, character } = file.getLineAndCharacterOfPosition(position);
    references.push({ file: rel(file.fileName), owner: owner(node), target: identity, role, kind, line: line + 1, column: character + 1 });
  };
  for (const file of sourceFiles) {
    const visit = node => {
      // Retain native authority ingress even when an arbitrary returned/erased value
      // prevents resolving its eventual call. Recipe admission must close these owners too.
      if ((ts.isIdentifier(node) || ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)) && runtimeValueUse(node)) {
        const type = checker.getTypeAtLocation(node), symbol = type.getSymbol();
        if (cryptoTypes.has(symbol)) add(node, symbol, "reference", "crypto-authority");
      }
      if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier && ts.isStringLiteralLike(node.moduleSpecifier)) {
        const module = checker.getSymbolAtLocation(node.moduleSpecifier);
        if (module) for (const exported of checker.getExportsOfModule(unalias(module))) {
          const target = unalias(exported);
          if (roles.has(target)) moduleEdges.push({ file: rel(file.fileName), module: node.moduleSpecifier.text, exported: exported.getName(), target: id(target), role: roles.get(target) });
        }
        return;
      }
      if (ts.isElementAccessExpression(node) && constantKey(node.argumentExpression) === undefined) {
        const sensitive = originTypes(node.expression).some(type => sensitiveType(type));
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
