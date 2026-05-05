namespace DbMetadataComparator.Export;

public class IndexInfo
{
    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new List<string>();
    public bool IsUnique { get; set; }
    public bool IsClustered { get; set; }     // для MS SQL, для PG не всегда используется
    public string? IndexType { get; set; }    // например, "CLUSTERED", "NONCLUSTERED", "BTREE", "HASH"
}