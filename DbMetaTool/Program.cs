using DbMetaTool.Services.Export;
using DbMetaTool.Services.Extraction;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Isql;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DbMetaTool
{
    public static class Program
    {
        private static string metadataFilesFormat = "sql";

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


                // podmieniamy ścieżkę w CREATE DATABASE
                string createDbScript = Regex.Replace(headerScript,
                    @"CREATE\s+DATABASE\s+'.+?'",
                    $"CREATE DATABASE '{dbPath}'",
                    RegexOptions.IgnoreCase);

                string connectionString = $"DataSource=localhost;User={user};Password={password};Database={dbPath};ServerType=0;";

                bool databaseCreated;
                // utworzenie bazy
                try
                {
                    FbConnection.CreateDatabase($"{connectionString}Charset={charset};", pageSize: pageSize, overwrite: true);
                    report.AppendLine("OK: empty database created");
                    databaseCreated = true;
                }
                catch (Exception ex)
                {
                    report.AppendLine($"ERROR: database creation error\n  {ex.Message}");
                    databaseCreated = false;
                }

                if (databaseCreated)
                {
                    // 2) Wczytaj i wykonaj kolejno skrypty z katalogu scriptsDirectory
                    //    (tylko domeny, tabele, procedury).
                    using (var dbConn = new FbConnection(connectionString))
                    {
                        dbConn.Open();

                        string domainsPath = Path.Combine(scriptsDirectory, "domains.sql");
                        if (File.Exists(domainsPath))
                        {
                            string script = File.ReadAllText(domainsPath);
                            ExecuteScript(dbConn, script, Path.GetFileName(domainsPath), report);
                        }

                        string tablesPath = Path.Combine(scriptsDirectory, "tables.sql");
                        if (File.Exists(tablesPath))
                        {
                            string script = File.ReadAllText(tablesPath);
                            ExecuteScript(dbConn, script, Path.GetFileName(tablesPath), report);
                        }

                        string proceduresPath = Path.Combine(scriptsDirectory, "procedures.sql");
                        if (File.Exists(proceduresPath))
                        {
                            string script = File.ReadAllText(proceduresPath);
                            ExecuteScript(dbConn, script, Path.GetFileName(proceduresPath), report);
                        }
                    }
                }
            }
            else
            {
                report.AppendLine($"ERROR: no header file at {headerPath}");
            }

            // 3) Obsłuż błędy i wyświetl raport.
            Console.WriteLine(report.ToString());

            static void ExecuteScript(FbConnection dbConn, string script, string fileName, StringBuilder report)
            {
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

                // Podziel po aktualnym terminatorze
                var statements = script.Split(new string[] { terminator }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var stmtRaw in statements)
                {
                    string stmt = stmtRaw.Trim();
                    if (string.IsNullOrWhiteSpace(stmt)) continue;

                    try
                    {
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
                    report.AppendLine($"OK: {fileName}");
                }
                else
                {
                    report.AppendLine($"ERROR: {fileName}{errors}");
                }
            }
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

                // --- Nagłówek ---
                var header = new HeaderExtractor(conn).Extract().FirstOrDefault();

                // --- Domeny ---
                var domains = new DomainExtractor(conn).Extract();

                // --- Tabele ---
                var tables = new TableExtractor(conn).Extract();

                // --- Procedury ---
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
            // TODO:
            // 1) Połącz się z bazą danych przy użyciu connectionString.
            // 2) Wykonaj skrypty z katalogu scriptsDirectory (tylko obsługiwane elementy).
            // 3) Zadbaj o poprawną kolejność i bezpieczeństwo zmian.
            throw new NotImplementedException();
        }
    }
}
