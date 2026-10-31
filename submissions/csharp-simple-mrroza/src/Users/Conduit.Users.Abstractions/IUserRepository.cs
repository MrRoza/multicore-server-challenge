namespace Conduit.Users.Abstractions;

public interface IUserRepository
{
    Task<UserRowDto> Create(string username, string email, string passwordHash);
    Task<UserRowDto?> FindByEmail(string email);
    Task<UserRowDto?> FindById(long id);
    Task<UserRowDto?> Update(long id, string? email, string? username, string? passwordHash, string? bio, string? image);
}
