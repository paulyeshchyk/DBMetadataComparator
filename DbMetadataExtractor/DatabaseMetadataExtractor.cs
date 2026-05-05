using DbMetadataComparator.Export;

namespace DbMetadataComparator.Extractor;

public abstract class DatabaseMetadataExtractor
{
    protected readonly string _connectionString;
    protected readonly bool _normalizeTypes;

    protected DatabaseMetadataExtractor(string connectionString, bool normalizeTypes)
    {
        _connectionString = connectionString;
        _normalizeTypes = normalizeTypes;
    }

    public abstract Task<DatabaseMetadata> ExtractMetadataAsync();

    protected abstract Task<List<TableInfo>> ExtractTablesAsync();
}
