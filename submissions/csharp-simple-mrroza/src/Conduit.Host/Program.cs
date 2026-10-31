using Conduit.Articles;
using Conduit.Articles.Data.Postgres;
using Conduit.Comments;
using Conduit.Comments.Data.Postgres;
using Conduit.Http;
using Conduit.Profiles;
using Conduit.Profiles.Data.Postgres;
using Conduit.Shared.Cqrs;
using Conduit.Tags;
using Conduit.Tags.Data.Postgres;
using Conduit.Users;
using Conduit.Users.Data.Postgres;

var builder = WebApplication.CreateBuilder(args);

builder.AddConduitHttp();
builder.Services.AddScoped<IDispatcher, Dispatcher>();

builder.Services.AddUsers().AddUsersPostgres();
builder.Services.AddProfiles().AddProfilesPostgres();
builder.Services.AddArticles().AddArticlesPostgres();
builder.Services.AddComments().AddCommentsPostgres();
builder.Services.AddTags().AddTagsPostgres();

var app = builder.Build();
app.UseConduitHttp();
app.Run();
