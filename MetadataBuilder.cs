using DbMetadataComparator.Export;
using DbMetadataComparator.Extractor;
using DbMetadataComparator.Options;

namespace DbMetadataComparator;

public static class MetadataBuilder
{
    public static async Task<DatabaseMetadata> Build(ConnectionConfig config, DatabaseMetadataExtractor extractor)
    {
        var metadata = await extractor.ExtractMetadataAsync();

        Normalize(config, metadata);

        Resort(metadata);
        return metadata;
    }

    private static void Normalize(ConnectionConfig config, DatabaseMetadata metadata)
    {
        if (config.Options.NormalizeSchema != null)
        {
            var mapping = config.Options.NormalizeSchema;
            foreach (var t in metadata.Tables)
                if (mapping.TryGetValue(t.Schema, out var mapped))
                    t.Schema = mapped;
            foreach (var p in metadata.Procedures)
                if (mapping.TryGetValue(p.Schema, out var mapped))
                    p.Schema = mapped;
        }
    }

    private static void Resort(DatabaseMetadata metadata)
    {
        // Сортировка колонок в таблицах
        foreach (var t in metadata.Tables)
            t.Columns = t.Columns.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // Сортировка таблиц и процедур
        metadata.Tables = metadata.Tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        metadata.Procedures = metadata.Procedures.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var t in metadata.Tables)
            t.Indexes = t.Indexes.OrderBy(idx => idx.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}