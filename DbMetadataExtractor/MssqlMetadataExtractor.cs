using DbMetadataComparator.Export;

using Microsoft.Data.SqlClient;

namespace DbMetadataComparator.Extractor;

public class MssqlMetadataExtractor : DatabaseMetadataExtractor
{
    public MssqlMetadataExtractor(string connectionString, bool normalizeTypes) : base(connectionString, normalizeTypes)
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
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = @"
        SELECT ROUTINE_SCHEMA, ROUTINE_NAME
        FROM INFORMATION_SCHEMA.ROUTINES
        WHERE ROUTINE_TYPE = 'PROCEDURE'
          AND ROUTINE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
        ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME";

        using var cmd = new SqlCommand(query, connection);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            procedures.Add(new ProcedureInfo { Schema = reader.GetString(0), Name = reader.GetString(1) });
        return procedures;
    }

    protected override async Task<List<TableInfo>> ExtractTablesAsync()
    {
        var tables = new List<TableInfo>();

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var tableQuery = @"
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE'
                  AND TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
                ORDER BY TABLE_SCHEMA, TABLE_NAME";

        using var tableCmd = new SqlCommand(tableQuery, connection);
        using var tableReader = await tableCmd.ExecuteReaderAsync();
        var tableList = new List<(string schema, string name)>();
        while (await tableReader.ReadAsync())
            tableList.Add((tableReader.GetString(0), tableReader.GetString(1)));
        await tableReader.CloseAsync();

        foreach (var (schema, tableName) in tableList)
        {
            var tableInfo = new TableInfo { Schema = schema, Name = tableName };

            var columnQuery = @"
                    SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE,
                           CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @tableName
                    ORDER BY ORDINAL_POSITION";

            using var colCmd = new SqlCommand(columnQuery, connection);
            colCmd.Parameters.AddWithValue("@schema", schema);
            colCmd.Parameters.AddWithValue("@tableName", tableName);

            var columnsTemp = new List<(string name, string dataType, string isNullable,
                                        long? maxLength, int? precision, int? scale)>();

            using (var colReader = await colCmd.ExecuteReaderAsync())
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
                    ? MssqlNormalizer.NormalizeType(col.dataType, col.maxLength, col.precision, col.scale)
                    : MssqlFormatter.FormatType(col.dataType, col.maxLength, col.precision, col.scale);

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

    private async Task<List<IndexInfo>> GetIndexesAsync(SqlConnection connection, string schema, string tableName)
    {
        var indexes = new List<IndexInfo>();
        var query = @"
        SELECT 
            i.name AS IndexName,
            i.is_unique,
            i.type_desc AS IndexType,
            STUFF((
                SELECT ',' + c.name
                FROM sys.index_columns ic2
                INNER JOIN sys.columns c ON ic2.object_id = c.object_id AND ic2.column_id = c.column_id
                WHERE ic2.object_id = i.object_id 
                  AND ic2.index_id = i.index_id
                  AND ic2.key_ordinal > 0
                ORDER BY ic2.key_ordinal
                FOR XML PATH('')
            ), 1, 1, '') AS Columns
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(@schema + '.' + @tableName)
          AND i.is_primary_key = 0
        ORDER BY i.name";

        using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@tableName", tableName);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(0))
                continue;

            var idx = new IndexInfo
            {
                Name = reader.GetString(0),
                IsUnique = reader.GetBoolean(1),
                IndexType = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsClustered = !reader.IsDBNull(2) && reader.GetString(2) == "CLUSTERED"
            };

            // Обработка списка колонок: если NULL, то пустой список
            string columnsStr = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            idx.Columns = columnsStr.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            indexes.Add(idx);
        }
        return indexes;
    }

    private async Task<HashSet<string>> GetPrimaryKeyColumnsAsync(SqlConnection connection, string schema, string tableName)
    {
        var pk = new HashSet<string>();
        var query = @"
                SELECT kcu.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                    ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                  AND tc.TABLE_SCHEMA = @schema
                  AND tc.TABLE_NAME = @tableName";

        using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@tableName", tableName);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pk.Add(reader.GetString(0));
        return pk;
    }
}

public static class MssqlFormatter
{
    public static string FormatType(string dataType, long? maxLength, int? precision, int? scale)
    {
        string dt = dataType.ToLowerInvariant();
        if (dt == "varchar" || dt == "char" || dt == "nvarchar" || dt == "nchar" || dt == "varbinary" || dt == "binary")
        {
            if (maxLength.HasValue && maxLength == -1)
                return $"{dataType}(MAX)";
            if (maxLength.HasValue && maxLength > 0)
                return $"{dataType}({maxLength})";
            return dataType;
        }
        if (dt == "decimal" || dt == "numeric")
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

public class MssqlNormalizer
{
    public static string NormalizeType(string originalDataType, long? maxLength, int? precision, int? scale)
    {
        string type = originalDataType.ToLowerInvariant();

        if (type == "tinyint" || type == "smallint") return "word";
        if (type == "int") return "dword";
        if (type == "bigint") return "qword";
        if (type == "bit") return "bool";
        if (type == "uniqueidentifier") return "guid";

        if (type == "float" || type == "real") return "float";
        if (type == "double") return "double";
        if (type == "decimal" || type == "numeric")
        {
            if (precision.HasValue && scale.HasValue) return $"decimal({precision},{scale})";
            if (precision.HasValue) return $"decimal({precision})";
            return "decimal";
        }

        if (type == "varchar" || type == "nvarchar")
        {
            if (maxLength.HasValue && maxLength == -1) return "string(MAX)";
            if (maxLength.HasValue && maxLength > 0) return $"string({maxLength})";
            return "string(MAX)";
        }
        if (type == "char" || type == "nchar")
        {
            if (maxLength.HasValue && maxLength > 0) return $"fixedstring({maxLength})";
            return "fixedstring";
        }
        if (type == "text" || type == "ntext") return "string(MAX)";

        if (type == "date") return "date";
        if (type == "time") return "time";
        if (type == "datetime" || type == "datetime2") return "datetime";
        if (type == "smalldatetime") return "datetime";

        if (type == "binary" || type == "varbinary")
        {
            if (maxLength.HasValue && maxLength == -1) return "binary(MAX)";
            if (maxLength.HasValue && maxLength > 0) return $"binary({maxLength})";
            return "binary(MAX)";
        }
        if (type == "image") return "binary(MAX)";

        return originalDataType;
    }
}
