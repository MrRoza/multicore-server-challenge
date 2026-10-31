namespace Conduit.Tags.Abstractions;

public interface ITagRepository
{
    Task<IEnumerable<string>> List();
}
