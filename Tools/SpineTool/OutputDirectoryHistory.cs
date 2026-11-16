using System.IO;
using System.Text.Json;

namespace IdleGearDefence.SpineTool;

public sealed class OutputDirectoryHistory
{
    private readonly string _path;
    private readonly List<string> _directories = new();

    public OutputDirectoryHistory(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path))
                foreach (var directory in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new())
                    Add(directory);
        }
        catch (JsonException) { }
        catch (IOException) { }
    }

    public IReadOnlyList<string> Directories => _directories;

    public void Remember(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.Trim()));
        _directories.RemoveAll(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        _directories.Insert(0, normalized);
        if (_directories.Count > 20) _directories.RemoveRange(20, _directories.Count - 20);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_directories, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, overwrite: true);
    }

    private void Add(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.Trim()));
            if (_directories.Count < 20 && !_directories.Contains(normalized, StringComparer.OrdinalIgnoreCase)) _directories.Add(normalized);
        }
        catch (ArgumentException) { }
    }
}
