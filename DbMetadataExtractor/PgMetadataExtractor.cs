using DbMetadataComparator.Export;

using Npgsql;

namespace DbMetadataComparator.Extractor;

public class PgMetadataExtractor : DatabaseMetadataExtractor
{
    public PgMetadataExtractor(string connectionString, bool normalizeTypes)
        : base(connectionString, normalizeTypes)
    {
    }

    public override async Task<DatabaseMetadata> ExtractMetadataAsync()
    {
        var metadata = new DatabaseMetadata { ConnectionString = _connectionString };
        metadata.Tables = await ExtractTablesAsync();
        metadata.Procedures = await ExtractProceduresAsync();
        return metadata;
    }

    private async Task<List<ProcedureInfo>> ExtractProceduresAsync()
    {
        var procedures = new List<ProcedureInfo>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = @"
        SELECT routine_schema, routine_name
        FROM information_schema.routines
        WHERE routine_type = 'PROCEDURE'
          AND routine_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY routine_schema, routine_name";

        await using var cmd = new NpgsqlCommand(query, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            procedures.Add(new ProcedureInfo { Schema = reader.GetString(0), Name = reader.GetString(1) });
        return procedures;
    }

    protected override async Task<List<TableInfo>> ExtractTablesAsync()
    {
        var tables = new List<TableInfo>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var tableQuery = @"
                SELECT table_schema, table_name
                FROM information_schema.tables
                WHERE table_type = 'BASE TABLE'
                  AND table_schema NOT IN ('pg_catalog', 'information_schema')
                ORDER BY table_schema, table_name";

        await using var tableCmd = new NpgsqlCommand(tableQuery, connection);
        await using var tableReader = await tableCmd.ExecuteReaderAsync();
        var tableList = new List<(string schema, string name)>();
        while (await tableReader.ReadAsync())
            tableList.Add((tableReader.GetString(0), tableReader.GetString(1)));
        await tableReader.CloseAsync();

        foreach (var (schema, tableName) in tableList)
        {
            var tableInfo = new TableInfo { Schema = schema, Name = tableName };

            var columnQuery = @"
                    SELECT column_name, data_type, is_nullable,
                           character_maximum_length, numeric_precision, numeric_scale
                    FROM information_schema.columns
                    WHERE table_schema = @schema AND table_name = @tableName
                    ORDER BY ordinal_position";

            await using var colCmd = new NpgsqlCommand(columnQuery, connection);
            colCmd.Parameters.AddWithValue("schema", schema);
            colCmd.Parameters.AddWithValue("tableName", tableName);

            var columnsTemp = new List<(string name, string dataType, string isNullable,
                                        long? maxLength, int? precision, int? scale)>();

            await using (var colReader = await colCmd.ExecuteReaderAsync())
            {
                while (await colReader.ReadAsync())
                {
                    string colName = colReader.GetString(0);
                    string dataType = colReader.GetString(1);
                    string isNullable = colReader.GetString(2);

                    long? maxLength = null;
                    if (!colReader.IsDBNull(3))
                        maxLength = Convert.ToInt64(colReader.GetValue(3));

                    int? precision = null;
                    if (!colReader.IsDBNull(4))
                        precision = Convert.ToInt32(colReader.GetValue(4));

                    int? scale = null;
                    if (!colReader.IsDBNull(5))
                        scale = Convert.ToInt32(colReader.GetValue(5));

                    columnsTemp.Add((colName, dataType, isNullable, maxLength, precision, scale));
                }
            }

            var pkColumns = await GetPrimaryKeyColumnsAsync(connection, schema, tableName);

            foreach (var col in columnsTemp)
            {
                string formattedType = _normalizeTypes
                    ? PgsqlNormalizer.NormalizeType(col.dataType, col.maxLength, col.precision, col.scale)
                    : PgsqlFormatter.FormatType(col.dataType, col.maxLength, col.precision, col.scale);

                tableInfo.Columns.Add(new ColumnInfo
                {
                    Name = col.name,
                    DataType = formattedType,
                    IsNullable = col.isNullable == "YES",
                    IsPrimaryKey = pkColumns.Contains(col.name)
                });
            }

            var indexes = await GetIndexesAsync(connection, schema, tableName);
            tableInfo.Indexes = indexes;

            tables.Add(tableInfo);
        }

        return tables;
    }

    private async Task<List<IndexInfo>> GetIndexesAsync(NpgsqlConnection connection, string schema, string tableName)
    {
        var indexes = new List<IndexInfo>();
        var query = @"
        SELECT 
            i.relname AS indexname,
            ix.indisunique,
            am.amname AS index_type,
            array_agg(a.attname ORDER BY a.attnum) AS columns
        FROM pg_index ix
        JOIN pg_class i ON i.oid = ix.indexrelid
        JOIN pg_class t ON t.oid = ix.indrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        LEFT JOIN pg_attribute a ON a.attrelid = i.oid AND a.attnum > 0
        LEFT JOIN pg_am am ON am.oid = i.relam
        WHERE n.nspname = @schema
          AND t.relname = @tableName
          AND ix.indisprimary = false
        GROUP BY i.relname, ix.indisunique, am.amname
        ORDER BY i.relname";

        await using var cmd = new NpgsqlCommand(query, connection);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", tableName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var idx = new IndexInfo
            {
                Name = reader.GetString(0),
                IsUnique = reader.GetBoolean(1),
                IndexType = reader.GetString(2),
                IsClustered = false   // для PG кластеризация индексов редко используется
            };

            // массив колонок
            var columnsArr = reader.GetValue(3) as string[];
            if (columnsArr != null)
                idx.Columns = columnsArr.ToList();
            indexes.Add(idx);
        }
        return indexes;
    }

