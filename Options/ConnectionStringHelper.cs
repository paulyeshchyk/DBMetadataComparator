using DbMetadataComparator.Extractor;

namespace DbMetadataComparator.Options;

public static class ConnectionStringHelper
{
    public static DatabaseType GetDatabaseType(string connectionString, string? explicitType = null)
    {
        return string.IsNullOrEmpty(explicitType)
            ? ConnectionStringHelper.DetectDatabaseType(connectionString)
            : ConnectionStringHelper.StringToDatabaseType(explicitType);
    }

    public static DatabaseType DetectDatabaseType(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return DatabaseType.Unknown;

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var keys = parts.Select(p => p.Split('=')[0].Trim().ToLowerInvariant()).ToHashSet();

        // Признаки MS SQL
        bool hasMssqlKeys = keys.Contains("server") || keys.Contains("data source") ||
                            keys.Contains("trustservercertificate") || keys.Contains("integrated security");

        // Признаки PostgreSQL
        bool hasPgKeys = keys.Contains("host") && keys.Contains("port") ||
                         keys.Contains("search path") || keys.Contains("pooling");

        if (hasMssqlKeys && !hasPgKeys)
            return DatabaseType.MsSql;
        if (hasPgKeys && !hasMssqlKeys)
            return DatabaseType.PostgreSql;

        // Если смешаны – пытаемся по дополнительным признакам
        if (keys.Contains("database"))
        {
            if (!keys.Contains("host"))
                return DatabaseType.MsSql;   // Есть database, нет host – вероятно MS SQL
            if (!keys.Contains("server") && !keys.Contains("data source"))
                return DatabaseType.PostgreSql; // Есть host и database, нет server – PG
        }

        return DatabaseType.Unknown;
    }

    public static DatabaseType StringToDatabaseType(string explicitType)
    {
        return explicitType.ToLowerInvariant() switch
        {
            "mssql" => DatabaseType.MsSql,
            "postgresql" => DatabaseType.PostgreSql,
            _ => DatabaseType.Unknown
        };
    }
}