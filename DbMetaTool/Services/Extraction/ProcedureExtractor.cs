using DbMetaTool.Models;
using FirebirdSql.Data.FirebirdClient;

namespace DbMetaTool.Services.Extraction
{
    public class ProcedureExtractor : MetadataExtractor<Procedure>
    {
        public ProcedureExtractor(FbConnection connection) : base(connection) { }

        public override List<Procedure> Extract()
        {
            List<Procedure> procedures = [];

            var procCmd = new FbCommand(@"
                SELECT RDB$PROCEDURE_NAME, RDB$PROCEDURE_SOURCE
                FROM RDB$PROCEDURES
                WHERE RDB$SYSTEM_FLAG = 0", connection);

            using (var procReader = procCmd.ExecuteReader())
            {
                while (procReader.Read())
                {
                    Procedure procedure = new Procedure()
                    {
                        Name = procReader.GetString(0).Trim(),
                        Body = procReader.GetString(1).Trim(),
                    };

                    // pobierz parametry (z typami/domenami)
                    var paramCmd = new FbCommand(@"
                        SELECT p.RDB$PARAMETER_NAME, p.RDB$PARAMETER_TYPE, p.RDB$FIELD_SOURCE, f.RDB$FIELD_TYPE,
                            f.RDB$FIELD_LENGTH, f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE
                        FROM RDB$PROCEDURE_PARAMETERS p
                        JOIN RDB$FIELDS f ON p.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
                        WHERE p.RDB$PROCEDURE_NAME = @proc
                        ORDER BY p.RDB$PARAMETER_TYPE, p.RDB$PARAMETER_NUMBER", connection);
                    paramCmd.Parameters.AddWithValue("proc", procedure.Name);

                    procedure.Inputs ??= new List<Parameter>();
                    procedure.Outputs ??= new List<Parameter>();

                    using var paramReader = paramCmd.ExecuteReader();
                    while (paramReader.Read())
                    {
                        string paramName = paramReader.GetString(0).Trim();
                        int paramType = paramReader.GetInt32(1); // 0=in, 1=out
                        string fieldSource = paramReader.GetString(2).Trim();
                        int fieldType = paramReader.GetInt32(3);
                        int fieldLength = paramReader.GetInt32(4);
                        short? fieldPrecision = paramReader.IsDBNull(5) ? null : paramReader.GetInt16(5);
                        short? fieldScale = paramReader.IsDBNull(6) ? null : paramReader.GetInt16(6);

                        string paramTypeName;
                        if (!string.IsNullOrEmpty(fieldSource) && !fieldSource.StartsWith("RDB$"))
                        {
                            // domena użytkownika
                            paramTypeName = fieldSource;
                        }
                        else
                        {
                            // domena systemowa → rozwiń do typu
                            paramTypeName = MapFbType(fieldType, fieldLength, fieldPrecision, fieldScale);
                        }

                        string paramDef = $"{paramName} {paramTypeName}";
                        if (paramType == 0)
                        {
                            procedure.Inputs.Add(new Parameter
                            {
                                Name = paramName,
                                TypeOrDomain = paramTypeName
                            });
                        }
                        else
                        {
                            procedure.Outputs.Add(new Parameter
                            {
                                Name = paramName,
                                TypeOrDomain = paramTypeName
                            });
                        }
                    }
                    procedures.Add(procedure);
                }
            }
            return procedures;
        }
    }
}
