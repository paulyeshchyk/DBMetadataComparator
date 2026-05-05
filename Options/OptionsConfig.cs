namespace DbMetadataComparator.Options;

public class OptionsConfig
{
    public bool NormalizeTypes { get; set; } = false;

    public Dictionary<string, string>? NormalizeSchema { get; set; }
}
