# Соответствие требованиям преподавателя

| Требование | Где реализовано |
|---|---|
| VS Code, C#, .NET10 | global.json, Directory.Build.props, .vscode |
| Отдельные классы и библиотека | SupplyPlanner.Core, не зависит от UI и файлов |
| Повторное использование | Core/Infrastructure используются Desktop и CLI |
| Свойства и коллекции | Models.cs, ObservableCollection в ViewModels |
| Интерфейсы и наследование | IDemandForecastStrategy, DemandForecastStrategy и реализации |
| События | EventHandler с EventArgs; PlannerWorkflow подписывается через += |
| Графический интерфейс | Avalonia: XAML и C#, самостоятельное окно |
| Командная строка | SupplyPlanner.Cli, Program.cs; Desktop --open и --help |
| Настройки | UserPreferences: тема и размер окна, отдельный JSON |
| Режимы | Средний/взвешенный прогноз |
| Горячие клавиши, F1 | MainWindow.axaml.cs, HelpText |
| Состояние в файл и обратно | ProjectDocument, ProjectFiles.Save/Read, формат версии1 |
| Проверки и примеры | tests/, examples/, docs/DEFENSE.md |
| Личный репозиторий | Эта отдельная папка с Git; push выполняется отдельно |

Это одна учебная работа, несмотря на несколько проектов .csproj в решении.
Если по заданию нужны две работы, вторая тема согласуется отдельно.

Результат не хранится как единственный источник истины: при загрузке состояние
сценария проверяется и расчёт воспроизводится из входных данных. Журнал и выбранная
позиция/фильтры сохраняются. При ошибке текущая работа остаётся в памяти.
