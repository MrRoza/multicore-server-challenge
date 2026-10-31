namespace Conduit.Shared.Cqrs;

/// <summary>The outcome of a command or query: a response or an error.</summary>
public readonly struct Result<T>
{
    readonly T value;
    readonly Error? error;

    Result(T value, Error? error) => (this.value, this.error) = (value, error);

    public static implicit operator Result<T>(T value) => new(value, null);
    public static implicit operator Result<T>(Error error) => new(default!, error);

    /// <summary>The response, or the error as a gRPC failure.</summary>
    public T Unwrap() => error is null ? value : throw error.ToRpcException();

    /// <summary>Runs the next step, or passes the error along.</summary>
    public async Task<Result<TNext>> Then<TNext>(Func<T, Task<Result<TNext>>> next) =>
        error is null ? await next(value) : error;
}
