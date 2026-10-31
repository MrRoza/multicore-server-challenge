namespace Conduit.Tags.Abstractions;

/// <summary>How other parts of the app use the Tags slice. Failures arrive as exceptions.</summary>
public interface ITagsClient
{
    Task<ListTagsResponseDto> List();
}
