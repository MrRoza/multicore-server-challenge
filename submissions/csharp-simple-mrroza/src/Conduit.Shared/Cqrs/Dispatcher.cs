using System.Collections.Concurrent;
using System.Reflection;

namespace Conduit.Shared.Cqrs;

public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    // One delegate per request type, so reflection only runs the first time that type is dispatched.
    static readonly ConcurrentDictionary<Type, Delegate> Routes = new();

    public Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command) => Dispatch<TResponse>(nameof(RunCommand), command);

    public Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query) => Dispatch<TResponse>(nameof(RunQuery), query);

    Task<Result<TResponse>> Dispatch<TResponse>(string run, object request)
    {
        var route = Routes.GetOrAdd(request.GetType(), type => typeof(Dispatcher)
            .GetMethod(run, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type, typeof(TResponse))
            .CreateDelegate<Func<IServiceProvider, object, Task<Result<TResponse>>>>());
        return ((Func<IServiceProvider, object, Task<Result<TResponse>>>)route)(services, request);
    }

    static Task<Result<TResponse>> RunCommand<TCommand, TResponse>(IServiceProvider services, object command)
        where TCommand : ICommand<TResponse> =>
        services.GetRequiredService<ICommandHandler<TCommand, TResponse>>().Handle((TCommand)command);

    static Task<Result<TResponse>> RunQuery<TQuery, TResponse>(IServiceProvider services, object query)
        where TQuery : IQuery<TResponse> =>
        services.GetRequiredService<IQueryHandler<TQuery, TResponse>>().Handle((TQuery)query);
}
