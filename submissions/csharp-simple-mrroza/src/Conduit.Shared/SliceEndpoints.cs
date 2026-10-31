namespace Conduit.Shared;

/// <summary>A slice's routes, kept until the app is built. Routes can't be mapped on an IServiceCollection.</summary>
public sealed record SliceEndpoints(Action<IEndpointRouteBuilder> Map);
