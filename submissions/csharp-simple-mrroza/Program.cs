using Conduit.Features.Articles;
using Conduit.Features.Comments;
using Conduit.Features.Profiles;
using Conduit.Features.Tags;
using Conduit.Features.Users;
using Conduit.Shared;

var builder = WebApplication.CreateBuilder(args);

var host = builder.Configuration["HOST"] ?? "127.0.0.1";
var port = builder.Configuration["PORT"] ?? "3000";
builder.WebHost.UseUrls($"http://{host}:{port}");

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddTokenAuth(builder.Configuration);
builder.Services.AddSingleton<IDispatcher, Dispatcher>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new UtcMillisecondsConverter()));

builder.Services.AddUsers();
builder.Services.AddProfiles();
builder.Services.AddArticles();
builder.Services.AddComments();
builder.Services.AddTags();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapSlices();
app.MapFallback(() => Error.NotFound("not found").ToHttp());

app.Run();
