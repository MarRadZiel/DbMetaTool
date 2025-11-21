using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool.Services.Extraction
{
    public abstract class MetadataExtractor<T>
    {
        protected readonly FbConnection connection;

        public MetadataExtractor(FbConnection connection)
        {
            this.connection = connection;
        }

        public abstract List<T> Extract();



        // Proste mapowanie typów Firebirda na SQL
        protected static string MapFbType(int fieldType, int length, short? precision, short? scale)
        {
            switch (fieldType)
            {
                case 7:  // SMALLINT
                    return "SMALLINT";
                case 8:  // INTEGER
                    return "INTEGER";
                case 10: // FLOAT
                    return "FLOAT";
                case 12: // DATE
                    return "DATE";
                case 13: // TIME
                    return "TIME";
                case 14: // CHAR
                    return $"CHAR({length})";
                case 16: // BIGINT / NUMERIC/DECIMAL
                    if (precision.HasValue && scale.HasValue && scale.Value < 0)
                    {
                        return $"DECIMAL({precision},{-scale.Value})";
                    }
                    else if (precision.HasValue && scale.HasValue)
                    {
                        return $"NUMERIC({precision},{-scale.Value})";
                    }
                    return "BIGINT";
                case 27: // DOUBLE
                    return "DOUBLE PRECISION";
                case 35: // TIMESTAMP
                    return "TIMESTAMP";
                case 37: // VARCHAR
                    return $"VARCHAR({length})";
                default:
                    return $"UNKNOWN({length})";
            }
        }


        protected static string TrimFormat(string text, string wordToTrim)
        {
            text = text.Trim();
            if (!string.IsNullOrEmpty(wordToTrim) && text.StartsWith(wordToTrim, StringComparison.OrdinalIgnoreCase))
            {
                text = text[wordToTrim.Length..].TrimStart();
            }
            return text;
        }
    }
}
