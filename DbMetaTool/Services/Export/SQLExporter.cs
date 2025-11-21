using DbMetaTool.Models;
using System.Text;

namespace DbMetaTool.Services.Export
{
    public class SQLExporter : IExporter
    {
        public string ExportHeader(Header header)
        {
            StringBuilder sql = new StringBuilder();
            sql.AppendLine($"SET SQL DIALECT {header.Dialect};");
            sql.AppendLine($"SET NAMES {header.CharSet};");
            sql.AppendLine();
            sql.AppendLine($"CREATE DATABASE '{header.DBFilePath}'");
            sql.AppendLine($"USER '{header.User}' PASSWORD '{header.Password}'");
            sql.AppendLine($"PAGE_SIZE {header.PageSize}");
            sql.AppendLine($"DEFAULT CHARACTER SET {header.CharSet} COLLATION NONE;");
            return sql.ToString().Trim();
        }
        public string ExportDomains(List<Domain> domains)
        {
            StringBuilder sql = new StringBuilder();
            foreach (var domain in domains)
            {
                StringBuilder domainDef = new StringBuilder();
                domainDef.Append($"CREATE DOMAIN {domain.Name} AS {domain.Type}");

                if (!string.IsNullOrWhiteSpace(domain.Default))
                {
                    domainDef.Append($"\n  DEFAULT {domain.Default}");
                }
                if (!string.IsNullOrWhiteSpace(domain.CheckConstraint))
                {
                    domainDef.Append($"\n  CHECK {domain.CheckConstraint}");
                }
                if (domain.NotNull)
                {
                    domainDef.Append("\n  NOT NULL");
                }
                domainDef.Append(';');
                sql.AppendLine(domainDef.ToString());
            }
            return sql.ToString().Trim();
        }
        public string ExportTables(List<Table> tables)
        {
            StringBuilder sql = new StringBuilder();
            foreach (var table in tables)
            {
                StringBuilder tableDef = new StringBuilder();
                tableDef.AppendLine($"CREATE TABLE {table.Name} (");

                // kolumny

                var colDefs = new List<string>();
                colDefs.AddRange(table.Columns.Select(col => $"    {col.Name} {col.DomainOrType}{(col.PrimaryKey ? " PRIMARY KEY" : string.Empty)}"));
                colDefs.AddRange(table.Constraints.Select(cons => $"    CONSTRAINT {cons}"));

                tableDef.AppendLine(string.Join(",\n", colDefs));
                tableDef.AppendLine(");");
                sql.AppendLine(tableDef.ToString());
            }
            return sql.ToString().Trim();
        }
        public string ExportProcedures(List<Procedure> procedures)
        {
            StringBuilder sql = new StringBuilder();

            foreach (Procedure procedure in procedures)
            {
                sql.AppendLine("SET TERM ^ ;");
                sql.AppendLine($"CREATE PROCEDURE {procedure.Name} (");
                if (procedure.Inputs.Count > 0)
                {
                    sql.AppendLine("    " + string.Join(",\n    ", procedure.Inputs.Select(param => $"{param.Name} {param.TypeOrDomain}")));
                }
                sql.AppendLine(")");
                if (procedure.Outputs.Count > 0)
                {
                    sql.AppendLine("RETURNS (");
                    sql.AppendLine("    " + string.Join(",\n    ", procedure.Outputs.Select(param => $"{param.Name} {param.TypeOrDomain}")));
                    sql.AppendLine(")");
                }
                sql.AppendLine("AS");
                sql.AppendLine(procedure.Body + "^");
                sql.AppendLine("SET TERM ; ^");
                sql.AppendLine();
            }
            return sql.ToString().Trim();
        }
    }
}
