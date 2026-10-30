namespace Conduit.Shared;

/// <summary>A request to change state.</summary>
public interface ICommand<TResponse>;

/// <summary>A request to read state, which changes nothing.</summary>
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command);
}

public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query);
}
