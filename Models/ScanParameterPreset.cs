namespace UpperMachine.Models;

public sealed class ScanParameterPreset
{
    public string Name { get; set; } = string.Empty;

    public ScanParameter Parameter { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public override string ToString()
    {
        return Name;
    }
}
