using DbMetaTool.Services.Export;
using DbMetaTool.Services.Extraction;
using FirebirdSql.Data.FirebirdClient;
using System.Text;
using System.Text.RegularExpressions;

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
            StringBuilder report = new StringBuilder("-- Build Database Report--\n");

            // 1) Utwórz pustą bazę danych FB 5.0 w katalogu databaseDirectory.

            string headerPath = Path.Combine(scriptsDirectory, "header.sql");
            if (File.Exists(headerPath))
            {
                string headerScript = File.ReadAllText(headerPath);

                // USER / PASSWORD
                var userMatch = Regex.Match(headerScript, @"USER\s+'?(\w+)'?", RegexOptions.IgnoreCase);
                var passMatch = Regex.Match(headerScript, @"PASSWORD\s+'?(\w+)'?", RegexOptions.IgnoreCase);

                string user = userMatch.Success ? userMatch.Groups[1].Value : "SYSDBA";
                string password = passMatch.Success ? passMatch.Groups[1].Value : "masterkey";

                // PAGE_SIZE
                var pageMatch = Regex.Match(headerScript, @"PAGE_SIZE\s+(\d+)", RegexOptions.IgnoreCase);
                int pageSize = pageMatch.Success && int.TryParse(pageMatch.Groups[1].Value, out int psize) ? psize : 8192;

                // CHARACTER SET
                var charsetMatch = Regex.Match(headerScript, @"CHARACTER\s+SET\s+(\w+)", RegexOptions.IgnoreCase);
                string charset = charsetMatch.Success ? charsetMatch.Groups[1].Value : "NONE";

                string dbPath = Path.Combine(databaseDirectory, "database.fdb");

                string connectionString = $"DataSource=localhost;User={user};Password={password};Database={dbPath};ServerType=0;";

                // utworzenie bazy
                try
                {
                    FbConnection.CreateDatabase($"{connectionString}Charset={charset};", pageSize: pageSize, overwrite: true);
                    report.AppendLine("OK: empty database created");
                }
                catch (Exception ex)
                {
                    report.AppendLine($"ERROR: database creation error\n  {ex.Message}");
                    Console.WriteLine(report.ToString());
                    throw;
                }

                // 2) Wczytaj i wykonaj kolejno skrypty z katalogu scriptsDirectory
                //    (tylko domeny, tabele, procedury).
                var orderedFiles = Directory.GetFiles(scriptsDirectory, "*.sql").Where(file => !string.Equals(Path.GetFileNameWithoutExtension(file), "header", StringComparison.InvariantCultureIgnoreCase))
                    .OrderBy(f =>
                    {
                        var name = Path.GetFileName(f).ToLowerInvariant();
                        if (name.Contains("domains")) return 1;
                        if (name.Contains("tables")) return 2;
                        if (name.Contains("procedures")) return 3;
                        if (name.Contains("triggers")) return 4;
                        return 5;
                    });
                try
                {
                    using (var dbConn = new FbConnection(connectionString))
                    {
                        dbConn.Open();


                        foreach (var file in orderedFiles)
                        {
                            string script = File.ReadAllText(file);
                            ExecuteScript(dbConn, script, false, Path.GetFileName(file), report);
                        }
                    }
                }
                catch
                {
                    Console.WriteLine(report.ToString());
                    throw;
                }
            }
            else
            {
                report.AppendLine($"ERROR: no header file at {headerPath}");
                Console.WriteLine(report.ToString());
                throw new Exception($"ERROR: no header file at {headerPath}");
            }

            // 3) Obsłuż błędy i wyświetl raport.
            Console.WriteLine(report.ToString());
        }

        /// <summary>
        /// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
        /// </summary>
        public static void ExportScripts(string connectionString, string outputDirectory)
        {
            // 1) Połącz się z bazą danych przy użyciu connectionString.
            using (var conn = new FbConnection(connectionString))
            {
                conn.Open();

                // 2) Pobierz metadane domen, tabel (z kolumnami) i procedur.
                string metadataFilesFormat = "sql";
                IExporter exporter;
                try
                {
                    exporter = ExporterFactory.CreateExporter(metadataFilesFormat);
                }
                catch // specified format is unsupported
                {
                    // fallback to sql format
                    exporter = new SQLExporter();
                    metadataFilesFormat = "sql";
                }

                var header = new HeaderExtractor(conn).Extract().FirstOrDefault();
                var domains = new DomainExtractor(conn).Extract();
                var tables = new TableExtractor(conn).Extract();
                var procedures = new ProcedureExtractor(conn).Extract();

                // 3) Wygeneruj pliki .sql / .json / .txt w outputDirectory.
                Directory.CreateDirectory(outputDirectory);

                if (header != null)
                {
                    File.WriteAllText(Path.Combine(outputDirectory, $"header.{metadataFilesFormat}"), exporter.ExportHeader(header));
                }
                if (domains != null && domains.Count > 0)
                {
                    File.WriteAllText(Path.Combine(outputDirectory, $"domains.{metadataFilesFormat}"), exporter.ExportDomains(domains));
                }
                if (tables != null && tables.Count > 0)
                {
                    File.WriteAllText(Path.Combine(outputDirectory, $"tables.{metadataFilesFormat}"), exporter.ExportTables(tables));
                }
                if (procedures != null && procedures.Count > 0)
                {
                    File.WriteAllText(Path.Combine(outputDirectory, $"procedures.{metadataFilesFormat}"), exporter.ExportProcedures(procedures));
                }
            }
        }

        /// <summary>
        /// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void UpdateDatabase(string connectionString, string scriptsDirectory)
        {
            var report = new StringBuilder("-- Update Database Report--\n");

            // 3) Zadbaj o poprawną kolejność
            var orderedFiles = Directory.GetFiles(scriptsDirectory, "*.sql").Where(file => !string.Equals(Path.GetFileNameWithoutExtension(file), "header", StringComparison.InvariantCultureIgnoreCase))
                .OrderBy(f =>
                {
                    var name = Path.GetFileName(f).ToLowerInvariant();
                    if (name.Contains("domains")) return 1;
                    if (name.Contains("tables")) return 2;
                    if (name.Contains("procedures")) return 3;
                    if (name.Contains("triggers")) return 4;
                    return 5;
                });

            // 1) Połącz się z bazą danych przy użyciu connectionString.
            using (var dbConn = new FbConnection(connectionString))
            {
                dbConn.Open();

                // 2) Wykonaj skrypty z katalogu scriptsDirectory (tylko obsługiwane elementy).
                foreach (var file in orderedFiles)
                {
                    string script = File.ReadAllText(file);

                    // 3) Zadbaj o bezpieczeństwo zmian.
                    try
                    {
                        ExecuteScript(dbConn, script, true, Path.GetFileName(file), report);
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"ERROR: {Path.GetFileName(file)}");
                        report.AppendLine(ex.Message);
                    }
                }
            }
            Console.WriteLine(report.ToString());
        }

        static void ExecuteScript(FbConnection dbConn, string script, bool forceUpdate, string fileName, StringBuilder report)
        {
            string warnings = string.Empty;
            string errors = string.Empty;
            // Normalizujemy końce linii
            script = script.Replace("\r\n", "\n");

            // Wykryj terminator z SET TERM
            var termMatch = Regex.Match(script, @"SET\s+TERM\s+(\S+)\s+;", RegexOptions.IgnoreCase);
            string terminator = ";";
            if (termMatch.Success)
            {
                terminator = termMatch.Groups[1].Value;
            }

            // Usuń wszystkie linie SET TERM
            script = Regex.Replace(script, @"(?mi)^\s*SET\s+TERM\b.*$", string.Empty, RegexOptions.IgnoreCase);

            if (forceUpdate)
            {
                // Procedury → zamień na CREATE OR ALTER
                script = Regex.Replace(script,
                    @"(?mi)CREATE\s+PROCEDURE",
                    "CREATE OR ALTER PROCEDURE");

                // Triggery → zamień na CREATE OR ALTER
                script = Regex.Replace(script,
                    @"(?mi)CREATE\s+TRIGGER",
                    "CREATE OR ALTER TRIGGER");
            }

            // Podziel po aktualnym terminatorze
            var statements = script.Split(new string[] { terminator }, StringSplitOptions.RemoveEmptyEntries);

            string warning = string.Empty;
            foreach (var stmtRaw in statements)
            {
                string stmt = stmtRaw.Trim();
                if (string.IsNullOrWhiteSpace(stmt)) continue;
                try
                {
                    if (forceUpdate)
                    {
                        if (stmt.StartsWith("CREATE DOMAIN", StringComparison.OrdinalIgnoreCase))
                        {
                            var name = stmt.Split(' ')[2];
                            if (DomainExists(dbConn, name))
                            {
                                warnings += $"\n - WARNING: Domain {name} already exists";
                                continue;
                            }
                        }
                        else if (stmt.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
                        {
                            var name = stmt.Split(' ')[2];
                            if (TableExists(dbConn, name))
                            {
                                warnings += $"\n - WARNING: Table {name} already exists";
                                continue;
                            }
                        }
                    }

                    using var cmd = new FbCommand(stmt, dbConn);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    errors += $"\n - ERROR executing statement:\n{stmt}\n{ex.Message}\n";
                }
            }

            if (string.IsNullOrEmpty(errors))
            {
                report.AppendLine($"OK: {fileName}{warnings}");
            }
            else
            {
                report.AppendLine($"ERROR: {fileName}{warnings}{errors}");
            }

            static bool DomainExists(FbConnection conn, string domainName)
            {
                using var cmd = new FbCommand(
                    "SELECT COUNT(*) FROM RDB$FIELDS WHERE RDB$FIELD_NAME = @name", conn);
                cmd.Parameters.AddWithValue("name", domainName.ToUpperInvariant());
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
            static bool TableExists(FbConnection conn, string tableName)
            {
                using var cmd = new FbCommand(
                    "SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @name", conn);
                cmd.Parameters.AddWithValue("name", tableName.ToUpperInvariant());
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }
    }
}
