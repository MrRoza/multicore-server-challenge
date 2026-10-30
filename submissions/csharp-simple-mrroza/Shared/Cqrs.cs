using System.Reflection;

namespace Conduit.Shared;

/// <summary>Changes state. Returns only what it created, such as an id.</summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> Handle(TCommand command);
}

/// <summary>Reads state and changes nothing.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> Handle(TQuery query);
}

/// <summary>A feature slice's route.</summary>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder api);
}

public static class FeatureRegistration
{
    static readonly Type[] Types = Assembly.GetExecutingAssembly().GetTypes();

    /// <summary>Registers every command and query handler in the assembly.</summary>
    public static IServiceCollection AddHandlers(this IServiceCollection services)
    {
        foreach (var type in Types.Where(type => type is { IsClass: true, IsAbstract: false }))
            foreach (var contract in type.GetInterfaces().Where(IsHandlerContract))
                services.AddSingleton(contract, type);
        return services;
    }

    /// <summary>Maps every feature slice in the assembly.</summary>
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder api)
    {
        foreach (var type in Types.Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(IEndpoint))))
            ((IEndpoint)Activator.CreateInstance(type)!).Map(api);
        return api;
    }

    static bool IsHandlerContract(Type contract) =>
        contract.IsGenericType
        && (contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) || contract.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));
}
