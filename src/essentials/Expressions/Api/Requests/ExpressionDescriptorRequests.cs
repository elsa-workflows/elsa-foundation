using Elsa.Expressions.Api.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.Api.Requests;

public sealed record ListExpressionDescriptors : IRequest<ExpressionDescriptorsResponse>;

public sealed record ListVariableTypeDescriptors : IRequest<VariableTypeDescriptorsResponse>;
