namespace Conduit.Http;

/// <summary>Errors in RealWorld's shape: {"errors": {"body": ["message"]}}.</summary>
public static class ApiError
{
    public static IResult Result(int status, string message) =>
        Results.Json(new { errors = new { body = new[] { message } } }, statusCode: status);
}
