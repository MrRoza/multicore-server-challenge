using Conduit.Tags;
using Conduit.Tags.Api;
using Conduit.Tags.Data.Postgres;
using Conduit.Shared;

ConduitService.Run(args, services => services
    .AddTags()
    .AddTagsPostgres()
    .AddTagsApi());
