# Соответствие требованиям преподавателя

| Требование | Реализация и место проверки |
|---|---|
| Visual Studio Code, C#, .NET 10 | [global.json](../global.json), [Directory.Build.props](../Directory.Build.props), [.vscode](../.vscode) |
| Отдельные классы и библиотека | [SupplyPlanner.Core](../src/SupplyPlanner.Core): без UI, HTTP, Excel и файловых зависимостей |
| Повторное использование | [Desktop](../src/SupplyPlanner.Desktop/SupplyPlanner.Desktop.csproj) и [CLI](../src/SupplyPlanner.Cli/SupplyPlanner.Cli.csproj) используют те же Core/Infrastructure |
| Классы, свойства и коллекции | [Models.cs](../src/SupplyPlanner.Core/Models.cs), ObservableCollection в [PlannerViewModel.cs](../src/SupplyPlanner.Desktop/ViewModels/PlannerViewModel.cs) |
| Интерфейсы и наследование | IDemandForecastStrategy → DemandForecastStrategy → AverageDemandStrategy / WeightedDemandStrategy в [DemandForecast.cs](../src/SupplyPlanner.Core/DemandForecast.cs) |
| События | EventHandler с EventArgs в [Events.cs](../src/SupplyPlanner.Core/Events.cs); подписки += и журнал в [PlannerWorkflow.cs](../src/SupplyPlanner.Infrastructure/PlannerWorkflow.cs) |
| Графический интерфейс из разрешённого списка | Avalonia, [MainWindow.axaml](../src/SupplyPlanner.Desktop/MainWindow.axaml): отдельное окно, без браузера |
| Параметры командной строки | [Cli/Program.cs](../src/SupplyPlanner.Cli/Program.cs): calculate, demo, --help; Desktop: --open и --help |
| Пользовательские настройки | UserPreferences: тема и размер окна, отдельный JSON в [ProjectFiles.cs](../src/SupplyPlanner.Infrastructure/ProjectFiles.cs) |
| Режимы, когда уместно | Средний и взвешенный прогноз; искусственного уровня сложности нет |
| Горячие клавиши, F1 | [MainWindow.axaml.cs](../src/SupplyPlanner.Desktop/MainWindow.axaml.cs): Cmd/Ctrl+O/S/F/Enter, справка F1, стрелки графика |
| Сохранение состояния и загрузка | Версионированный ProjectDocument: сценарий, журнал, фильтры и выбор; пересчёт при открытии; локальное восстановление |
| Тестирование | 67 локальных тестов, независимый перебор 144 комбинаций, реальные JSON-примеры, проверки Excel и обработчиков окна |
| Наглядный показ и CLI-примеры | [QUICKSTART.md](QUICKSTART.md), [DEFENSE.md](DEFENSE.md), examples/*.json; Jupyter не требуется |
| Личный репозиторий с понятной структурой | Отдельный https://github.com/Ibragim29052003/supply-planner; публичный доступ, без архивов/пользовательских данных |
| Целостность и завершённость | Окно, библиотека, CLI, сохранение, отчёт и справка решают одну задачу; проверяется по [VERIFICATION.md](VERIFICATION.md) |

Это одна учебная работа, несмотря на несколько .csproj в решении. По требованию
двух работ вторая тема согласуется отдельно. Тема планирования была ранее
согласована пользователем с преподавателем; текущая реализация использует
прямо разрешённый Avalonia. Повторное согласование формата окна из текста
приведённых требований не следует.

Основные операции написаны на C#. XAML — разметка, shell — запуск/упаковка,
YAML — CI. В Desktop нет JavaScript. График рисуется C#-контролом InventoryChart.

JSON — входной сценарий или сохранённый проект. Excel — выходной отчёт,
не импортируемый калькулятор. Прямой интеграции с production SellerStatus нет;
она не является условием этих требований. Реальные данные не имитируются.

Нативные системные диалоги ОС не заменены утверждением о ручной проверке:
автотесты проходят обработчики кнопок с подменой выбора пути. Полная граница
автоматических и ручных проверок указана в VERIFICATION.md.
