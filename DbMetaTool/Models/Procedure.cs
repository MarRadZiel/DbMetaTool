namespace DbMetaTool.Models
{
    public class Procedure
    {
        public string? Name { get; set; }
        public List<Parameter> Inputs { get; set; } = new();
        public List<Parameter> Outputs { get; set; } = new();
        public string? Body { get; set; }
    }
    public class Parameter
    {
        public string? Name { get; set; }
        public string? TypeOrDomain { get; set; }
    }
}