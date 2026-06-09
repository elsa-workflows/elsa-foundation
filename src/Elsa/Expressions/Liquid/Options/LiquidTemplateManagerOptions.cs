using Fluid;
using System.Text.Encodings.Web;

namespace Elsa.Expressions.Liquid.Options;

public sealed record LiquidTemplateManagerOptions(TextEncoder TextEncoder, Dictionary<string, Type> FilterRegistrations, Action<TemplateContext> ConfigureFilters);