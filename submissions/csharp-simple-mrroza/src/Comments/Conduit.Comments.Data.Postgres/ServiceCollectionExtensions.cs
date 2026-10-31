using Conduit.Comments.Abstractions;
using Conduit.Shared.Data.Postgres;

namespace Conduit.Comments.Data.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommentsPostgres(this IServiceCollection services) =>
        services.AddPostgres().AddScoped<ICommentRepository, CommentRepository>();
}
