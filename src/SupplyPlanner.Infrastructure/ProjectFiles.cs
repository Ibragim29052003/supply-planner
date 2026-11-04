using System.Text.Json;
using SupplyPlanner.Core;

namespace SupplyPlanner.Infrastructure;

public sealed record ViewState(string Search = "", string Warehouse = "", string Status = "", string Sort = "priority", string? Selected = null);
public sealed record ProjectDocument(int FormatVersion, Workspace Workspace, ViewState View);
public sealed record UserPreferences(string Theme = "dark", double Width = 1320, double Height = 900);

public static class ProjectFiles
{
    public const int MaxBytes = 4_000_000;
    // Читаем и старые сценарии JSON. Результат из файла не считаем доверенным: строим заново.
    public static ProjectDocument Read(string path)
    {
        if (new FileInfo(path).Length > MaxBytes) throw new ArgumentException("Файл должен быть не больше 4 МБ.");
        return Parse(File.ReadAllText(path));
    }
    public static ProjectDocument Parse(string text)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new ArgumentException("Файл должен быть не больше 4 МБ.");
        using var json = JsonDocument.Parse(text, new() { MaxDepth = 24 });
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Корнем JSON должен быть объект сценария или проекта.");
        if (json.RootElement.TryGetProperty("formatVersion", out _))
        {
            var doc = JsonSerializer.Deserialize<ProjectDocument>(text, ScenarioJson.Options)
                ?? throw new ArgumentException("Пустой файл проекта.");
            if (doc.FormatVersion != 1 || doc.Workspace?.Scenario is null || doc.View is null || doc.Workspace.Journal is null
                || doc.Workspace.Journal.Count > 100 || doc.Workspace.Journal.Any(e => e is null || e.Event is null || e.Message is null || e.Event.Length > 100 || e.Message.Length > 2000)
                || doc.View.Search is null || doc.View.Search.Length > 200 || doc.View.Warehouse is null || doc.View.Status is null || doc.View.Sort is null)
                throw new ArgumentException("Неподдерживаемая версия или повреждённое состояние проекта.");
            doc.Workspace.Scenario.Validate();
            return doc with { Workspace = doc.Workspace with { Plan = new Core.SupplyPlanner().Calculate(doc.Workspace.Scenario) } };
        }
        var scenario = ScenarioJson.Import(text);
        return new(1, PlannerWorkflow.Calculate(new(scenario, null, []), scenario), new());
    }
    public static void Save(string path, ProjectDocument document, bool overwrite = true)
    {
        document.Workspace.Scenario.Validate();
        // План детерминирован; файл хранит исходные данные, журнал и состояние представления.
        var json = JsonSerializer.Serialize(document with { Workspace = document.Workspace with { Plan = null } }, ScenarioJson.Options);
        WriteAtomic(path, json, overwrite);
    }
    public static void WriteAtomic(string path, string contents, bool overwrite = true) => WriteAtomic(path, System.Text.Encoding.UTF8.GetBytes(contents), overwrite);
    public static void WriteAtomic(string path, byte[] contents, bool overwrite = true)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(contents); stream.Flush(flushToDisk: true); }
            File.Move(temporary, full, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static PlanningScenario Example()
    {
        using var stream = typeof(ProjectFiles).Assembly.GetManifestResourceStream("SupplyPlanner.Example.json")!;
        using var reader = new StreamReader(stream);
        return ScenarioJson.Import(reader.ReadToEnd());
    }
    public static string DataDirectory => Environment.GetEnvironmentVariable("SUPPLY_PLANNER_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SupplyPlanner");
    public static UserPreferences LoadPreferences(string path)
    {
        if (!File.Exists(path)) return new();
        var value = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path), ScenarioJson.Options) ?? new();
        if (value.Theme is not ("light" or "dark") || !double.IsFinite(value.Width) || !double.IsFinite(value.Height)
            || value.Width is < 960 or > 5000 || value.Height is < 640 or > 3000)
            throw new ArgumentException("Некорректный файл настроек.");
        return value;
    }
    public static void SavePreferences(string path, UserPreferences value) => WriteAtomic(path, JsonSerializer.Serialize(value, ScenarioJson.Options));
}
