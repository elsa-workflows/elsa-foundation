using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Services;
using Elsa.Primitives.Models;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Spec 1223 (S-1): the <c>binding-readonly-v1</c> surface. Native intrinsics on the host clock in UTC with the
/// invariant culture, the built-in functions and registered host functions, identical in an input binding and a script.
/// </summary>
public sealed class ExpressionCapabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 8, 15, 30, 250, TimeSpan.Zero);

    // ---- T010: intrinsics and the clock (quickstart A1–A4, SC-003) ----

    [Theory]
    [InlineData("new Date().toISOString()", "2026-01-15T08:15:30.250Z")]
    [InlineData("String(Date.now())", "1768464930250")]
    [InlineData("Temporal.Now.instant().toString()", "2026-01-15T08:15:30.25Z")]
    public async Task The_clock_is_the_host_time_provider(string expression, string expected)
    {
        var (binding, script) = await BothAsync(expression);

        Assert.Equal(expected, binding.GetString());
        Assert.Equal(expected, script.GetString());
    }

    [Fact]
    public async Task Expressions_evaluate_in_utc_with_the_invariant_culture()
    {
        var (binding, script) = await BothAsync(
            "[new Date(Date.UTC(2026, 0, 15, 8, 15)).getHours(), new Date().getTimezoneOffset(), (1234.5).toLocaleString(), " +
            "new Intl.DateTimeFormat().resolvedOptions().timeZone, Temporal.Now.timeZoneId(), " +
            "new Intl.DateTimeFormat('en-GB', { hour: '2-digit', hourCycle: 'h23' }).format(new Date('2026-01-15T08:15:00Z')), " +
            "Intl.DateTimeFormat() instanceof Intl.DateTimeFormat].join('|')");

        // Jint defaults Intl.DateTimeFormat and Temporal.Now to the host's zone; the engine defaults both to UTC.
        Assert.Equal("8|0|1,234.5|UTC|UTC|08|true", binding.GetString());
        Assert.Equal(binding.GetString(), script.GetString());
    }

    [Fact]
    public async Task Math_random_is_available_and_may_differ_between_calls()
    {
        var (binding, script) = await BothAsync(
            "(() => { const a = Math.random(), b = Math.random(); return typeof a === 'number' && a >= 0 && a < 1 && b >= 0 && b < 1; })()");

        Assert.True(binding.GetBoolean());
        Assert.True(script.GetBoolean());
    }

    // ---- T012: IANA zones through Temporal and Intl (quickstart A5a, FR-025) ----

    [Theory]
    [InlineData("2026-01-15T08:15:00Z", 9)]
    [InlineData("2026-07-15T08:15:00Z", 10)]
    public async Task Temporal_converts_an_instant_to_an_iana_zone_with_daylight_saving(string instant, int hour)
    {
        var (binding, script) = await BothAsync($"Temporal.Instant.from('{instant}').toZonedDateTimeISO('Europe/Amsterdam').hour");

        Assert.Equal(hour, binding.GetInt32());
        Assert.Equal(hour, script.GetInt32());
    }

    [Theory]
    [InlineData("2026-01-15T08:15:00Z", "09")]
    [InlineData("2026-07-15T08:15:00Z", "10")]
    public async Task Intl_formats_an_instant_in_an_iana_zone_with_daylight_saving(string instant, string hour)
    {
        var (binding, script) = await BothAsync(
            $"new Intl.DateTimeFormat('en-GB', {{ timeZone: 'Europe/Amsterdam', hour: '2-digit', hourCycle: 'h23' }}).format(new Date('{instant}'))");

        Assert.Equal(hour, binding.GetString());
        Assert.Equal(hour, script.GetString());
    }

    // ---- T011: GUID built-ins in Elsa 3's forms (quickstart B6, B7) ----

    [Theory]
    [InlineData("newGuid")]
    [InlineData("newGuidString")]
    [InlineData("getGuidString")]
    public async Task Guid_functions_return_a_new_canonical_guid_string(string function)
    {
        var (binding, script) = await BothAsync($"[{function}(), {function}()]");

        foreach (var result in new[] { binding, script })
        {
            var values = result.EnumerateArray().Select(item => item.GetString()!).ToArray();
            Assert.All(values, value => Assert.Equal(Guid.Parse(value).ToString(), value));
            Assert.NotEqual(values[0], values[1]);
        }
    }

    [Theory]
    [InlineData("newShortGuid")]
    [InlineData("getShortGuid")]
    public async Task Short_guid_functions_return_elsa_3s_short_form(string function)
    {
        var (binding, script) = await BothAsync($"{function}()");

        foreach (var value in new[] { binding.GetString()!, script.GetString()! })
        {
            Assert.InRange(value.Length, 1, 22);
            Assert.DoesNotContain('/', value);
            Assert.DoesNotContain('+', value);
            Assert.DoesNotContain('=', value);
        }
    }

    /// <summary>T001: Elsa.JavaScript 3.5.3's short GUID for fixed GUIDs (Base64 of ToByteArray(), '/', '+', '=' removed).</summary>
    [Theory]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964ff", "xmWb4aLEdC0LQDPT8lkw")]
    [InlineData("00000000-0000-0000-0000-000000000000", "AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("fbfbfbfb-fbfb-fbfb-fbfb-fbfbfbfbfbfb", "v7v7v7v7v7w")]
    public void Short_guid_matches_elsa_3_for_a_fixed_guid(string guid, string expected) =>
        Assert.Equal(expected, Elsa.Expressions.JavaScript.Jint.Services.JavaScriptBuiltIns.ShortGuid(Guid.Parse(guid)));

    [Theory]
    [InlineData("{6F9619FF-8B86-D011-B42D-00CF4FC964FF}")]
    [InlineData("6F9619FF8B86D011B42D00CF4FC964FF")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("(6F9619FF-8B86-D011-B42D-00CF4FC964FF)")]
    public async Task ParseGuid_returns_the_canonical_text_of_each_accepted_form(string text)
    {
        var (binding, script) = await BothAsync($"parseGuid('{text}')");

        Assert.Equal("6f9619ff-8b86-d011-b42d-00cf4fc964ff", binding.GetString());
        Assert.Equal("6f9619ff-8b86-d011-b42d-00cf4fc964ff", script.GetString());
    }

    [Fact]
    public async Task ParseGuid_fails_the_evaluation_on_text_that_is_not_a_guid()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<JavaScriptException>(() => EvaluateBindingAsync(scope, "parseGuid('not a guid')"));
        Assert.Contains("parseGuid", exception.Message, StringComparison.Ordinal);
    }

    // ---- T017: Base64, no toJson, JSON values only (quickstart B8–B10, SC-005, SC-008) ----

    [Theory]
    [InlineData("hello", "aGVsbG8=")]
    [InlineData("Grüße, 世界 😀", "R3LDvMOfZSwg5LiW55WMIPCfmIA=")]
    [InlineData("", "")]
    public async Task Base64_functions_match_elsa_3_and_round_trip(string text, string base64)
    {
        var literal = JsonSerializer.Serialize(text);
        var (binding, script) = await BothAsync($"[stringToBase64({literal}), stringFromBase64('{base64}')]");

        foreach (var result in new[] { binding, script })
        {
            Assert.Equal(base64, result[0].GetString());
            Assert.Equal(text, result[1].GetString());
        }
    }

    // Elsa 3 converted a number argument to its text.
    [Fact]
    public async Task StringToBase64_encodes_a_number_as_its_text()
    {
        var (binding, script) = await BothAsync("[stringToBase64(123), stringToBase64(1.5)]");

        foreach (var result in new[] { binding, script })
        {
            Assert.Equal("MTIz", result[0].GetString());
            Assert.Equal("MS41", result[1].GetString());
        }
    }

    [Fact]
    public async Task StringFromBase64_fails_on_invalid_base64_and_replaces_invalid_utf8()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();

        await Assert.ThrowsAsync<JavaScriptException>(() => EvaluateBindingAsync(scope, "stringFromBase64('not base64!')"));
        // 0xFF is not UTF-8: Elsa 3 decoded it to U+FFFD.
        var replaced = await EvaluateBindingAsync(scope, "stringFromBase64('/w==')");
        Assert.Equal("�", replaced.GetString());
    }

    [Fact]
    public async Task There_is_no_toJson_global()
    {
        var (binding, script) = await BothAsync("typeof toJson");

        Assert.Equal("undefined", binding.GetString());
        Assert.Equal("undefined", script.GetString());
    }

    [Fact]
    public async Task Host_function_arguments_and_results_are_json_values()
    {
        var seen = new List<string>();
        var echo = new JavaScriptHostFunctionDeclaration("acme.echo", call =>
        {
            seen.AddRange(call.Arguments.Select(argument => argument.GetRawText()));
            return JsonSerializer.SerializeToElement(new { kind = "json", items = new[] { 1, 2 } });
        });

        var (binding, script) = await BothAsync(
            "(() => { const r = acme.echo(new Date(Date.UTC(2026, 0, 15, 8, 15)), undefined, { a: [1, 'x'] }); r.items.push(3); return r; })()",
            services => services.AddSingleton(echo));

        Assert.Equal(["\"2026-01-15T08:15:00.000Z\"", "null", "{\"a\":[1,\"x\"]}"], seen.Take(3));
        Assert.Equal("{\"kind\":\"json\",\"items\":[1,2,3]}", binding.GetRawText());
        Assert.Equal(binding.GetRawText(), script.GetRawText());
    }

    [Fact]
    public async Task A_function_argument_that_is_not_json_fails_the_call()
    {
        var echo = new JavaScriptHostFunctionDeclaration("acme.echo", call => call.Arguments[0]);
        await using var provider = Build(services => services.AddSingleton(echo));
        await using var scope = provider.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<JavaScriptException>(() => EvaluateBindingAsync(scope, "acme.echo(() => 1)"));
        Assert.Contains("acme.echo", exception.Message, StringComparison.Ordinal);
    }

    // ---- T018: registration and naming (quickstart B11–B11b) ----

    [Fact]
    public async Task A_namespaced_host_function_is_a_member_of_a_read_only_namespace_object()
    {
        var lookup = new JavaScriptHostFunctionDeclaration("acme.lookup", call => JsonSerializer.SerializeToElement("rate:" + call.Arguments[0].GetString()));

        var (binding, script) = await BothAsync(
            "[acme.lookup('eur'), Object.isExtensible(acme), Object.getOwnPropertyDescriptor(acme, 'lookup').writable, " +
            "Object.getOwnPropertyDescriptor(globalThis, 'acme').writable].join('|')",
            services => services.AddSingleton(lookup));

        Assert.Equal("rate:eur|false|false|false", binding.GetString());
        Assert.Equal(binding.GetString(), script.GetString());
    }

    [Theory]
    [InlineData("lookup", "lookup")]
    [InlineData("variables.lookup", "variables")]
    [InlineData("Math.lookup", "Math")]
    [InlineData("newGuid.lookup", "newGuid")]
    [InlineData("acme.crm.lookup", "acme.crm.lookup")]
    [InlineData("acme.", "acme.")]
    [InlineData("1acme.lookup", "1acme.lookup")]
    public void A_registration_that_breaks_the_naming_rule_fails_naming_the_function(string name, string named)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            JavaScriptHostFunctions.Validate([new JavaScriptHostFunctionDeclaration(name, _ => default)]));

        Assert.Contains($"'{name}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(named, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("acme.lookup")]
    [InlineData(JavaScriptBuiltInFunctions.NewGuid)]
    public void A_duplicate_registration_fails_naming_the_function(string name)
    {
        var declaration = new JavaScriptHostFunctionDeclaration(name, _ => default);

        var exception = Assert.Throws<InvalidOperationException>(() => JavaScriptHostFunctions.Validate([declaration, declaration with { }]));

        Assert.Contains($"'{name}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
    }

    // ---- #5664: a host function declares the unprefixed global it replaces ----

    [Fact]
    public void A_declaration_records_the_global_functions_it_replaces()
    {
        var declaration = new JavaScriptHostFunctionDeclaration("nexxbiz.getBusinessSettings", _ => default)
        {
            ReplacesGlobalFunctions = ["getBusinessSettings", "getSettings"],
        };

        Assert.Equal(["getBusinessSettings", "getSettings"], declaration.ReplacesGlobalFunctions);
        Assert.Empty(new JavaScriptHostFunctionDeclaration("acme.lookup", _ => default).ReplacesGlobalFunctions);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" getSettings")]
    [InlineData("acme.getSettings")]
    public void A_replaced_global_that_is_not_a_plain_name_fails_naming_the_function(string replaced)
    {
        var exception = Assert.Throws<ArgumentException>(() => new JavaScriptHostFunctionDeclaration("acme.getSettings", _ => default)
        {
            ReplacesGlobalFunctions = [replaced],
        });

        Assert.Contains("'acme.getSettings'", exception.Message, StringComparison.Ordinal);
    }

    // ---- T034: the default composition (framework §2.23.1) ----

    [Fact]
    public async Task The_default_composition_resolves_the_built_ins_the_function_catalog_and_the_analyzer()
    {
        await using var provider = Build();

        var declarations = provider.GetServices<JavaScriptHostFunctionDeclaration>().Select(declaration => declaration.Name).ToArray();
        Assert.Equal(JavaScriptBuiltInFunctions.Names.Order(StringComparer.Ordinal), declarations.Order(StringComparer.Ordinal));
        Assert.All(provider.GetServices<JavaScriptHostFunctionDeclaration>(), declaration => Assert.False(declaration.ReadsVariables));
        Assert.NotNull(provider.GetRequiredService<JavaScriptHostFunctions>());
        Assert.NotNull(provider.GetRequiredService<IJavaScriptDotNetUsageAnalyzer>());
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
    }

    [Fact]
    public void Shell_activation_fails_on_a_registration_that_breaks_the_naming_rule()
    {
        using var provider = Build(services => services.AddSingleton(new JavaScriptHostFunctionDeclaration("lookup", _ => default)));

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetServices<IShellInitializer>().ToArray());

        Assert.Contains("'lookup'", exception.Message, StringComparison.Ordinal);
    }

    // ---- T019: surface parity (quickstart A5, SC-004) ----

    [Fact]
    public async Task Every_built_in_and_intrinsic_is_available_on_both_surfaces()
    {
        var names = JavaScriptBuiltInFunctions.Names.Order(StringComparer.Ordinal).Concat(["Date", "Intl", "Temporal", "Math.random"]).ToArray();
        var (binding, script) = await BothAsync($"[{string.Join(", ", names.Select(name => $"typeof {name}"))}].join('|')");

        Assert.Equal(string.Join('|', names.Select(name => name is "Intl" or "Temporal" ? "object" : "function")), binding.GetString());
        Assert.Equal(binding.GetString(), script.GetString());
    }

    private static async Task<(JsonElement Binding, JsonElement Script)> BothAsync(string expression, Action<IServiceCollection>? configure = null)
    {
        await using var provider = Build(configure);
        await using var scope = provider.CreateAsyncScope();
        var binding = await EvaluateBindingAsync(scope, expression);
        var script = (await scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>()
            .EvaluateAsync(new JavaScriptScriptEvaluationRequest($"return {expression};", JsonSerializer.SerializeToElement(new { }), CancellationToken.None))).Value!.Value;
        return (binding, script);
    }

    private static async Task<JsonElement> EvaluateBindingAsync(IServiceScope scope, string expression)
    {
        var definition = new ExpressionDefinition(
            "JavaScript",
            expression,
            new TypeReference("Any"),
            new Dictionary<string, ExpressionParameterBinding>(),
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingReadonlyV1);
        return await scope.ServiceProvider.GetRequiredService<IPortableJavaScriptEvaluator>()
            .EvaluateAsync(new ExpressionEvaluationRequest(definition, new Dictionary<string, JsonElement>(), CancellationToken.None));
    }

    private static ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        configure?.Invoke(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
