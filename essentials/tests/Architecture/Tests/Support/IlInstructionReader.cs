using System.Reflection;
using System.Reflection.Emit;

namespace Elsa.Architecture.Tests;

/// <summary>One decoded IL instruction of a compiled method body.</summary>
/// <param name="Offset">The instruction's byte offset in the body.</param>
/// <param name="OpCode">The opcode, with any macro form (<c>ldloc.0</c>, <c>ldarg.s</c>) kept as emitted.</param>
/// <param name="Operand">
/// The metadata token for field, method, string, type and signature operands; the local or argument index for
/// variable operands, including the implicit index of the macro forms; otherwise zero.
/// </param>
/// <param name="Targets">The absolute offsets a branch or <c>switch</c> may jump to; empty for any other instruction.</param>
internal readonly record struct IlInstruction(int Offset, OpCode OpCode, int Operand, int[] Targets);

/// <summary>
/// Decodes a compiled method body (<see cref="MethodBody.GetILAsByteArray"/>) into instructions, so guards can inspect
/// what a method actually calls and loads rather than the source text that produced it.
/// </summary>
internal static class IlInstructionReader
{
    private static readonly OpCode?[] OneByteOpCodes = new OpCode?[0x100];
    private static readonly OpCode?[] TwoByteOpCodes = new OpCode?[0x100];

    static IlInstructionReader()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var opCode = (OpCode)field.GetValue(null)!;
            var value = unchecked((ushort)opCode.Value);
            if (opCode.Size == 1)
                OneByteOpCodes[value] = opCode;
            else
                TwoByteOpCodes[value & 0xFF] = opCode;
        }
    }

    internal static IReadOnlyList<IlInstruction> Read(byte[] il)
    {
        var instructions = new List<IlInstruction>();
        var position = 0;
        while (position < il.Length)
        {
            var offset = position;
            var opCode = il[position] == 0xFE
                ? TwoByteOpCodes[il[++position]]
                : OneByteOpCodes[il[position]];
            position++;
            if (opCode is not { } code)
                throw new InvalidOperationException($"Unknown IL opcode at offset {offset}.");

            var operand = 0;
            int[] targets = [];
            switch (code.OperandType)
            {
                case OperandType.InlineNone:
                    operand = ImplicitIndex(code);
                    break;
                case OperandType.ShortInlineBrTarget:
                    targets = [position + 1 + (sbyte)il[position]];
                    position += 1;
                    break;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    operand = il[position];
                    position += 1;
                    break;
                case OperandType.InlineVar:
                    operand = BitConverter.ToUInt16(il, position);
                    position += 2;
                    break;
                case OperandType.InlineBrTarget:
                    targets = [position + 4 + BitConverter.ToInt32(il, position)];
                    position += 4;
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, position);
                    position += 4;
                    var origin = position + count * 4;
                    targets = new int[count];
                    for (var index = 0; index < count; index++, position += 4)
                        targets[index] = origin + BitConverter.ToInt32(il, position);
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    position += 8;
                    break;
                case OperandType.ShortInlineR:
                case OperandType.InlineI:
                    position += 4;
                    break;
                default:
                    // InlineField, InlineMethod, InlineSig, InlineString, InlineTok and InlineType: a metadata token.
                    operand = BitConverter.ToInt32(il, position);
                    position += 4;
                    break;
            }

            instructions.Add(new IlInstruction(offset, code, operand, targets));
        }

        return instructions;
    }

    /// <summary>The local or argument index a macro opcode such as <c>ldloc.2</c> carries in its name.</summary>
    private static int ImplicitIndex(OpCode code) => code.Name switch
    {
        "ldloc.0" or "stloc.0" or "ldarg.0" => 0,
        "ldloc.1" or "stloc.1" or "ldarg.1" => 1,
        "ldloc.2" or "stloc.2" or "ldarg.2" => 2,
        "ldloc.3" or "stloc.3" or "ldarg.3" => 3,
        _ => 0
    };
}
