using Conduit.Http.Articles;
using Conduit.Http.Comments;
using Conduit.Http.Profiles;
using Conduit.Http.Tags;
using Conduit.Http.Users;
using Conduit.Shared;
using Conduit.Shared.Auth;

namespace Conduit.Http;

/// <summary>The HTTP API, whichever way the slices behind it are reached.</summary>
public static class ConduitHttp
{
    public static void AddConduitHttp(this WebApplicationBuilder builder)
    {
        var host = builder.Configuration["HOST"] ?? "127.0.0.1";
        var port = builder.Configuration["PORT"] ?? "3000";
        builder.WebHost.UseUrls($"http://{host}:{port}");

        builder.Services.AddTokenAuth(builder.Configuration);
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new UtcMillisecondsConverter()));

        builder.Services.AddUsersApi();
        builder.Services.AddProfilesApi();
        builder.Services.AddArticlesApi();
        builder.Services.AddCommentsApi();
        builder.Services.AddTagsApi();
    }

    public static void UseConduitHttp(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapSlices();
        app.MapFallback(() => ApiError.Result(StatusCodes.Status404NotFound, "not found"));
    }
}
