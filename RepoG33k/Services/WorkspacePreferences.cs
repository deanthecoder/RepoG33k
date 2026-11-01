// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using System.Text.Json;

namespace RepoG33k.Services;

/// <summary>Stores the workspace and commit conventions separately from ReviewG33k settings.</summary>
/// <remarks>Only repository paths and UI preferences are stored; Codex manages its own credentials.</remarks>
internal sealed class WorkspacePreferences
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RepoG33k", "workspace.json");
    private static readonly string LegacySettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiffLens", "workspace.json");
    public List<string> Repositories { get; set; } = [];
    public Dictionary<string, RepositoryPreferences> PerRepository { get; set; } = new();
    public List<string> CollapsedSections { get; set; } = [];
    public string RepositoryRoot { get; set; } = string.Empty;

    public RepositoryPreferences For(string root)
    {
        if (!PerRepository.TryGetValue(root, out var value))
            PerRepository[root] = value = new RepositoryPreferences();
        return value;
    }

    public static WorkspacePreferences Load()
    {
        try
        {
            var path = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
            return JsonSerializer.Deserialize<WorkspacePreferences>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, overwrite: true);
    }
}

/// <summary>Holds one repository's commit conventions and unfinished draft.</summary>
/// <remarks>Jira overrides are per commit and deliberately omitted from persisted preferences.</remarks>
internal sealed class RepositoryPreferences
{
    public string Convention { get; set; } = "home";
    public string Category { get; set; } = "Feature";
    public string Summary { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public string JiraOverride { get; set; }
}
