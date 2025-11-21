namespace DbMetaTool.Models
{
    public class Header
    {
        public int Dialect { get; set; } = 3;
        public string CharSet { get; set; } = "NONE";
        public string DBFileName { get; set; }
        public string DBFileDirectory { get; set; }
        public string DBFilePath => System.IO.Path.Combine(DBFileDirectory, DBFileName);
        public string User { get; set; }
        public string Password { get; set; }
        public int PageSize { get; set; } = 16384;
    }
}
