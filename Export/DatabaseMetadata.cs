namespace DbMetadataComparator.Export;

public class DatabaseMetadata
{
    public string ConnectionString { get; set; } = string.Empty;

    public List<TableInfo> Tables { get; set; } = new List<TableInfo>();

    public List<ProcedureInfo> Procedures { get; set; } = new List<ProcedureInfo>();
}