using CodeMap.Storage;

namespace CodeMap.Engine.Indexing;

public static class IncrementalCodeMapIndexerFactory
{
    public static IncrementalCodeMapIndexer Create() => IncrementalCodeMapIndexer.CreateDefault();
}
