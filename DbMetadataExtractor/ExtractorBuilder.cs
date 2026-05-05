namespace DbMetadataComparator.Extractor;

public static class ExtractorBuilder
{
    public static DatabaseMetadataExtractor? CreateExtractor(DatabaseType type, string connectionString, bool normalizeTypes)
    {
        return type switch
        {
            DatabaseType.MsSql => new MssqlMetadataExtractor(connectionString, normalizeTypes),
            DatabaseType.PostgreSql => new PgMetadataExtractor(connectionString, normalizeTypes),
            _ => null
        };
    }
}
