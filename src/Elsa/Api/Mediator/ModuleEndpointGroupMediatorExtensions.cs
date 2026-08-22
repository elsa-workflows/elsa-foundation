using Elsa.Api.AspNetCore;
using Elsa.Api.Endpoints;
using Elsa.Mediator.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Api.Mediator;

/// <summary>
/// Maps routes onto existing mediator requests and commands.
/// </summary>
/// <remarks>
/// The bridge between the endpoint framework and Elsa's mediator, kept out of the framework project
/// so a consumer without the mediator pays for neither its contracts nor its pipeline dependency.
/// </remarks>
public static class ModuleEndpointGroupMediatorExtensions
{
    /// <summary>Maps a route onto a mediator request that returns a response body.</summary>
    public static IEndpointConventionBuilder MapRequest<TRequest, TResponse>(
        this ModuleEndpointGroup api,
        string method,
        string pattern,
        string operation,
        EndpointBodyMode? bodyMode = null,
        string[]? accepts = null,
        int successStatus = StatusCodes.Status200OK)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull =>
        api.MapOperation<TRequest>(
            method, pattern, operation, bodyMode, accepts, typeof(TResponse), successStatus, null,
            async (context, request, cancellationToken) =>
            {
                var sender = context.RequestServices.GetRequiredService<IRequestSender>();
                var response = await sender.Send(request, cancellationToken);
                await api.WriteJsonAsync(context, response, successStatus);
            });

    /// <summary>Maps a route onto a mediator command that returns a response body.</summary>
    public static IEndpointConventionBuilder MapCommand<TCommand, TResponse>(
        this ModuleEndpointGroup api,
        string method,
        string pattern,
        string operation,
        EndpointBodyMode? bodyMode = null,
        string[]? accepts = null,
        int successStatus = StatusCodes.Status200OK,
        int? documentedStatus = null)
        where TCommand : ICommand<TResponse>
        where TResponse : notnull =>
        api.MapOperation<TCommand>(
            method, pattern, operation, bodyMode, accepts, typeof(TResponse), successStatus, documentedStatus,
            async (context, command, cancellationToken) =>
            {
                var sender = context.RequestServices.GetRequiredService<ICommandSender>();
                var response = await sender.Send(command, cancellationToken);
                await api.WriteJsonAsync(context, response, successStatus);
            });

    /// <summary>Maps a route onto a mediator command that returns no content.</summary>
    public static IEndpointConventionBuilder MapCommand<TCommand>(
        this ModuleEndpointGroup api,
        string method,
        string pattern,
        string operation,
        EndpointBodyMode? bodyMode = null,
        string[]? accepts = null)
        where TCommand : ICommand =>
        api.MapOperation<TCommand>(
            method, pattern, operation, bodyMode, accepts, null, StatusCodes.Status204NoContent, null,
            async (context, command, cancellationToken) =>
            {
                await context.RequestServices.GetRequiredService<ICommandSender>().Send(command, cancellationToken);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });
}
