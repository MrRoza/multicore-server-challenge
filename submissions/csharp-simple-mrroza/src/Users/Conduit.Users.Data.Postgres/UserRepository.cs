using Conduit.Shared.Data.Postgres;
using Conduit.Users.Abstractions;

namespace Conduit.Users.Data.Postgres;

public sealed class UserRepository(PostgresUnitOfWork work) : PostgresRepository(work), IUserRepository
{
    const string Columns = "id, username, email, bio, image";

    public async Task<UserRowDto> Create(string username, string email, string passwordHash)
    {
        return await QuerySingle<UserRowDto>(
            $"INSERT INTO users (username, email, password_hash) VALUES (@username, @email, @passwordHash) RETURNING {Columns}",
            new { username, email, passwordHash });
    }

    public async Task<UserRowDto?> FindByEmail(string email)
    {
        return await QuerySingleOrDefault<UserRowDto>($"SELECT {Columns}, password_hash FROM users WHERE email = @email", new { email });
    }

    public async Task<UserRowDto?> FindById(long id)
    {
        return await QuerySingleOrDefault<UserRowDto>($"SELECT {Columns} FROM users WHERE id = @id", new { id });
    }

    public async Task<UserRowDto?> Update(long id, string? email, string? username, string? passwordHash, string? bio, string? image)
    {
        return await QuerySingleOrDefault<UserRowDto>($"""
            UPDATE users SET email = COALESCE(@email, email), username = COALESCE(@username, username),
                             password_hash = COALESCE(@passwordHash, password_hash),
                             bio = COALESCE(@bio, bio), image = COALESCE(@image, image)
             WHERE id = @id
            RETURNING {Columns}
            """, new { id, email, username, passwordHash, bio, image });
    }
}
