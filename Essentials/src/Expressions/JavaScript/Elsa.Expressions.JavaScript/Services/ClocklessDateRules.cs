using System.Text.Json;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>
/// The <c>Date</c> forms the <c>binding-pure-v2</c> profile removes, with the guidance each one gives, shared by the
/// runtime sandbox and authoring validation so both report the same text (ADR 0088 D4).
/// </summary>
internal static class ClocklessDateRules
{
    public const string ProfileName = "binding-pure-v2";

    public static readonly ClocklessDateRule NoArgumentConstruction = new(
        "new Date()",
        "new Date() with no argument reads the clock, which binding-pure-v2 does not allow. Declare a Clock parameter and write new Date(args.now).",
        IsWarning: false);

    public static readonly ClocklessDateRule FunctionCall = new(
        "Date()",
        "Date() called as a function reads the clock, which binding-pure-v2 does not allow. Declare a Clock parameter and write new Date(args.now).toString().",
        IsWarning: false);

    public static readonly ClocklessDateRule Now = new(
        "Date.now()",
        "Date.now() reads the clock, which binding-pure-v2 does not allow. Declare a Clock parameter and write Date.parse(args.now).",
        IsWarning: false);

    // The receiver of a toLocale* call may not be a date, so validation can only warn; at runtime a date always fails.
    public static readonly ClocklessDateRule ToLocaleString = new(
        "toLocaleString()",
        "toLocaleString() depends on a locale, which binding-pure-v2 does not provide. Use toISOString(), or format the parts with the UTC getters.",
        IsWarning: true);

    public static readonly ClocklessDateRule ToLocaleDateString = new(
        "toLocaleDateString()",
        "toLocaleDateString() depends on a locale, which binding-pure-v2 does not provide. Use toISOString().slice(0, 10), or format the parts with the UTC getters.",
        IsWarning: true);

    public static readonly ClocklessDateRule ToLocaleTimeString = new(
        "toLocaleTimeString()",
        "toLocaleTimeString() depends on a locale, which binding-pure-v2 does not provide. Use toISOString().slice(11, 23), or format the parts with the UTC getters.",
        IsWarning: true);

    public const string InvalidDateMessage =
        "Invalid date: binding-pure-v2 parses only ISO 8601 date-time strings such as 2026-10-20T08:30:00.000Z.";

    public static readonly IReadOnlyList<ClocklessDateRule> All =
        [NoArgumentConstruction, FunctionCall, Now, ToLocaleString, ToLocaleDateString, ToLocaleTimeString];

    /// <summary>The <c>toLocale*</c> rules by method name.</summary>
    public static readonly IReadOnlyDictionary<string, ClocklessDateRule> LocaleMethods =
        new Dictionary<string, ClocklessDateRule>(StringComparer.Ordinal)
        {
            ["toLocaleString"] = ToLocaleString,
            ["toLocaleDateString"] = ToLocaleDateString,
            ["toLocaleTimeString"] = ToLocaleTimeString
        };

    /// <summary>
    /// Replaces the global <c>Date</c> with a constructor that throws for the clock-reading forms and otherwise
    /// constructs a native date, keeps the native prototype (so <c>instanceof</c>, subclassing and JSON work), and
    /// replaces <c>Date.now</c>, the <c>toLocale*</c> methods and <c>toISOString</c> with guarded versions. Every
    /// replaced binding is non-writable and non-configurable, so an expression cannot restore the native forms.
    /// </summary>
    public static readonly string Prelude = $$"""
        (function () {
          'use strict';
          const NativeDate = Date;
          const proto = NativeDate.prototype;
          const freeze = (target, name, value) => Object.defineProperty(target, name, { value: value, writable: false, enumerable: false, configurable: false });
          function ClocklessDate(...args) {
            if (new.target === undefined) throw new TypeError({{Encode(FunctionCall.Message)}});
            if (args.length === 0) throw new TypeError({{Encode(NoArgumentConstruction.Message)}});
            return Reflect.construct(NativeDate, args, new.target);
          }
          freeze(ClocklessDate, 'prototype', proto);
          freeze(proto, 'constructor', ClocklessDate);
          freeze(ClocklessDate, 'UTC', NativeDate.UTC);
          freeze(ClocklessDate, 'parse', NativeDate.parse);
          freeze(ClocklessDate, 'now', function now() { throw new TypeError({{Encode(Now.Message)}}); });
          const nativeToISOString = proto.toISOString;
          freeze(proto, 'toISOString', function toISOString() {
            if (Number.isNaN(proto.getTime.call(this))) throw new RangeError({{Encode(InvalidDateMessage)}});
            return nativeToISOString.call(this);
          });
          freeze(proto, 'toLocaleString', function toLocaleString() { throw new TypeError({{Encode(ToLocaleString.Message)}}); });
          freeze(proto, 'toLocaleDateString', function toLocaleDateString() { throw new TypeError({{Encode(ToLocaleDateString.Message)}}); });
          freeze(proto, 'toLocaleTimeString', function toLocaleTimeString() { throw new TypeError({{Encode(ToLocaleTimeString.Message)}}); });
          freeze(globalThis, 'Date', ClocklessDate);
        })();
        """;

    /// <summary>The rule whose message a sandbox error carries, if any.</summary>
    public static bool IsRuleMessage(string message) =>
        StringComparer.Ordinal.Equals(message, InvalidDateMessage) ||
        All.Any(rule => StringComparer.Ordinal.Equals(rule.Message, message));

    private static string Encode(string value) => JsonSerializer.Serialize(value);
}

/// <summary>One removed <c>Date</c> form, its guidance, and whether validation reports it as a warning.</summary>
internal sealed record ClocklessDateRule(string Form, string Message, bool IsWarning);
