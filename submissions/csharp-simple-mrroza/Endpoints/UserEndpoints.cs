using System.Security.Claims;
using System.Text;
using Conduit.Auth;
using Conduit.Models;
using Dapper;
using Npgsql;

namespace Conduit.Endpoints;

public static class UserEndpoints
{
    const string UserColumns = "id, username, email, bio, image";

    public static void MapUserEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/users", Register);
        api.MapPost("/users/login", Login);
        api.MapGet("/user", GetCurrent).AuthRequired();
        api.MapPut("/user", Update).AuthRequired();
    }

    static async Task<IResult> Register(RegisterRequest request, NpgsqlDataSource data, TokenService tokens)
    {
        var user = request.User;
        if (user is null || string.IsNullOrEmpty(user.Username) || string.IsNullOrEmpty(user.Email) || string.IsNullOrEmpty(user.Password))
            return ApiErrors.Validation("username, email and password are required");
        if (TooLongForBcrypt(user.Password)) return ApiErrors.Validation("password is over 72 bytes");

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(user.Password, 10);
        await using var db = await data.OpenConnectionAsync();
        var row = await db.QuerySingleAsync<UserRow>(
            $"INSERT INTO users (username, email, password_hash) VALUES (@Username, @Email, @passwordHash) RETURNING {UserColumns}",
            new { user.Username, user.Email, passwordHash });
        return Results.Json(new UserResponse(row.ToUser(tokens.Create(row.Id))), statusCode: StatusCodes.Status201Created);
    }

    static async Task<IResult> Login(LoginRequest request, NpgsqlDataSource data, TokenService tokens)
    {
        var user = request.User;
        if (user is null || string.IsNullOrEmpty(user.Email) || string.IsNullOrEmpty(user.Password))
            return ApiErrors.Validation("email and password are required");

        await using var db = await data.OpenConnectionAsync();
        var row = await db.QuerySingleOrDefaultAsync<UserRow>(
            $"SELECT {UserColumns}, password_hash FROM users WHERE email = @Email", new { user.Email });
        if (row is null || TooLongForBcrypt(user.Password) || !BCrypt.Net.BCrypt.Verify(user.Password, row.PasswordHash))
            return ApiErrors.Unauthorized("wrong email or password");
        return Results.Ok(new UserResponse(row.ToUser(tokens.Create(row.Id))));
    }

    static async Task<IResult> GetCurrent(ClaimsPrincipal principal, HttpRequest http, NpgsqlDataSource data)
    {
        await using var db = await data.OpenConnectionAsync();
        var row = await db.QuerySingleOrDefaultAsync<UserRow>(
            $"SELECT {UserColumns} FROM users WHERE id = @id", new { id = principal.UserId() });
        return row is null
            ? ApiErrors.Unauthorized("this user no longer exists")
            : Results.Ok(new UserResponse(row.ToUser(TokenAuth.PresentedToken(http)!)));
    }

    static async Task<IResult> Update(UpdateUserRequest request, ClaimsPrincipal principal, HttpRequest http, NpgsqlDataSource data)
    {
        var user = request.User;
        if (user is null) return ApiErrors.Validation("user is required");
        if (user.Email == "" || user.Username == "" || user.Password == "") return ApiErrors.Validation("email, username and password can't be empty");
        if (user.Password is not null && TooLongForBcrypt(user.Password)) return ApiErrors.Validation("password is over 72 bytes");

        var passwordHash = user.Password is null ? null : BCrypt.Net.BCrypt.HashPassword(user.Password, 10);
        await using var db = await data.OpenConnectionAsync();
        var row = await db.QuerySingleOrDefaultAsync<UserRow>($"""
            UPDATE users SET email = COALESCE(@Email, email), username = COALESCE(@Username, username),
                             password_hash = COALESCE(@passwordHash, password_hash),
                             bio = COALESCE(@Bio, bio), image = COALESCE(@Image, image)
             WHERE id = @id
            RETURNING {UserColumns}
            """, new { id = principal.UserId(), user.Email, user.Username, passwordHash, user.Bio, user.Image });
        return row is null
            ? ApiErrors.Unauthorized("this user no longer exists")
            : Results.Ok(new UserResponse(row.ToUser(TokenAuth.PresentedToken(http)!)));
    }

    // bcrypt only uses the first 72 bytes, so a longer password is refused instead of silently cut.
    static bool TooLongForBcrypt(string password) => Encoding.UTF8.GetByteCount(password) > 72;
}
