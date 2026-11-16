using Elsa.Expressions.Api.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.Api.Requests;

internal sealed record ListExpressionDescriptors : IRequest<ExpressionDescriptorsResponse>;

internal sealed record ListVariableTypeDescriptors : IRequest<VariableTypeDescriptorsResponse>;
