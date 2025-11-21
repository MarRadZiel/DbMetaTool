namespace DbMetaTool.Services.Export
{
    public static class ExporterFactory
    {
        public static IExporter CreateExporter(string format)
        {
            format = format.Trim().TrimStart('.').ToLower();
            return format switch
            {
                "sql" => new SQLExporter(),
                _ => throw new NotSupportedException($"There is no valid exporter defined for {format} format."),
            };
        }
    }
}
