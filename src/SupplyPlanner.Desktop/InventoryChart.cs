using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SupplyPlanner.Core;
using SupplyPlanner.Infrastructure;

namespace SupplyPlanner.Desktop;

public sealed class InventoryChart : Control
{
    public static readonly StyledProperty<ProductPlan?> PlanProperty = AvaloniaProperty.Register<InventoryChart, ProductPlan?>(nameof(Plan));
    public ProductPlan? Plan { get => GetValue(PlanProperty); set => SetValue(PlanProperty, value); }
    public event Action<int>? DaySelected;
    private int _index;
    static InventoryChart() => AffectsRender<InventoryChart>(PlanProperty);
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == PlanProperty) _index = 0; }
    private void Select(int index)
    { _index = Math.Clamp(index, 0, Math.Max(0, (Plan?.Timeline.Count ?? 0) - 1)); DaySelected?.Invoke(_index); InvalidateVisual(); }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Plan is not { Timeline.Count: > 0 }) return;
        Select((int)Math.Round((e.GetPosition(this).X - 42) / Math.Max(1, Bounds.Width - 55) * (Plan.Timeline.Count - 1)));
        var d = Plan.Timeline[_index];
        ToolTip.SetTip(this, $"{d.Date:dd.MM.yyyy}\nС новой: {PlanLabels.Number(d.PlannedClosing)}\nБез новой: {PlanLabels.Number(d.BaselineClosing)}\nПоступит: {PlanLabels.Number(d.Incoming + d.RecommendedIncoming)}");
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); Focus(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        { Select(e.Key switch { Key.Left => _index - 1, Key.Right => _index + 1, Key.Home => 0, _ => int.MaxValue }); e.Handled = true; }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, Bounds.WithX(0).WithY(0));
        if (Plan is not { Timeline.Count: > 0 } plan) return;
        var width = Math.Max(1, Bounds.Width - 55); var height = Math.Max(1, Bounds.Height - 38);
        var max = Math.Max(1, (double)Math.Max(plan.SafetyStock ?? 0, plan.Timeline.Max(d => Math.Max(d.BaselineClosing, d.PlannedClosing)))) * 1.1;
        Point PointAt(int index, decimal value) => new(42 + index * width / Math.Max(1, plan.Timeline.Count - 1), 10 + height - (double)value / max * height);
        var muted = new SolidColorBrush(Color.Parse("#8899AC"));
        void Text(string text, Point point) => context.DrawText(new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, muted), point);
        for (var i = 0; i <= 4; i++)
        { var yy = 10 + height * i / 4; context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#34445A")), .5), new(42, yy), new(42 + width, yy)); Text((max * (4-i)/4).ToString("0"), new(0, yy-7)); }
        var safetyY = PointAt(0, plan.SafetyStock ?? 0).Y;
        context.DrawLine(new Pen(Brushes.Orange,1,DashStyle.Dash),new(42,safetyY),new(42+width,safetyY));
        for (var i = 1; i < plan.Timeline.Count; i++)
        { context.DrawLine(new Pen(muted,1.8,DashStyle.Dash),PointAt(i-1,plan.Timeline[i-1].BaselineClosing),PointAt(i,plan.Timeline[i].BaselineClosing)); context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#7298FF")),2.5),PointAt(i-1,plan.Timeline[i-1].PlannedClosing),PointAt(i,plan.Timeline[i].PlannedClosing)); }
        var selected = PointAt(_index,plan.Timeline[_index].PlannedClosing);
        context.DrawLine(new Pen(muted,.7),new(selected.X,10),new(selected.X,10+height));
        context.DrawEllipse(Brushes.CornflowerBlue,null,selected,4,4);
        Text(plan.Timeline[0].Date.ToString("dd.MM"),new(42,height+18));
        Text(plan.Timeline[^1].Date.ToString("dd.MM"),new(42+width-35,height+18));
    }
}
