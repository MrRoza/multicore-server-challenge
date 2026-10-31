using Conduit.Articles;
using Conduit.Articles.Api;
using Conduit.Articles.Data.Postgres;
using Conduit.Shared;

ConduitService.Run(args, services => services
    .AddArticles()
    .AddArticlesPostgres()
    .AddArticlesApi());