    private async Task<HashSet<string>> GetPrimaryKeyColumnsAsync(NpgsqlConnection connection, string schema, string tableName)
    {
        var pk = new HashSet<string>();
        var query = @"
                SELECT kcu.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                    ON tc.constraint_name = kcu.constraint_name
                WHERE tc.constraint_type = 'PRIMARY KEY'
                  AND tc.table_schema = @schema
                  AND tc.table_name = @tableName";

        await using var cmd = new NpgsqlCommand(query, connection);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", tableName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pk.Add(reader.GetString(0));
        return pk;
    }
}

public static class PgsqlFormatter
{
    public static string FormatType(string dataType, long? maxLength, int? precision, int? scale)
    {
        string dt = dataType.ToLowerInvariant();
        if (dt == "character varying" || dt == "varchar" || dt == "character" || dt == "char")
        {
            if (maxLength.HasValue && maxLength > 0)
                return $"{dataType}({maxLength})";
            return dataType;
        }
        if (dt == "numeric" || dt == "decimal")
        {
            if (precision.HasValue && scale.HasValue)
                return $"{dataType}({precision},{scale})";
            if (precision.HasValue)
                return $"{dataType}({precision})";
            return dataType;
        }
        return dataType;
    }
}

public static class PgsqlNormalizer
{
    public static string NormalizeType(string originalDataType, long? maxLength, int? precision, int? scale)
    {
        string type = originalDataType.ToLowerInvariant();

        if (type == "smallint") return "word";
        if (type == "integer") return "dword";
        if (type == "bigint") return "qword";
        if (type == "boolean") return "bool";
        if (type == "uuid") return "guid";

        if (type == "real") return "float";
        if (type == "double precision") return "double";
        if (type == "numeric" || type == "decimal")
        {
            if (precision.HasValue && scale.HasValue) return $"decimal({precision},{scale})";
            if (precision.HasValue) return $"decimal({precision})";
            return "decimal";
        }

        if (type == "character varying" || type == "varchar")
        {
            if (maxLength.HasValue && maxLength > 0) return $"string({maxLength})";
            return "string(MAX)";
        }
        if (type == "character" || type == "char")
        {
            if (maxLength.HasValue && maxLength > 0) return $"fixedstring({maxLength})";
            return "fixedstring";
        }
        if (type == "text") return "string(MAX)";

        if (type == "date") return "date";
        if (type == "time" || type == "time without time zone") return "time";
        if (type == "timestamp" || type == "timestamp without time zone" || type == "timestamptz")
            return "datetime";

        if (type == "bytea") return "binary(MAX)";

        return originalDataType;
    }
}