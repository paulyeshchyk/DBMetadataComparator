using System.Text.Json;

using DbMetadataComparator.Export;
using DbMetadataComparator.Extractor;
using DbMetadataComparator.Options;

namespace DbMetadataComparator;

class Program
{
    static async Task Main(string[] args)
    {
        string configPath = args.Length > 0 ? args[0] : "connections.json";
        if (!File.Exists(configPath))
        {
            PrintHelp(configPath);
            return;
        }

        string jsonConfig = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<ConnectionConfig>(jsonConfig);
        if (config == null || string.IsNullOrEmpty(config.Source.ConnectionString) || string.IsNullOrEmpty(config.Target.ConnectionString))
        {
            Console.WriteLine("Неверная конфигурация");
            return;
        }

        bool normalizeTypes = config.Options.NormalizeTypes;
        await Run(config, normalizeTypes, config.Source.Name, config.Source.ConnectionString);
        await Run(config, normalizeTypes, config.Target.Name, config.Target.ConnectionString);

        Console.WriteLine("Готово.");
    }

    private static async Task Run(ConnectionConfig config, bool normalizeTypes, string? dbName, string connectionString)
    {
        var db1Type = ConnectionStringHelper.GetDatabaseType(connectionString);
        var extractor = ExtractorBuilder.CreateExtractor(db1Type, connectionString, normalizeTypes);
        if (extractor == null)
        {
            throw new Exception($"Unable to create extractor for: [{connectionString}]");
        }
        Console.WriteLine($"Извлечение метаданных из базы [{connectionString}]...");
        var metadata = await MetadataBuilder.Build(config, extractor);

        await Exporter.ExportAsync(config, dbName, db1Type, metadata);
    }

    private static void PrintHelp(string configPath)
    {
        Console.WriteLine($"Файл конфигурации не найден: {configPath}");
        Console.WriteLine("Создайте connections.json со структурой:");
        Console.WriteLine(SHelpText);
    }

    private const string SHelpText = @"{
        ""db1"": ""..."",
        ""db2"": ""..."",
        ""options"": {
        ""normalizeTypes"": true,
        ""normalizeSchema"": {
            ""dbo"": ""DBO"",
            ""ora_dbo"": ""DBO""
            }
        }
    }";
}