namespace Conduit.Shared;

/// <summary>A slice's routes, kept until the app is built. Routes can't be mapped on an IServiceCollection.</summary>
public sealed record SliceEndpoints(Action<IEndpointRouteBuilder> Map);

public static class SliceRegistration
{
    public static IServiceCollection AddCommand<TCommand, TResponse, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResponse>
        where THandler : class, ICommandHandler<TCommand, TResponse> =>
        services.AddSingleton<ICommandHandler<TCommand, TResponse>, THandler>();

    public static IServiceCollection AddQuery<TQuery, TResponse, THandler>(this IServiceCollection services)
        where TQuery : IQuery<TResponse>
        where THandler : class, IQueryHandler<TQuery, TResponse> =>
        services.AddSingleton<IQueryHandler<TQuery, TResponse>, THandler>();

    public static IServiceCollection AddEndpoints(this IServiceCollection services, Action<IEndpointRouteBuilder> map) =>
        services.AddSingleton(new SliceEndpoints(map));

    /// <summary>Maps the routes of every slice that was added.</summary>
    public static void MapSlices(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        foreach (var slice in app.Services.GetServices<SliceEndpoints>()) slice.Map(api);
    }
}
