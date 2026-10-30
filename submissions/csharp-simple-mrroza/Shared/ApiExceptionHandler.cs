using Conduit.Shared.Cqrs;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace Conduit.Shared;

/// <summary>Maps unreadable requests and database constraint errors to their status codes.</summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var result = exception switch
        {
            BadHttpRequestException => Error.Validation("the request body or parameters are invalid").ToHttp(),
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => Error.Validation("that value is already taken").ToHttp(),
            PostgresException { SqlState: PostgresErrorCodes.CharacterNotInRepertoire } => Error.Validation("the text is invalid").ToHttp(),
            PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } => Error.NotFound("it no longer exists").ToHttp(),
            _ => null,
        };

        if (result is null)
        {
            logger.LogError(exception, "{Method} {Path} failed", context.Request.Method, context.Request.Path);
            result = Results.Json(Error.Body("something went wrong"), statusCode: StatusCodes.Status500InternalServerError);
        }

        await result.ExecuteAsync(context);
        return true;
    }
}
