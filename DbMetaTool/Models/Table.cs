namespace DbMetaTool.Models
{
    public class Table
    {
        public string? Name { get; set; }
        public List<Column> Columns { get; set; } = [];
        public List<string> Constraints { get; set; } = [];
    }

    public class Column
    {
        public string? Name { get; set; }
        public string? DomainOrType { get; set; }
        public bool PrimaryKey { get; set; }
        public bool NotNull { get; set; }
        public string? Default { get; set; }
    }
}