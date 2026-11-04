using ClosedXML.Excel;
using SupplyPlanner.Core;

namespace SupplyPlanner.Infrastructure;

public static class PlanExcelExport
{
    public static byte[] Create(PlanningScenario scenario, SupplyPlan plan, PlanQuery? query = null)
    {
        query ??= new PlanQuery();
        var products = query.Apply(plan.Products);
        var scope = $"Позиций: {products.Count} из {plan.Products.Count}. Склад: {(string.IsNullOrWhiteSpace(query.Warehouse) ? "весь сценарий" : query.Warehouse)}. Оценка: {(string.IsNullOrEmpty(query.Status) ? "все" : PlanLabels.Status(query.Status))}. Поиск: {(string.IsNullOrWhiteSpace(query.Search) ? "не задан" : query.Search)}.";
        using var book = new XLWorkbook();
        book.Style.Font.FontName = "Arial";
        book.Style.Font.FontSize = 11;
        var sheet = book.AddWorksheet("План поставок");
        Title(sheet, "SupplyPlanner · План поставок", 12);
        sheet.Range("A2:L2").Merge().Value = scenario.Name;
        sheet.Range("A3:L3").Merge().Value = $"Начало модели: {scenario.StartDate:dd.MM.yyyy} · {scenario.Source}";
        sheet.Range("A4:L4").Merge().Value = "Сохранённый результат: изменение ячеек не пересчитывает план. Для нового расчёта используйте приложение.";
        sheet.Range("A5:L5").Merge().Value = scope;
        sheet.Range("A5:L5").Style.Alignment.WrapText = true;
        sheet.Row(5).Height = 32;
        sheet.Range("A3:L4").Style.Font.FontColor = XLColor.FromHtml("#5B687A");
        sheet.Range("A3:L4").Style.Alignment.WrapText = true;
        sheet.Rows(3, 4).Height = 32;
        Headers(sheet, 6, ["ID позиции", "Артикул", "Товар", "Склад", "Остаток, шт.",
            "Спрос, шт./день", "К поставке, шт.", "Прибытие", "Отправить до", "Оценка",
            "Страховой запас, шт.", "Пояснения"]);
        var row = 7;
        foreach (var item in products)
        {
            sheet.Cell(row, 1).Value = item.ProductId;
            sheet.Cell(row, 2).Value = item.Article ?? item.ProductId;
            sheet.Cell(row, 3).Value = item.Name;
            sheet.Cell(row, 4).Value = PlanQuery.WarehouseName(item.Warehouse);
            Number(sheet, row, 5, item.Stock);
            Number(sheet, row, 6, item.Forecast.DailyRate);
            Number(sheet, row, 7, item.RecommendedQuantity);
            Date(sheet, row, 8, PlanOverview.RecommendedArrival(item));
            Date(sheet, row, 9, item.RecommendedQuantity > 0 ? item.LatestDispatchDate : null);
            sheet.Cell(row, 10).Value = item.Status is "urgent" or "unknown"
                ? PlanOverview.Action(item) + (item.LatestDispatchDate < scenario.StartDate ? "; срок отправки до начала модели" : "")
                : PlanLabels.Status(item.Status);
            Number(sheet, row, 11, item.SafetyStock);
            sheet.Cell(row, 12).Value = string.Join(" ", new[] { PlanOverview.Action(item), PlanOverview.DispatchNote(item, scenario.StartDate), string.Join(" ", item.Warnings) }.Where(text => text.Length > 0));
            sheet.Cell(row, 10).Style.Font.FontColor = XLColor.FromHtml(item.Status switch
            { "urgent" => "#B53C4A", "healthy" => "#187C57", _ => "#315FD4" });
            row++;
        }
        Finish(sheet, 6, row - 1, [16, 16, 30, 20, 14, 14, 16, 15, 16, 32, 18, 64]);
        if (products.Count > 0) sheet.Range(7, 7, row - 1, 7).Style.Font.Bold = true;
        // Основные действия помещаются рядом; технический страховой запас и пояснения можно раскрыть.
        sheet.Column(11).Hide();
        sheet.Column(12).Hide();
        var warehouses = PlanOverview.ByWarehouse(products);
        if (warehouses.Count > 0)
        {
            var summaryTitle = row + 2;
            sheet.Range(summaryTitle, 1, summaryTitle, 8).Merge().Value = "Итого по складам · снимок из приложения; фильтры Excel не пересчитывают сводку";
            sheet.Row(summaryTitle).Height = 28;
            sheet.Cell(summaryTitle, 1).Style.Font.Bold = true;
            Headers(sheet, summaryTitle + 1, ["Склад", "К поставке, шт.", "Позиций", "К пополнению", "Без оценки", "Отправить до", "При обычной доставке", "Действие"]);
            var summaryRow = summaryTitle + 2;
            foreach (var warehouse in warehouses)
            {
                sheet.Cell(summaryRow, 1).Value = warehouse.Warehouse;
                Number(sheet, summaryRow, 2, warehouse.Quantity);
                Number(sheet, summaryRow, 3, warehouse.Positions);
                Number(sheet, summaryRow, 4, warehouse.ToReplenish);
                Number(sheet, summaryRow, 5, warehouse.Unknown);
                Date(sheet, summaryRow, 6, warehouse.DispatchBy);
                Date(sheet, summaryRow, 7, warehouse.Arrival);
                sheet.Cell(summaryRow, 8).Value = warehouse.Urgent > 0 ? "Нужна ранняя доставка: " + warehouse.Urgent
                    : warehouse.ToReplenish > 0 ? "Запланировать поставку" : warehouse.Unknown > 0 ? "Уточнить данные" : "Новая поставка не нужна";
                if (warehouse.DispatchBy < scenario.StartDate) sheet.Cell(summaryRow, 8).Value += "; срок отправки до начала модели";
                summaryRow++;
            }
            var summaryRange = sheet.Range(summaryTitle + 1, 1, summaryRow - 1, 8);
            summaryRange.CreateTable("WarehouseTotals").Theme = XLTableTheme.None;
            StyleRange(summaryRange);
            StyleHeading(sheet.Range(summaryTitle + 1, 1, summaryTitle + 1, 8));
            for (var r = summaryTitle + 2; r < summaryRow; r++)
            {
                var lines = Enumerable.Range(1, 8).Max(col => Math.Ceiling(sheet.Cell(r, col).GetString().Length / (sheet.Column(col).Width * .8)));
                sheet.Row(r).Height = Math.Min(409, Math.Max(42, lines * 16 + 10));
            }
            sheet.Cell(summaryRow, 1).Value = "Всего";
            Number(sheet, summaryRow, 2, warehouses.Any(item => item.Quantity.HasValue) ? warehouses.Sum(item => item.Quantity ?? 0) : null);
            Number(sheet, summaryRow, 3, warehouses.Sum(item => item.Positions));
            Number(sheet, summaryRow, 4, warehouses.Sum(item => item.ToReplenish));
            Number(sheet, summaryRow, 5, warehouses.Sum(item => item.Unknown));
            StyleRange(sheet.Range(summaryRow, 1, summaryRow, 8));
            sheet.Range(summaryRow, 1, summaryRow, 8).Style.Font.Bold = true;
            sheet.Row(summaryRow).Height = 30;
        }

        var daily = book.AddWorksheet("Расчёт по дням");
        Title(daily, "Остатки на конец модельного дня", 11);
        daily.Range("A2:K2").Merge().Value = "Поступления — в начале дня, расход — в конце. Неудовлетворённый спрос не переносится на следующий день.";
        daily.Range("A2:K2").Style.Alignment.WrapText = true;
        daily.Row(2).Height = 32;
        daily.Range("A3:K3").Merge().Value = scope + $" Дневной баланс: {products.Count(item => item.Timeline.Count > 0)} позиций. Для позиций без оценки баланс не строится.";
        daily.Range("A3:K3").Style.Alignment.WrapText = true;
        daily.Row(3).Height = 32;
        Headers(daily, 4, ["ID позиции", "Артикул", "Товар", "Склад", "Дата", "Поступит, шт.",
            "Новая поставка, шт.", "Без новой, шт.", "С новой, шт.", "Потерянный спрос без новой, шт.", "Потерянный спрос с новой, шт."]);
        row = 5;
        foreach (var item in products)
        foreach (var day in item.Timeline)
        {
            daily.Cell(row, 1).Value = item.ProductId;
            daily.Cell(row, 2).Value = item.Article ?? item.ProductId;
            daily.Cell(row, 3).Value = item.Name;
            daily.Cell(row, 4).Value = PlanQuery.WarehouseName(item.Warehouse);
            Date(daily, row, 5, day.Date);
            Number(daily, row, 6, day.Incoming);
            Number(daily, row, 7, day.RecommendedIncoming);
            Number(daily, row, 8, day.BaselineClosing);
            Number(daily, row, 9, day.PlannedClosing);
            Number(daily, row, 10, day.BaselineLostDemand);
            Number(daily, row, 11, day.PlannedLostDemand);
            row++;
        }
        Finish(daily, 4, row - 1, [22, 20, 36, 30, 17, 20, 24, 20, 20, 28, 28]);

        var parameters = book.AddWorksheet("Параметры");
        Title(parameters, "Параметры сохранённого расчёта", 2);
        var values = new (string Label, XLCellValue Value)[]
        {
            ("Сценарий", scenario.Name), ("Источник", scenario.Source),
            ("Снимок данных", scenario.SnapshotAt?.ToString("dd.MM.yyyy HH:mm zzz") ?? "Не указан"),
            ("Начало модели", scenario.StartDate.ToDateTime(TimeOnly.MinValue)),
            ("Метод прогноза", scenario.Settings.Strategy == "weighted" ? "Взвешенное среднее" : "Средний спрос"),
            ("Доставка, дней", scenario.Settings.LeadDays),
            ("Запас после прибытия, дней", scenario.Settings.TargetDays),
            ("Страховой запас, дней", scenario.Settings.SafetyDays),
            ("Упаковка, шт.", scenario.Settings.PackSize),
            ("История, дней", scenario.Settings.HistoryDays),
            ("Неизвестные значения", "Пустая ячейка означает отсутствие оценки, а не ноль."),
            ("Склад", "Без разбивки — суммарные данные. Остаток и история каждой складской позиции независимы."),
            ("Ограничение", "Прогноз по суточной модели; будущие продажи и сроки могут отличаться. Это экспорт результата, не Excel-калькулятор.")
        };
        Headers(parameters, 3, ["Параметр", "Значение"]);
        row = 4;
        foreach (var pair in values)
        {
            parameters.Cell(row, 1).Value = pair.Label;
            parameters.Cell(row, 2).Value = pair.Value;
            if (parameters.Cell(row, 2).DataType == XLDataType.DateTime)
                parameters.Cell(row, 2).Style.DateFormat.Format = "dd.mm.yyyy";
            row++;
        }
        Finish(parameters, 3, row - 1, [36, 96]);
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    private static void Title(IXLWorksheet sheet, string text, int columns)
    {
        sheet.Range(1, 1, 1, columns).Merge().Value = text;
        var title = sheet.Range(1, 1, 1, columns);
        title.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF8");
        title.Style.Font.FontColor = XLColor.FromHtml("#132238");
        title.Style.Font.FontSize = 18;
        title.Style.Font.Bold = true;
        sheet.Row(1).Height = 36;
    }

    private static void Headers(IXLWorksheet sheet, int row, string[] names)
    {
        for (var col = 0; col < names.Length; col++) sheet.Cell(row, col + 1).Value = names[col];
        sheet.Row(row).Height = 42;
    }

    private static void Finish(IXLWorksheet sheet, int header, int lastRow, double[] widths)
    {
        // Фиксированные ширины не требуют системных шрифтов при запуске в Linux-контейнере.
        for (var col = 0; col < widths.Length; col++) sheet.Column(col + 1).Width = widths[col];
        var range = sheet.Range(header, 1, Math.Max(header, lastRow), widths.Length);
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        if (lastRow > header)
        {
            var table = range.CreateTable();
            table.Theme = XLTableTheme.None;
            sheet.Rows(header + 1, lastRow).Height = 56;
        }
        var heading = sheet.Range(header, 1, header, widths.Length);
        StyleRange(range);
        StyleHeading(heading);
        sheet.SheetView.FreezeRows(header);
        // Закрепляем только заголовки: вертикальная граница не режет объединённые названия.
        sheet.ShowGridLines = false;
        for (var row = header + 1; row <= lastRow; row++)
        {
            var lines = Enumerable.Range(1, widths.Length).Max(col =>
                Math.Ceiling(sheet.Cell(row, col).GetString().Length / (widths[col - 1] * .85)));
            sheet.Row(row).Height = Math.Max(30, lines * 15 + 8);
        }
    }

    private static void StyleRange(IXLRange range)
    {
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Style.Fill.BackgroundColor = XLColor.White;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorderColor = XLColor.FromHtml("#DCE4EF");
        range.Style.Border.OutsideBorderColor = XLColor.FromHtml("#DCE4EF");
    }

    private static void StyleHeading(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF8");
        range.Style.Font.FontColor = XLColor.FromHtml("#132238");
        range.Style.Font.Bold = true;
    }

    private static void Number(IXLWorksheet sheet, int row, int col, decimal? value)
    {
        if (value.HasValue) sheet.Cell(row, col).Value = (double)value.Value;
        sheet.Cell(row, col).Style.NumberFormat.Format = value.HasValue && decimal.Truncate(value.Value) == value.Value
            ? "#,##0" : "#,##0.########";
    }

    private static void Date(IXLWorksheet sheet, int row, int col, DateOnly? date)
    {
        if (date.HasValue) sheet.Cell(row, col).Value = date.Value.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(row, col).Style.DateFormat.Format = "dd.mm.yyyy";
    }
}
