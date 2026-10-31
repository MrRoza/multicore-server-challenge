using Grpc.Core;
using Grpc.Core.Interceptors;
using Npgsql;

namespace Conduit.Shared.Data.Postgres;

/// <summary>Turns database constraint errors into gRPC statuses, which the gateway maps to HTTP.</summary>
public sealed class DatabaseErrorInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.UniqueViolation)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "that value is already taken"));
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.CharacterNotInRepertoire)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "the text is invalid"));
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "it no longer exists"));
        }
    }
}
