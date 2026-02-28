using System.Text.Json;

namespace VoxAiGo.Core.Managers;

public class SnippetsManager
{
    private static readonly Lazy<SnippetsManager> _instance = new(() => new SnippetsManager());
    public static SnippetsManager Shared => _instance.Value;

    private readonly string _snippetsPath;
    private List<Snippet> _snippets;

    public event Action? SnippetsChanged;

    public IReadOnlyList<Snippet> Snippets => _snippets.AsReadOnly();

    private SnippetsManager()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoxAiGo");
        Directory.CreateDirectory(dir);
        _snippetsPath = Path.Combine(dir, "snippets.json");
        _snippets = Load();
    }

    public void Add(string trigger, string replacement)
    {
        _snippets.Add(new Snippet
        {
            Id = Guid.NewGuid().ToString(),
            Trigger = trigger,
            Replacement = replacement,
            IsEnabled = true
        });
        Save();
        SnippetsChanged?.Invoke();
    }

    public void Remove(string id)
    {
        _snippets.RemoveAll(s => s.Id == id);
        Save();
        SnippetsChanged?.Invoke();
    }

    public void Update(string id, string trigger, string replacement)
    {
        var snippet = _snippets.FirstOrDefault(s => s.Id == id);
        if (snippet != null)
        {
            snippet.Trigger = trigger;
            snippet.Replacement = replacement;
            Save();
            SnippetsChanged?.Invoke();
        }
    }

    public void ToggleEnabled(string id)
    {
        var snippet = _snippets.FirstOrDefault(s => s.Id == id);
        if (snippet != null)
        {
            snippet.IsEnabled = !snippet.IsEnabled;
            Save();
            SnippetsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Expand all enabled snippets in the given text.
    /// Replaces trigger words (case-insensitive) with their replacement text.
    /// </summary>
    public string ExpandSnippets(string text)
    {
        var result = text;
        foreach (var snippet in _snippets.Where(s => s.IsEnabled))
        {
            if (string.IsNullOrEmpty(snippet.Trigger)) continue;
            result = result.Replace(snippet.Trigger, snippet.Replacement, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    private List<Snippet> Load()
    {
        try
        {
            if (File.Exists(_snippetsPath))
            {
                var json = File.ReadAllText(_snippetsPath);
                return JsonSerializer.Deserialize<List<Snippet>>(json) ?? [];
            }
        }
        catch { }
        return [];
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_snippets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_snippetsPath, json);
        }
        catch { }
    }
}

public class Snippet
{
    public string Id { get; set; } = "";
    public string Trigger { get; set; } = "";
    public string Replacement { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
}
