using Conduit;
using Conduit.Auth;
using Conduit.Data;
using Conduit.Endpoints;
using Conduit.Models;

var builder = WebApplication.CreateBuilder(args);

var host = builder.Configuration["HOST"] ?? "127.0.0.1";
var port = builder.Configuration["PORT"] ?? "3000";
builder.WebHost.UseUrls($"http://{host}:{port}");

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddTokenAuth(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new UtcMillisecondsConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapUserEndpoints();
api.MapProfileEndpoints();
api.MapArticleEndpoints();
api.MapCommentEndpoints();
api.MapTagEndpoints();

app.MapFallback(() => ApiErrors.NotFound("not found"));

app.Run();
