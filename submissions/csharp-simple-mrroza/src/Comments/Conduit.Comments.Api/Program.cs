using Conduit.Comments;
using Conduit.Comments.Api;
using Conduit.Comments.Data.Postgres;
using Conduit.Shared;

ConduitService.Run(args, services => services
    .AddComments()
    .AddCommentsPostgres()
    .AddCommentsApi());
