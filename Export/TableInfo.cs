namespace DbMetadataComparator.Export;

public class TableInfo
{
    public string Schema { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();

    public List<IndexInfo> Indexes { get; set; } = new List<IndexInfo>();
}