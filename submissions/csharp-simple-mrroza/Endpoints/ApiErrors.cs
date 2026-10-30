namespace Conduit.Endpoints;

/// <summary>Errors in RealWorld's shape: {"errors": {"body": ["message"]}}.</summary>
public static class ApiErrors
{
    public static object Body(string message) => new { errors = new { body = new[] { message } } };

    public static IResult Validation(string message) => Results.Json(Body(message), statusCode: StatusCodes.Status422UnprocessableEntity);
    public static IResult Unauthorized(string message) => Results.Json(Body(message), statusCode: StatusCodes.Status401Unauthorized);
    public static IResult Forbidden(string message) => Results.Json(Body(message), statusCode: StatusCodes.Status403Forbidden);
    public static IResult NotFound(string message) => Results.Json(Body(message), statusCode: StatusCodes.Status404NotFound);
}
