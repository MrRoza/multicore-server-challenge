using Conduit.Shared.Cqrs;
using Conduit.Shared.Cqrs.Batching;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Conduit.Shared;

public static class SliceRegistration
{
    public static IServiceCollection AddQueryBatching(this IServiceCollection services, IConfiguration configuration) => services
        .AddSingleton<QueryLoad>()
        .Configure<QueryBatchingOptions>(configuration);

    public static IServiceCollection AddCommand<TCommand, TResponse, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResponse>
        where THandler : class, ICommandHandler<TCommand, TResponse> =>
        services.AddScoped<ICommandHandler<TCommand, TResponse>, THandler>();

    /// <param name="overrule">Changes the global batching options for this query only.</param>
    public static IServiceCollection AddQuery<TQuery, TResponse, THandler>(this IServiceCollection services, Func<QueryBatchingOptions, QueryBatchingOptions>? overrule = null)
        where TQuery : notnull, IQuery<TResponse>
        where THandler : class, IQueryHandler<TQuery, TResponse>
    {
        services.TryAddScoped<THandler>(); // One handler class can answer several queries
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<QueryBatchingOptions>>().Value;
            return new QueryBatcher<TQuery, TResponse, THandler>(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<QueryLoad>(),
                overrule is null ? options : overrule(options));
        });
        return services.AddScoped<IQueryHandler<TQuery, TResponse>, BatchedQueryHandler<TQuery, TResponse, THandler>>();
    }

    public static IServiceCollection AddEndpoints(this IServiceCollection services, Action<IEndpointRouteBuilder> map) =>
        services.AddSingleton(new SliceEndpoints(map));

    /// <summary>Maps the routes of every slice that was added.</summary>
    public static void MapSlices(this WebApplication app)
    {
        foreach (var slice in app.Services.GetServices<SliceEndpoints>()) slice.Map(app);
    }
}
