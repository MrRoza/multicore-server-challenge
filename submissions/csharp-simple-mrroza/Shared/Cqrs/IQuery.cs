namespace Conduit.Shared.Cqrs;

/// <summary>A request to read state, which changes nothing.</summary>
public interface IQuery<TResponse>;
