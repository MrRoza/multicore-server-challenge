namespace Conduit.Shared;

/// <summary>A failure with its status code. Sent in RealWorld's shape: {"errors": {"body": ["message"]}}.</summary>
public sealed record Error(int Status, string Message)
{
    public static Error Validation(string message) => new(StatusCodes.Status422UnprocessableEntity, message);
    public static Error Unauthorized(string message) => new(StatusCodes.Status401Unauthorized, message);
    public static Error Forbidden(string message) => new(StatusCodes.Status403Forbidden, message);
    public static Error NotFound(string message) => new(StatusCodes.Status404NotFound, message);

    public static object Body(string message) => new { errors = new { body = new[] { message } } };

    public IResult ToHttp() => Results.Json(Body(Message), statusCode: Status);
}

/// <summary>What a command returns when it has nothing to report.</summary>
public readonly record struct Unit;

/// <summary>The outcome of a command or query: a response or an error.</summary>
public readonly struct Result<T>
{
    readonly T value;
    readonly Error? error;

    Result(T value, Error? error) => (this.value, this.error) = (value, error);

    public static implicit operator Result<T>(T value) => new(value, null);
    public static implicit operator Result<T>(Error error) => new(default!, error);

    public IResult ToHttp(int status = StatusCodes.Status200OK) => error?.ToHttp() ?? Results.Json(value, statusCode: status);

    public IResult ToNoContent() => error?.ToHttp() ?? Results.NoContent();

    /// <summary>Runs the next step, or passes the error along.</summary>
    public async Task<Result<TNext>> Then<TNext>(Func<T, Task<Result<TNext>>> next) =>
        error is null ? await next(value) : error;
}
