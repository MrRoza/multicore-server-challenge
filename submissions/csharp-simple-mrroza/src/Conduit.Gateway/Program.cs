using Conduit.Gateway;
using Conduit.Gateway.Articles;
using Conduit.Gateway.Comments;
using Conduit.Gateway.Profiles;
using Conduit.Gateway.Tags;
using Conduit.Gateway.Users;
using Conduit.Shared;
using Conduit.Shared.Auth;

var builder = WebApplication.CreateBuilder(args);

var host = builder.Configuration["HOST"] ?? "127.0.0.1";
var port = builder.Configuration["PORT"] ?? "3000";
builder.WebHost.UseUrls($"http://{host}:{port}");

builder.Services.AddTokenAuth(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new UtcMillisecondsConverter()));

builder.Services.AddUsersApi(builder.Configuration);
builder.Services.AddProfilesApi(builder.Configuration);
builder.Services.AddArticlesApi(builder.Configuration);
builder.Services.AddCommentsApi(builder.Configuration);
builder.Services.AddTagsApi(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapSlices();
app.MapFallback(() => ApiError.Result(StatusCodes.Status404NotFound, "not found"));

app.Run();
