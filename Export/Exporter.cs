using System.Text.Json;

using DbMetadataComparator.Extractor;
using DbMetadataComparator.Options;

namespace DbMetadataComparator.Export;

public static class Exporter
{
    public static async Task ExportAsync(ConnectionConfig config, string? Db1Name, DatabaseType db1Type, DatabaseMetadata metadata)
    {
        string folder1 = Db1Name ?? GetDefaultFolderName(db1Type);

        string resultDir = "result";
        string combinedDir = Path.Combine(resultDir, folder1);

        Directory.CreateDirectory(combinedDir);

        var optionsJson = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(combinedDir, "metadata.json"), JsonSerializer.Serialize(metadata, optionsJson));

        Console.WriteLine($"Сохранено {metadata.Tables.Count} таблиц и {metadata.Procedures.Count} процедур в {Path.Combine(combinedDir, "metadata.json")}");
    }

    private static string GetDefaultFolderName(DatabaseType type)
    {
        return type switch
        {
            DatabaseType.MsSql => "mssql",
            DatabaseType.PostgreSql => "postgresql",
            _ => "unknown"
        };
    }
}
