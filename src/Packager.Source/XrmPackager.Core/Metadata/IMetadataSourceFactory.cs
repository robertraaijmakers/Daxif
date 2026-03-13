namespace XrmPackager.Core.Metadata;

public interface IMetadataSourceFactory
{
    IDataverseMetadataFetcher CreateFetcher(MetadataSourceType type, object config);

    bool SupportsSourceType(MetadataSourceType type);
}
