using Conduit.Profiles;
using Conduit.Profiles.Api;
using Conduit.Profiles.Data.Postgres;
using Conduit.Shared;

ConduitService.Run(args, services => services
    .AddProfiles()
    .AddProfilesPostgres()
    .AddProfilesApi());
