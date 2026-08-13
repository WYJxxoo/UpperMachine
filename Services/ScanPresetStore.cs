using System.IO;
using System.Text.Json;
using UpperMachine.Models;

namespace UpperMachine.Services;

public sealed class ScanPresetStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public ScanPresetStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "Config", "scan-parameter-presets.json");
    }

    public IReadOnlyList<ScanParameterPreset> LoadPresets()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<ScanParameterPreset>();
        }

        string json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ScanParameterPreset>();
        }

        List<ScanParameterPreset>? presets = JsonSerializer.Deserialize<List<ScanParameterPreset>>(json, _jsonOptions);
        return (presets ?? [])
            .OrderBy(preset => preset.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public void SavePreset(ScanParameterPreset preset)
    {
        List<ScanParameterPreset> presets = LoadPresets().ToList();
        int existingIndex = presets.FindIndex(item =>
            string.Equals(item.Name, preset.Name, StringComparison.CurrentCultureIgnoreCase));

        if (existingIndex >= 0)
        {
            presets[existingIndex] = preset;
        }
        else
        {
            presets.Add(preset);
        }

        WriteAll(presets);
    }

    public void DeletePreset(string presetName)
    {
        List<ScanParameterPreset> presets = LoadPresets().ToList();
        presets.RemoveAll(item => string.Equals(item.Name, presetName, StringComparison.CurrentCultureIgnoreCase));
        WriteAll(presets);
    }

    private void WriteAll(IEnumerable<ScanParameterPreset> presets)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            presets.OrderBy(preset => preset.Name, StringComparer.CurrentCultureIgnoreCase),
            _jsonOptions);

        File.WriteAllText(_filePath, json);
    }
}
