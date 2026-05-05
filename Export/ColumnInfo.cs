namespace DbMetadataComparator.Export;

public class ColumnInfo
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public bool IsNullable { get; set; }

    public bool IsPrimaryKey { get; set; }
}
