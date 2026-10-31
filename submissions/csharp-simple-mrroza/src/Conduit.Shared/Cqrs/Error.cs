using Grpc.Core;

namespace Conduit.Shared.Cqrs;

/// <summary>A failure with the gRPC status that carries it to the gateway.</summary>
public sealed record Error(StatusCode Status, string Message)
{
    public static Error Validation(string message) => new(StatusCode.InvalidArgument, message);
    public static Error Unauthorized(string message) => new(StatusCode.Unauthenticated, message);
    public static Error Forbidden(string message) => new(StatusCode.PermissionDenied, message);
    public static Error NotFound(string message) => new(StatusCode.NotFound, message);

    public RpcException ToRpcException() => new(new Status(Status, Message));
}
