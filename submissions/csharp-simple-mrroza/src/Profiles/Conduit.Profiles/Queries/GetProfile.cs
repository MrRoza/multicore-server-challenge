using Conduit.Profiles.Abstractions;
using Conduit.Shared.Cqrs;

namespace Conduit.Profiles.Queries;

public static class GetProfile
{
    public record Query(long Viewer, string Username) : IQuery<ProfileResponseDto>;

    public sealed class Handler(IProfileRepository profiles) : IQueryHandler<Query, ProfileResponseDto>
    {
        public async Task<Result<ProfileResponseDto>> Handle(Query query)
        {
            var profile = await profiles.Find(query.Viewer, query.Username);
            if (profile is null) return Error.NotFound("user not found");
            return new ProfileResponseDto(profile);
        }
    }
}
