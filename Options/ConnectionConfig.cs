namespace DbMetadataComparator.Options;

public class ConnectionConfig
{
    public required DbConfig Source { get; init; }

    public required DbConfig Target { get; init; }

    public OptionsConfig Options { get; set; } = new OptionsConfig();
}

public class DbConfig
{
    public string ConnectionString { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
