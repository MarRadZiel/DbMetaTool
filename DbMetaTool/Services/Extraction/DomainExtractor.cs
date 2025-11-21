using DbMetaTool.Models;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool.Services.Extraction
{
    public class DomainExtractor : MetadataExtractor<Domain>
    {
        public DomainExtractor(FbConnection connection) : base(connection) { }

        public override List<Domain> Extract()
        {
            List<Domain> domains = [];

            var domainsCmd = new FbCommand(@"
                SELECT RDB$FIELD_NAME, RDB$FIELD_TYPE, RDB$FIELD_LENGTH, RDB$FIELD_PRECISION,
                       RDB$FIELD_SCALE, RDB$DEFAULT_SOURCE, RDB$VALIDATION_SOURCE, RDB$NULL_FLAG
                FROM RDB$FIELDS
                WHERE RDB$SYSTEM_FLAG = 0
                      AND RDB$FIELD_NAME NOT LIKE 'RDB$%'", connection);

            using (var reader = domainsCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string name = reader.GetString(0).Trim();
                    int type = reader.GetInt32(1);
                    int length = reader.GetInt32(2);
                    short? precision = reader["RDB$FIELD_PRECISION"] as short?;
                    short? scale = reader["RDB$FIELD_SCALE"] as short?;

                    string? defaultSrc = reader["RDB$DEFAULT_SOURCE"] as string;
                    string? checkSrc = reader["RDB$VALIDATION_SOURCE"] as string;
                    object nullFlag = reader["RDB$NULL_FLAG"];

                    string sqlType = MapFbType(type, length, precision, scale);

                    domains.Add(new Domain
                    {
                        Name = name,
                        Type = sqlType,
                        Default = !string.IsNullOrWhiteSpace(defaultSrc) ? TrimFormat(defaultSrc, "DEFAULT") : null,
                        NotNull = nullFlag != DBNull.Value,
                        CheckConstraint = !string.IsNullOrWhiteSpace(checkSrc) ? TrimFormat(checkSrc, "CHECK") : null
                    });
                }
            }
            return domains;
        }
    }
}
