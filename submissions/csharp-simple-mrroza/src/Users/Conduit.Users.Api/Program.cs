using Conduit.Users;
using Conduit.Users.Api;
using Conduit.Users.Data.Postgres;
using Conduit.Shared;

ConduitService.Run(args, services => services
    .AddUsers()
    .AddUsersPostgres()
    .AddUsersApi());
