using Conduit.Endpoints;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace Conduit;

/// <summary>Maps unreadable requests and database constraint errors to their status codes.</summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var result = exception switch
        {
            BadHttpRequestException => ApiErrors.Validation("the request body or parameters are invalid"),
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => ApiErrors.Validation("that value is already taken"),
            PostgresException { SqlState: PostgresErrorCodes.CharacterNotInRepertoire } => ApiErrors.Validation("the text is invalid"),
            PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } => ApiErrors.NotFound("it no longer exists"),
            _ => null,
        };

        if (result is null)
        {
            logger.LogError(exception, "{Method} {Path} failed", context.Request.Method, context.Request.Path);
            result = Results.Json(ApiErrors.Body("something went wrong"), statusCode: StatusCodes.Status500InternalServerError);
        }

        await result.ExecuteAsync(context);
        return true;
    }
}
