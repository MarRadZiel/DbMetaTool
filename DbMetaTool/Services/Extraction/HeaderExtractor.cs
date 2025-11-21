using DbMetaTool.Models;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool.Services.Extraction
{
    public class HeaderExtractor : MetadataExtractor<Header>
    {
        public HeaderExtractor(FbConnection connection) : base(connection) { }

        public override List<Header> Extract()
        {
            // Dialect – zawsze 3 w Firebird 5.0
            const int dialect = 3;

            // MON$DATABASE: path + page size
            var monCmd = new FbCommand("SELECT MON$DATABASE_NAME, MON$PAGE_SIZE FROM MON$DATABASE", connection);
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
            var csCmd = new FbCommand("SELECT RDB$CHARACTER_SET_NAME FROM RDB$DATABASE", connection);
            string charset = "NONE";
            using (var csReader = csCmd.ExecuteReader())
            {
                if (csReader.Read())
                {
                    charset = csReader.GetString(0).Trim();
                }
            }

            // User + password z connection stringa
            var builder = new FbConnectionStringBuilder(connection.ConnectionString);
            string user = builder.UserID;
            string password = builder.Password;

            return [ 
                new Header
                {
                    Dialect = dialect,
                    CharSet = charset,
                    DBFileDirectory = Path.GetDirectoryName(dbPath) ?? string.Empty,
                    DBFileName = Path.GetFileName(dbPath),
                    PageSize = pageSize,
                    User = user,
                    Password = password,
                }
            ];
        }
    }
}
