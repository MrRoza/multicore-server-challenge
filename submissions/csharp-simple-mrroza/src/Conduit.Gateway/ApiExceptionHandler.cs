using Grpc.Core;
using Microsoft.AspNetCore.Diagnostics;

namespace Conduit.Gateway;

/// <summary>Maps unreadable requests and the slices' gRPC failures to HTTP.</summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var result = exception switch
        {
            BadHttpRequestException => ApiError.Result(StatusCodes.Status422UnprocessableEntity, "the request body or parameters are invalid"),
            RpcException { StatusCode: StatusCode.InvalidArgument } e => ApiError.Result(StatusCodes.Status422UnprocessableEntity, e.Status.Detail),
            RpcException { StatusCode: StatusCode.Unauthenticated } e => ApiError.Result(StatusCodes.Status401Unauthorized, e.Status.Detail),
            RpcException { StatusCode: StatusCode.PermissionDenied } e => ApiError.Result(StatusCodes.Status403Forbidden, e.Status.Detail),
            RpcException { StatusCode: StatusCode.NotFound } e => ApiError.Result(StatusCodes.Status404NotFound, e.Status.Detail),
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
