using Acornima;
using Acornima.Ast;
using Elsa.Expressions.JavaScript.Core.Models;
using JsExpression = Acornima.Ast.Expression;

namespace Elsa.Expressions.JavaScript.Services;

public sealed partial class JavaScriptExpressionToolingProvider
{
    private sealed partial class AmbientCapabilityVisitor
    {
        private TypeofProbeFacts AnalyzeTypeofProbe(JsExpression expression)
        {
            var facts = new TypeofProbeFacts();
            var value = AnalyzeTypeofProbe(expression, facts, inOptionalChain: false);
            if (value == TypeofProbeValue.Unresolvable && UnwrapParentheses(expression) is Identifier identifier)
                facts.SafeNodes.Add(identifier);
            return facts;
        }

        private TypeofProbeValue AnalyzeTypeofProbe(JsExpression expression, TypeofProbeFacts facts, bool inOptionalChain)
        {
            switch (expression)
            {
                case ParenthesizedExpression parenthesized:
                {
                    var childFacts = new TypeofProbeFacts();
                    var value = AnalyzeTypeofProbe(parenthesized.Expression, childFacts, inOptionalChain: false);
                    facts.Merge(childFacts);
                    return value == TypeofProbeValue.ShortCircuited ? TypeofProbeValue.Undefined : value;
                }
                case ChainExpression chain:
                {
                    var childFacts = new TypeofProbeFacts();
                    var value = AnalyzeTypeofProbe(chain.Expression, childFacts, inOptionalChain: true);
                    if (value is not (TypeofProbeValue.Unsafe or TypeofProbeValue.Unresolvable))
                        facts.Merge(childFacts);
                    return value == TypeofProbeValue.ShortCircuited ? TypeofProbeValue.Undefined : value;
                }
                case Identifier identifier:
                    return AnalyzeTypeofIdentifier(identifier, facts);
                case MemberExpression member:
                    return AnalyzeTypeofMember(member, facts, inOptionalChain);
                case CallExpression call:
                    return AnalyzeTypeofCall(call, facts, inOptionalChain);
                case LogicalExpression { Operator: Operator.NullishCoalescing } logical:
                    return AnalyzeNullishCoalescing(logical, facts);
                case BinaryExpression { Operator: Operator.Addition } binary:
                    return AnalyzeAddition(binary, facts, inOptionalChain);
                case Literal { Kind: TokenKind.NullLiteral or TokenKind.BooleanLiteral or TokenKind.StringLiteral or TokenKind.NumericLiteral }:
                    return TypeofProbeValue.PrimitiveLiteral;
                default:
                    return TypeofProbeValue.Unknown;
            }
        }

        private TypeofProbeValue AnalyzeTypeofMember(MemberExpression member, TypeofProbeFacts facts, bool inOptionalChain)
        {
            var objectFacts = new TypeofProbeFacts();
            var target = AnalyzeTypeofProbe(member.Object, objectFacts, inOptionalChain);
            if (target == TypeofProbeValue.ShortCircuited && inOptionalChain)
            {
                facts.Merge(objectFacts);
                facts.SafeNodes.Add(member);
                if (member.Computed)
                    facts.SkippedNodes.Add(member.Property);
                return TypeofProbeValue.ShortCircuited;
            }

            if (target is TypeofProbeValue.Unresolvable or TypeofProbeValue.Unsafe)
                return TypeofProbeValue.Unsafe;

            if (target == TypeofProbeValue.Undefined)
            {
                if (!member.Optional)
                    return TypeofProbeValue.Unsafe;

                facts.Merge(objectFacts);
                facts.SafeNodes.Add(member);
                if (member.Computed)
                    facts.SkippedNodes.Add(member.Property);
                return TypeofProbeValue.ShortCircuited;
            }

            if (target != TypeofProbeValue.Present)
                return target;

            facts.Merge(objectFacts);
            if (TryGetStaticPath(member, out var staticPath) && staticPath == "globalThis.Math" && !mutations.GlobalThisMayBeReplaced)
            {
                facts.SafeNodes.Add(member);
                return TypeofProbeValue.Present;
            }

            if (!TryGetStaticPath(member, out var path) || FindCapability(path) is not { } capability || ShouldWithholdDiagnostic(capability))
                return TypeofProbeValue.Unknown;

            facts.SafeNodes.Add(member);
            return TypeofProbeValue.Undefined;
        }

        private TypeofProbeValue AnalyzeTypeofCall(CallExpression call, TypeofProbeFacts facts, bool inOptionalChain)
        {
            var calleeFacts = new TypeofProbeFacts();
            var callee = AnalyzeTypeofProbe(call.Callee, calleeFacts, inOptionalChain);
            if (callee == TypeofProbeValue.ShortCircuited && inOptionalChain)
            {
                facts.Merge(calleeFacts);
                facts.SafeNodes.Add(call);
                AddCallArgumentsToSkipped(call, facts);
                return TypeofProbeValue.ShortCircuited;
            }

            if (callee is TypeofProbeValue.Unresolvable or TypeofProbeValue.Unsafe)
                return TypeofProbeValue.Unsafe;

            if (callee == TypeofProbeValue.Undefined)
            {
                if (!call.Optional)
                    return TypeofProbeValue.Unsafe;

                facts.Merge(calleeFacts);
                facts.SafeNodes.Add(call);
                AddCallArgumentsToSkipped(call, facts);
                return TypeofProbeValue.ShortCircuited;
            }

            facts.Merge(calleeFacts);
            return TypeofProbeValue.Unknown;
        }

