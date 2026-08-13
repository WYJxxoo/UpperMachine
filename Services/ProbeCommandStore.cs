using System.IO;
using System.Text.Json;
using UpperMachine.Models;

namespace UpperMachine.Services;

public sealed class ProbeCommandStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public ProbeCommandStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "Config", "probe-command-settings.json");
    }

    public ProbeCommandSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new ProbeCommandSettings();
        }

        string json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProbeCommandSettings();
        }

        ProbeCommandSettings? settings = JsonSerializer.Deserialize<ProbeCommandSettings>(json, _jsonOptions);
        return settings ?? new ProbeCommandSettings();
    }

    public void Save(ProbeCommandSettings settings)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
