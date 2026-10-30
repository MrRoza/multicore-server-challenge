namespace Conduit.Shared.Cqrs;

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
