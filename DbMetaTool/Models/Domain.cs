namespace DbMetaTool.Models
{
    public class Domain
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Default { get; set; }
        public bool NotNull { get; set; }
        public string? CheckConstraint { get; set; }
    }
}