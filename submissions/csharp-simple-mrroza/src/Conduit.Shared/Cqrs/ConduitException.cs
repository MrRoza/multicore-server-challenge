namespace Conduit.Shared.Cqrs;

/// <summary>An expected failure on its way to whoever turns it into a response.</summary>
public sealed class ConduitException(Error error) : Exception(error.Message)
{
    public Error Error { get; } = error;
}
