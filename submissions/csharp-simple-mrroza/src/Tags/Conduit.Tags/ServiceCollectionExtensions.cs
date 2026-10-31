using Conduit.Shared;
using Conduit.Tags.Abstractions;
using Conduit.Tags.Queries;

namespace Conduit.Tags;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTags(this IServiceCollection services) => services
        .AddScoped<ITagsClient, TagsClient>()
        .AddQuery<ListTags.Query, ListTagsResponseDto, ListTags.Handler>();
}
