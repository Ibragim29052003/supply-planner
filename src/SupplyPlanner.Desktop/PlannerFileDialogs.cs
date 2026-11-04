using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SupplyPlanner.Desktop;

// Диалог выбирает путь. Проверка данных и файловые операции остаются в C#-сервисах.
public interface IPlannerFileDialogs
{
    Task<string?> OpenProjectAsync();
    Task<string?> SaveProjectAsync();
    Task<string?> SaveExcelAsync();
}

public sealed class NativePlannerFileDialogs(Window owner) : IPlannerFileDialogs
{
    public async Task<string?> OpenProjectAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Открыть JSON-проект или сценарий", AllowMultiple = false,
            FileTypeFilter = [new("JSON / проект") { Patterns = ["*.json"] }]
        });
        return files.Count == 0 ? null : LocalPath(files[0]);
    }
    public async Task<string?> SaveProjectAsync()
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "Сохранить состояние проекта", SuggestedFileName = "plan.supply.json",
            DefaultExtension = "json",
            FileTypeChoices = [new("Проект SupplyPlanner") { Patterns = ["*.supply.json"] }]
        });
        return file is null ? null : LocalPath(file);
    }
    public async Task<string?> SaveExcelAsync()
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "Экспорт текущего списка", SuggestedFileName = "supply-plan.xlsx",
            DefaultExtension = "xlsx", FileTypeChoices = [new("Excel") { Patterns = ["*.xlsx"] }]
        });
        return file is null ? null : LocalPath(file);
    }
    private static string LocalPath(IStorageFile file) => file.TryGetLocalPath()
        ?? throw new ArgumentException("Выберите локальный файл.");
}
