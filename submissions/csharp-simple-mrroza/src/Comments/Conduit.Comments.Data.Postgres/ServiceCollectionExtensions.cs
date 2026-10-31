using Conduit.Comments.Abstractions;

namespace Conduit.Comments.Data.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommentsPostgres(this IServiceCollection services) =>
        services.AddScoped<ICommentRepository, CommentRepository>();
}
