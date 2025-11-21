using DbMetaTool.Models;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool.Services.Extraction
{
    public class TableExtractor : MetadataExtractor<Table>
    {
        public TableExtractor(FbConnection connection) : base(connection) { }

        public override List<Table> Extract()
        {
            List<Table> tables = [];

            var tablesCmd = new FbCommand(@"
                SELECT RDB$RELATION_NAME
                FROM RDB$RELATIONS
                WHERE RDB$SYSTEM_FLAG = 0 AND RDB$VIEW_BLR IS NULL", connection);

            using (var reader = tablesCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    Table table = new Table
                    {
                        Name = reader.GetString(0).Trim()
                    };
                    table.Columns ??= [];
                    table.Constraints ??= [];

                    // --- pobierz kolumny będące w PRIMARY KEY (dla inline) ---
                    var pkCmd = new FbCommand(@"
                        SELECT seg.RDB$FIELD_NAME
                        FROM RDB$RELATION_CONSTRAINTS rc
                        JOIN RDB$INDEX_SEGMENTS seg ON rc.RDB$INDEX_NAME = seg.RDB$INDEX_NAME
                        WHERE rc.RDB$RELATION_NAME = @table AND rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'", connection);
                    pkCmd.Parameters.AddWithValue("table", table.Name);

                    var pkCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var pkReader = pkCmd.ExecuteReader())
                    {
                        while (pkReader.Read())
                        {
                            pkCols.Add(pkReader.GetString(0).Trim());
                        }
                    }

                    // kolumny
                    var colsCmd = new FbCommand(@"
                        SELECT rf.RDB$FIELD_NAME, rf.RDB$FIELD_SOURCE, f.RDB$FIELD_TYPE, f.RDB$FIELD_LENGTH,
                               f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, rf.RDB$NULL_FLAG, rf.RDB$DEFAULT_SOURCE
                        FROM RDB$RELATION_FIELDS rf
                        JOIN RDB$FIELDS f ON rf.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
                        WHERE rf.RDB$RELATION_NAME = @table
                        ORDER BY rf.RDB$FIELD_POSITION", connection);

                    colsCmd.Parameters.AddWithValue("table", table.Name);

                    using (var colsReader = colsCmd.ExecuteReader())
                    {
                        var colDefs = new List<string>();
                        while (colsReader.Read())
                        {
                            string colName = colsReader.GetString(0).Trim();
                            string fieldSource = colsReader.GetString(1).Trim();
                            int type = colsReader.GetInt32(2);
                            int length = colsReader.GetInt32(3);
                            short? precision = colsReader.IsDBNull(4) ? null : colsReader.GetInt16(4);
                            short? scale = colsReader.IsDBNull(5) ? null : colsReader.GetInt16(5);
                            bool notNull = !colsReader.IsDBNull(6) && colsReader.GetInt32(6) == 1;
                            string? defaultSrc = colsReader.IsDBNull(7) ? null : colsReader.GetString(7).Trim();

                            string sqlType = !string.IsNullOrEmpty(fieldSource) && !fieldSource.StartsWith("RDB$")
                                ? fieldSource   // domena użytkownika
                                : MapFbType(type, length, precision, scale); // fallback na typ

                            // dopisz PRIMARY KEY inline dla kolumn w PK
                            table.Columns.Add(new Column
                            {
                                Name = colName,
                                DomainOrType = sqlType,
                                PrimaryKey = pkCols.Contains(colName),
                                NotNull = notNull,
                                Default = defaultSrc
                            });
                        }
                    }
                    // constraints (UNIQUE, FOREIGN KEY — bez PRIMARY KEY, bo jest inline)
                    var consCmd = new FbCommand(@"
                        SELECT rc.RDB$CONSTRAINT_NAME, rc.RDB$CONSTRAINT_TYPE,
                               seg.RDB$FIELD_NAME, ref.RDB$CONST_NAME_UQ
                        FROM RDB$RELATION_CONSTRAINTS rc
                        JOIN RDB$INDEX_SEGMENTS seg ON rc.RDB$INDEX_NAME = seg.RDB$INDEX_NAME
                        LEFT JOIN RDB$REF_CONSTRAINTS ref ON rc.RDB$CONSTRAINT_NAME = ref.RDB$CONSTRAINT_NAME
                        WHERE rc.RDB$RELATION_NAME = @table", connection);

                    consCmd.Parameters.AddWithValue("table", table.Name);

                    using (var consReader = consCmd.ExecuteReader())
                    {
                        while (consReader.Read())
                        {
                            string consName = consReader.GetString(0).Trim();
                            string consType = consReader.GetString(1).Trim();
                            string fieldName = consReader.GetString(2).Trim();

                            if (consType == "UNIQUE")
                            {
                                table.Constraints.Add($"{consName} UNIQUE ({fieldName})");
                            }
                            else if (consType == "FOREIGN KEY")
                            {
                                string? refConstraint = consReader.IsDBNull(3) ? null : consReader.GetString(3).Trim();

                                // odczytujemy tabelę i kolumnę docelową referencji
                                string refTable = string.Empty;
                                string refField = string.Empty;
                                if (!string.IsNullOrEmpty(refConstraint))
                                {
                                    var refCmd = new FbCommand(@"
                                        SELECT seg.RDB$FIELD_NAME, rc.RDB$RELATION_NAME
                                        FROM RDB$RELATION_CONSTRAINTS rc
                                        JOIN RDB$INDEX_SEGMENTS seg ON rc.RDB$INDEX_NAME = seg.RDB$INDEX_NAME
                                        WHERE rc.RDB$CONSTRAINT_NAME = @refConstraint", connection);

                                    refCmd.Parameters.AddWithValue("refConstraint", refConstraint);
                                    using (var refReader = refCmd.ExecuteReader())
                                    {
                                        if (refReader.Read())
                                        {
                                            refField = refReader.GetString(0).Trim();
                                            refTable = refReader.GetString(1).Trim();
                                            table.Constraints.Add($"{consName} FOREIGN KEY ({fieldName}) REFERENCES {refTable}({refField})");
                                        }
                                    }
                                }
                            }
                            else if (consType == "CHECK")
                            {
                                // pobierz treść warunku
                                var checkCmd = new FbCommand(@"
                                    SELECT cc.RDB$TRIGGER_SOURCE
                                    FROM RDB$CHECK_CONSTRAINTS cc
                                    WHERE cc.RDB$CONSTRAINT_NAME = @consName", connection);
                                checkCmd.Parameters.AddWithValue("consName", consName);

                                using (var checkReader = checkCmd.ExecuteReader())
                                {
                                    if (checkReader.Read())
                                    {
                                        string triggerSrc = checkReader.GetString(0).Trim();
                                        table.Constraints.Add($"{consName} CHECK {triggerSrc}");
                                    }
                                }
                            }
                        }
                    }

                    tables.Add(table);
                }
            }
            return tables;
        }
    }
}