        private TypeofProbeValue AnalyzeNullishCoalescing(LogicalExpression logical, TypeofProbeFacts facts)
        {
            var leftFacts = new TypeofProbeFacts();
            var left = AnalyzeTypeofProbe(logical.Left, leftFacts, inOptionalChain: false);
            if (left is TypeofProbeValue.Unsafe or TypeofProbeValue.Unresolvable)
                return TypeofProbeValue.Unsafe;

            if (left != TypeofProbeValue.Undefined)
            {
                facts.Merge(leftFacts);
                return TypeofProbeValue.Unknown;
            }

            var rightFacts = new TypeofProbeFacts();
            var right = AnalyzeTypeofProbe(logical.Right, rightFacts, inOptionalChain: false);
            facts.Merge(leftFacts);
            if (right is TypeofProbeValue.Unsafe or TypeofProbeValue.Unresolvable)
                return TypeofProbeValue.Unsafe;

            facts.Merge(rightFacts);
            return right;
        }

        private TypeofProbeValue AnalyzeAddition(BinaryExpression binary, TypeofProbeFacts facts, bool inOptionalChain)
        {
            var leftFacts = new TypeofProbeFacts();
            var left = AnalyzeTypeofProbe(binary.Left, leftFacts, inOptionalChain);
            if (left is TypeofProbeValue.Unsafe or TypeofProbeValue.Unresolvable)
                return TypeofProbeValue.Unsafe;

            var rightFacts = new TypeofProbeFacts();
            var right = AnalyzeTypeofProbe(binary.Right, rightFacts, inOptionalChain);
            if (right is TypeofProbeValue.Unsafe or TypeofProbeValue.Unresolvable)
            {
                if (left == TypeofProbeValue.Undefined)
                    facts.Merge(leftFacts);
                return TypeofProbeValue.Unsafe;
            }

            if (left is (TypeofProbeValue.Undefined or TypeofProbeValue.PrimitiveLiteral) &&
                right is (TypeofProbeValue.Undefined or TypeofProbeValue.PrimitiveLiteral))
            {
                facts.Merge(leftFacts);
                facts.Merge(rightFacts);
                return TypeofProbeValue.PrimitiveLiteral;
            }

            if (left == TypeofProbeValue.Undefined)
                facts.Merge(leftFacts);
            return TypeofProbeValue.Unknown;
        }

        private TypeofProbeValue AnalyzeTypeofIdentifier(Identifier identifier, TypeofProbeFacts facts)
        {
            if (nodeScopes[identifier].IsBound(identifier.Name))
                return TypeofProbeValue.Unknown;

            if (identifier.Name == "globalThis")
                return mutations.GlobalThisMayBeReplaced ? TypeofProbeValue.Unknown : TypeofProbeValue.Present;

            if (JavaScriptRuntimeProfile.DisabledAmbientGlobalNames.Contains(identifier.Name, StringComparer.Ordinal))
            {
                facts.SafeNodes.Add(identifier);
                return TypeofProbeValue.Undefined;
            }

            if (FindCapability(identifier.Name) is not null)
                return TypeofProbeValue.Unresolvable;

            if (identifier.Name == "Math")
                return mutations.MathRootMayBeReplaced ? TypeofProbeValue.Unknown : TypeofProbeValue.Present;

            return TypeofProbeValue.Unknown;
        }

        private static JsExpression UnwrapParentheses(JsExpression expression)
        {
            while (expression is ParenthesizedExpression parenthesized)
                expression = parenthesized.Expression;
            return expression;
        }

        private static void AddCallArgumentsToSkipped(CallExpression call, TypeofProbeFacts facts)
        {
            foreach (var argument in call.Arguments)
                facts.SkippedNodes.Add(argument);
        }

        private sealed class TypeofProbeFacts
        {
            public HashSet<Node> SafeNodes { get; } = new(ReferenceEqualityComparer.Instance);
            public HashSet<Node> SkippedNodes { get; } = new(ReferenceEqualityComparer.Instance);

            public void Merge(TypeofProbeFacts other)
            {
                SafeNodes.UnionWith(other.SafeNodes);
                SkippedNodes.UnionWith(other.SkippedNodes);
            }
        }

        private enum TypeofProbeValue
        {
            Unknown,
            Present,
            Undefined,
            PrimitiveLiteral,
            ShortCircuited,
            Unresolvable,
            Unsafe
        }
    }
}
