using Conduit.Shared.Cqrs;
using Microsoft.AspNetCore.Diagnostics;

namespace Conduit.Http;

/// <summary>Maps unreadable requests and expected failures to HTTP. Anything else is logged and is a 500.</summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var result = exception switch
        {
            BadHttpRequestException => ApiError.Result(StatusCodes.Status422UnprocessableEntity, "the request body or parameters are invalid"),
            ConduitException e => ApiError.Result(e.Error.Status, e.Error.Message),
            _ => null,
        };

        if (result is null)
        {
            logger.LogError(exception, "{Method} {Path} failed", context.Request.Method, context.Request.Path);
            result = ApiError.Result(StatusCodes.Status500InternalServerError, "something went wrong");
        }

        await result.ExecuteAsync(context);
        return true;
    }
}
