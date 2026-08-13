using System.Text.Json;

namespace GitIt.UserAnnotations;

public sealed class UserAnnotationProjectStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public UserAnnotationProject Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("GitIt project file not found.", path);
        var project = JsonSerializer.Deserialize<UserAnnotationProject>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("GitIt project file is empty or invalid.");
        if (project.SchemaVersion != UserAnnotationProject.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported GitIt project schema: {project.SchemaVersion}.");
        project.DocumentGroups ??= [];
        project.ConfirmedRelations ??= [];
        project.HiddenItems ??= [];
        project.FamilyNames ??= new(StringComparer.OrdinalIgnoreCase);
        project.Notes ??= new(StringComparer.OrdinalIgnoreCase);
        return project;
    }

    public void Save(string path, UserAnnotationProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Project path has no directory.");
        Directory.CreateDirectory(directory);
        project.SchemaVersion = UserAnnotationProject.CurrentSchemaVersion;
        project.SavedAt = DateTimeOffset.UtcNow;
        var temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(project, Json));
        File.Move(temporary, fullPath, true);
    }
}
