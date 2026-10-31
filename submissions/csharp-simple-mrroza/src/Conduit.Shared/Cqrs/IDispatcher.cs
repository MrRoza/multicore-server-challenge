namespace Conduit.Shared.Cqrs;

/// <summary>Sends a command or query to its handler and returns the response.</summary>
public interface IDispatcher
{
    Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command);
    Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query);
}
