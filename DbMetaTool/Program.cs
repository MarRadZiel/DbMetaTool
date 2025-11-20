using FirebirdSql.Data.FirebirdClient;
using System;
using System.IO;
using System.Text;

namespace DbMetaTool
{
    public static class Program
    {
        // Przykładowe wywołania:
        // DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
        // DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
        // DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Użycie:");
                Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
                Console.WriteLine("  export-scripts --connection-string <connStr> --output-dir <ścieżka>");
                Console.WriteLine("  update-db --connection-string <connStr> --scripts-dir <ścieżka>");
                return 1;
            }

            try
            {
                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "build-db":
                        {
                            string dbDir = GetArgValue(args, "--db-dir");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            BuildDatabase(dbDir, scriptsDir);
                            Console.WriteLine("Baza danych została zbudowana pomyślnie.");
                            return 0;
                        }

                    case "export-scripts":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string outputDir = GetArgValue(args, "--output-dir");

                            ExportScripts(connStr, outputDir);
                            Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
                            return 0;
                        }

                    case "update-db":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            UpdateDatabase(connStr, scriptsDir);
                            Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
                            return 0;
                        }

                    default:
                        Console.WriteLine($"Nieznane polecenie: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Błąd: " + ex.Message);
                return -1;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx == -1 || idx + 1 >= args.Length)
                throw new ArgumentException($"Brak wymaganego parametru {name}");
            return args[idx + 1];
        }

        /// <summary>
        /// Buduje nową bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void BuildDatabase(string databaseDirectory, string scriptsDirectory)
        {
            // TODO:
            // 1) Utwórz pustą bazę danych FB 5.0 w katalogu databaseDirectory.
            // 2) Wczytaj i wykonaj kolejno skrypty z katalogu scriptsDirectory
            //    (tylko domeny, tabele, procedury).
            // 3) Obsłuż błędy i wyświetl raport.
            throw new NotImplementedException();
        }

        /// <summary>
        /// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
        /// </summary>
        public static void ExportScripts(string connectionString, string outputDirectory)
        {
            // 1) Połącz się z bazą danych przy użyciu connectionString.
            using var conn = new FbConnection(connectionString);
            conn.Open();

            // 2) Pobierz metadane domen, tabel (z kolumnami) i procedur.
            var sb = new StringBuilder();

            // --- Nagłówek ---
            // Dialect – zawsze 3 w Firebird 5.0
            const int dialect = 3;

            // MON$DATABASE: path + page size
            var monCmd = new FbCommand("SELECT MON$DATABASE_NAME, MON$PAGE_SIZE FROM MON$DATABASE", conn);
            string dbPath = "";
            int pageSize = 16384;
            using (var monReader = monCmd.ExecuteReader())
            {
                if (monReader.Read())
                {
                    dbPath = monReader.GetString(0).Trim();
                    pageSize = monReader.GetInt32(1);
                }
            }

            // RDB$DATABASE: charset
            var csCmd = new FbCommand("SELECT RDB$CHARACTER_SET_NAME FROM RDB$DATABASE", conn);
            string charset = "NONE";
            using (var csReader = csCmd.ExecuteReader())
            {
                if (csReader.Read())
                    charset = csReader.GetString(0).Trim();
            }

            // User + password z connection stringa
            var builder = new FbConnectionStringBuilder(connectionString);
            string user = builder.UserID;
            string password = builder.Password;

            // Header
            sb.AppendLine($"SET SQL DIALECT {dialect};");
            sb.AppendLine($"SET NAMES {charset};");
            sb.AppendLine();
            sb.AppendLine($"CREATE DATABASE '{dbPath}'");
            sb.AppendLine($"USER '{user}' PASSWORD '{password}'");
            sb.AppendLine($"PAGE_SIZE {pageSize}");
            sb.AppendLine($"DEFAULT CHARACTER SET {charset} COLLATION NONE;");
            sb.AppendLine();


            // --- Domeny ---
            var domainsCmd = new FbCommand(@"
        SELECT RDB$FIELD_NAME, RDB$FIELD_TYPE, RDB$FIELD_LENGTH,
               RDB$DEFAULT_SOURCE, RDB$VALIDATION_SOURCE, RDB$NULL_FLAG
        FROM RDB$FIELDS
        WHERE RDB$SYSTEM_FLAG = 0
              AND RDB$FIELD_NAME NOT LIKE 'RDB$%'", conn);

            using (var reader = domainsCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string? defaultSrc = reader["RDB$DEFAULT_SOURCE"] as string;
                    string? checkSrc = reader["RDB$VALIDATION_SOURCE"] as string;
                    object nullFlag = reader["RDB$NULL_FLAG"];

                    string name = reader.GetString(0).Trim();
                    int type = reader.GetInt32(1);
                    int length = reader.GetInt32(2);

                    string sqlType = MapFbType(type, length);

                    var domainDef = new StringBuilder();
                    domainDef.Append($"CREATE DOMAIN {name} AS {sqlType}");

                    if (!string.IsNullOrWhiteSpace(defaultSrc))
                        domainDef.Append($"\n  {defaultSrc.Trim()}");

                    if (!string.IsNullOrWhiteSpace(checkSrc))
                        domainDef.Append($"\n  {checkSrc.Trim()}");

                    if (nullFlag != DBNull.Value)
                        domainDef.Append("\n  NOT NULL");

                    domainDef.Append(';');
                    sb.AppendLine(domainDef.ToString());
                }
            }

            sb.AppendLine();

            // --- Tabele ---
            var tablesCmd = new FbCommand(@"
        SELECT RDB$RELATION_NAME
        FROM RDB$RELATIONS
        WHERE RDB$SYSTEM_FLAG = 0 AND RDB$VIEW_BLR IS NULL", conn);

            using (var reader = tablesCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string tableName = reader.GetString(0).Trim();
                    sb.AppendLine($"CREATE TABLE {tableName} (");

                    // --- pobierz kolumny będące w PRIMARY KEY (dla inline) ---
                    var pkCmd = new FbCommand(@"
            SELECT seg.RDB$FIELD_NAME
            FROM RDB$RELATION_CONSTRAINTS rc
            JOIN RDB$INDEX_SEGMENTS seg ON rc.RDB$INDEX_NAME = seg.RDB$INDEX_NAME
            WHERE rc.RDB$RELATION_NAME = @table AND rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'", conn);
                    pkCmd.Parameters.AddWithValue("table", tableName);

                    var pkCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var pkReader = pkCmd.ExecuteReader())
                    {
                        while (pkReader.Read())
                            pkCols.Add(pkReader.GetString(0).Trim());
                    }

                    // kolumny
                    var colsCmd = new FbCommand(@"
            SELECT rf.RDB$FIELD_NAME, rf.RDB$FIELD_SOURCE,
                   f.RDB$FIELD_TYPE, f.RDB$FIELD_LENGTH
            FROM RDB$RELATION_FIELDS rf
            JOIN RDB$FIELDS f ON rf.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
            WHERE rf.RDB$RELATION_NAME = @table
            ORDER BY rf.RDB$FIELD_POSITION", conn);

                    colsCmd.Parameters.AddWithValue("table", tableName);

                    using var colsReader = colsCmd.ExecuteReader();
                    var colDefs = new List<string>();
                    while (colsReader.Read())
                    {
                        string colName = colsReader.GetString(0).Trim();
                        string fieldSource = colsReader.GetString(1).Trim();
                        int type = colsReader.GetInt32(2);
                        int length = colsReader.GetInt32(3);

                        string sqlType = (!string.IsNullOrEmpty(fieldSource) && !fieldSource.StartsWith("RDB$"))
                            ? fieldSource   // domena użytkownika
                            : MapFbType(type, length); // fallback na typ

                        // dopisz PRIMARY KEY inline dla kolumn w PK
                        if (pkCols.Contains(colName))
                            colDefs.Add($"    {colName} {sqlType} PRIMARY KEY");
                        else
                            colDefs.Add($"    {colName} {sqlType}");
                    }

                    // constraints (UNIQUE, FOREIGN KEY — bez PRIMARY KEY, bo jest inline)
                    var consCmd = new FbCommand(@"
            SELECT rc.RDB$CONSTRAINT_NAME, rc.RDB$CONSTRAINT_TYPE,
                   seg.RDB$FIELD_NAME, ref.RDB$CONST_NAME_UQ
            FROM RDB$RELATION_CONSTRAINTS rc
            JOIN RDB$INDEX_SEGMENTS seg ON rc.RDB$INDEX_NAME = seg.RDB$INDEX_NAME
            LEFT JOIN RDB$REF_CONSTRAINTS ref ON rc.RDB$CONSTRAINT_NAME = ref.RDB$CONSTRAINT_NAME
            WHERE rc.RDB$RELATION_NAME = @table", conn);

                    consCmd.Parameters.AddWithValue("table", tableName);

                    using var consReader = consCmd.ExecuteReader();
                    while (consReader.Read())
                    {
                        string consName = consReader.GetString(0).Trim();
                        string consType = consReader.GetString(1).Trim();
                        string fieldName = consReader.GetString(2).Trim();

                        if (consType == "UNIQUE")
                        {
                            colDefs.Add($"    CONSTRAINT {consName} UNIQUE ({fieldName})");
                        }
                        else if (consType == "FOREIGN KEY")
                        {
                            string? refConstraint = consReader.IsDBNull(3) ? null : consReader.GetString(3).Trim();

                            // odczytujemy tabelę i kolumnę docelową referencji
                            string refTable = "";
                            string refField = "";
                            if (!string.IsNullOrEmpty(refConstraint))
                            {
                                var refCmd = new FbCommand(@"
                        SELECT seg.RDB$FIELD_NAME, rc.RDB$RELATION_NAME
                        FROM RDB$RELATION_CONSTRAINTS rc
                        JOIN RDB$INDEX_SEGMENTS seg ON rc.RDB$INDEX_NAME = seg.RDB$INDEX_NAME
                        WHERE rc.RDB$CONSTRAINT_NAME = @refConstraint", conn);

                                refCmd.Parameters.AddWithValue("refConstraint", refConstraint);
                                using var refReader = refCmd.ExecuteReader();
                                if (refReader.Read())
                                {
                                    refField = refReader.GetString(0).Trim();
                                    refTable = refReader.GetString(1).Trim();
                                }
                            }

                            colDefs.Add($"    CONSTRAINT {consName} FOREIGN KEY ({fieldName}) REFERENCES {refTable}({refField})");
                        }
                        // PRIMARY KEY pomijamy — już dodany inline
                    }

                    sb.AppendLine(string.Join(",\n", colDefs));
                    sb.AppendLine(");");
                    sb.AppendLine();
                }

            }

            // --- Procedury ---
            var procCmd = new FbCommand(@"
SELECT RDB$PROCEDURE_NAME, RDB$PROCEDURE_SOURCE
FROM RDB$PROCEDURES
WHERE RDB$SYSTEM_FLAG = 0", conn);

            using var procReader = procCmd.ExecuteReader();
            while (procReader.Read())
            {
                string procName = procReader.GetString(0).Trim();
                string body = procReader.GetString(1).Trim();

                // pobierz parametry (z typami/domenami)
                var paramCmd = new FbCommand(@"
        SELECT p.RDB$PARAMETER_NAME, p.RDB$PARAMETER_TYPE, 
               p.RDB$FIELD_SOURCE, f.RDB$FIELD_TYPE, f.RDB$FIELD_LENGTH
        FROM RDB$PROCEDURE_PARAMETERS p
        JOIN RDB$FIELDS f ON p.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
        WHERE p.RDB$PROCEDURE_NAME = @proc
        ORDER BY p.RDB$PARAMETER_TYPE, p.RDB$PARAMETER_NUMBER", conn);
                paramCmd.Parameters.AddWithValue("proc", procName);

                var inputs = new List<string>();
                var outputs = new List<string>();

                using var paramReader = paramCmd.ExecuteReader();
                while (paramReader.Read())
                {
                    string paramName = paramReader.GetString(0).Trim();
                    int paramType = paramReader.GetInt32(1); // 0=in, 1=out
                    string fieldSource = paramReader.GetString(2).Trim();
                    int fieldType = paramReader.GetInt32(3);
                    int fieldLength = paramReader.GetInt32(4);

                    string paramTypeName;
                    if (!string.IsNullOrEmpty(fieldSource) && !fieldSource.StartsWith("RDB$"))
                    {
                        // domena użytkownika
                        paramTypeName = fieldSource;
                    }
                    else
                    {
                        // domena systemowa → rozwiń do typu
                        paramTypeName = MapFbType(fieldType, fieldLength);
                    }

                    string paramDef = $"{paramName} {paramTypeName}";
                    if (paramType == 0) inputs.Add(paramDef);
                    else outputs.Add(paramDef);
                }

                sb.AppendLine("SET TERM ^ ;");
                sb.AppendLine($"CREATE PROCEDURE {procName} (");
                if (inputs.Count > 0)
                    sb.AppendLine("    " + string.Join(",\n    ", inputs));
                sb.AppendLine(")");
                if (outputs.Count > 0)
                {
                    sb.AppendLine("RETURNS (");
                    sb.AppendLine("    " + string.Join(",\n    ", outputs));
                    sb.AppendLine(")");
                }
                sb.AppendLine("AS");
                sb.AppendLine(body + "^");
                sb.AppendLine("SET TERM ; ^");
                sb.AppendLine();
            }

            // 3) Wygeneruj pliki .sql / .json / .txt w outputDirectory.
            // --- zapis do pliku ---
            Directory.CreateDirectory(outputDirectory);
            string outputFile = Path.Combine(outputDirectory, "metadata.sql");
            File.WriteAllText(outputFile, sb.ToString());
        }

        // Proste mapowanie typów Firebirda na SQL
        private static string MapFbType(int fieldType, int length)
        {
            return fieldType switch
            {
                7 => "SMALLINT",
                8 => "INTEGER",
                10 => "FLOAT",
                12 => "DATE",
                13 => "TIME",
                14 => $"CHAR({length})",
                37 => $"VARCHAR({length})",
                27 => "DOUBLE PRECISION",
                35 => "TIMESTAMP",
                _ => $"UNKNOWN({fieldType})"
            };
        }


        /// <summary>
        /// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void UpdateDatabase(string connectionString, string scriptsDirectory)
        {
            // TODO:
            // 1) Połącz się z bazą danych przy użyciu connectionString.
            // 2) Wykonaj skrypty z katalogu scriptsDirectory (tylko obsługiwane elementy).
            // 3) Zadbaj o poprawną kolejność i bezpieczeństwo zmian.
            throw new NotImplementedException();
        }
    }
}
