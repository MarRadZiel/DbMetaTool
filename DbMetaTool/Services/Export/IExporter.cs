using DbMetaTool.Models;

namespace DbMetaTool.Services.Export
{
    public interface IExporter
    {
        string ExportHeader(Header header);
        string ExportDomains(List<Domain> domains);
        string ExportTables(List<Table> tables);
        string ExportProcedures(List<Procedure> procedures);
    }
}
