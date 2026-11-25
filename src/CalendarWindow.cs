using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopSpace {
 internal sealed class CalendarWindow : Window {
  static readonly string[] WeekNames = { "一", "二", "三", "四", "五", "六", "日" };
  readonly TextBlock timeLabel, dateLabel, monthLabel;
  readonly UniformGrid days = new UniformGrid { Columns = 7, Rows = 6 };
  readonly DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
  DateTime month;
  DateTime lastHidden = DateTime.MinValue;

  internal CalendarWindow() {
   Ui.PopupWindow(this, "桌面空间 · 日历");
   Width = 316;
   Height = 418;
   Resources[typeof(Button)] = Ui.ButtonStyle(Ui.LightHover, Ui.LightPressed);
   var body = new StackPanel();

   timeLabel = Ui.Label("", 30, Ui.LightInk);
   timeLabel.FontWeight = FontWeights.SemiBold;
   body.Children.Add(timeLabel);
   dateLabel = Ui.Label("", 13, Ui.LightMuted);
   dateLabel.Margin = new Thickness(1, 2, 0, 14);
   body.Children.Add(dateLabel);
   body.Children.Add(new Border { Height = 1, Background = Ui.Brush("#22405A78"), Margin = new Thickness(0, 0, 0, 10) });

   var nav = new Grid { Margin = new Thickness(0, 0, 0, 6) };
   nav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
   nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   monthLabel = Ui.Label("", 14, Ui.LightInk);
   monthLabel.FontWeight = FontWeights.SemiBold;
   monthLabel.Cursor = Cursors.Hand;
   monthLabel.ToolTip = "回到今天";
   monthLabel.MouseLeftButtonUp += delegate { month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); Draw(); };
   nav.Children.Add(monthLabel);
   var prev = Ui.Button(Ui.Symbol("left", 12, Ui.LightInk), delegate { month = month.AddMonths(-1); Draw(); });
   prev.Width = 30; prev.Height = 28; prev.Padding = new Thickness(6);
   Grid.SetColumn(prev, 1); nav.Children.Add(prev);
   var next = Ui.Button(Ui.Symbol("right", 12, Ui.LightInk), delegate { month = month.AddMonths(1); Draw(); });
   next.Width = 30; next.Height = 28; next.Padding = new Thickness(6);
   Grid.SetColumn(next, 2); nav.Children.Add(next);
   body.Children.Add(nav);

   var week = new UniformGrid { Columns = 7, Rows = 1, Margin = new Thickness(0, 0, 0, 2) };
   foreach (string w in WeekNames) {
    var t = Ui.Label(w, 12, Ui.LightMuted);
    t.HorizontalAlignment = HorizontalAlignment.Center;
    week.Children.Add(t);
   }
   body.Children.Add(week);
   days.Height = 210;
   body.Children.Add(days);

   var center = Ui.Button(Ui.Label("通知中心与系统日历", 13, Ui.LightInk), delegate {
    Hide();
    Native.SendWinCombo(0x4E);
   });
   center.Background = Ui.LightTile;
   center.Height = 38;
   center.Margin = new Thickness(0, 10, 0, 0);
   body.Children.Add(center);

   Content = Ui.LightPanel(body, new Thickness(20, 16, 20, 16));
   tick.Tick += delegate { UpdateTime(); };
   IsVisibleChanged += delegate { if (IsVisible) tick.Start(); else { tick.Stop(); lastHidden = DateTime.UtcNow; } };
   Deactivated += delegate { Hide(); };
   PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
   PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e) { month = month.AddMonths(e.Delta > 0 ? -1 : 1); Draw(); e.Handled = true; };
  }

  void UpdateTime() {
   var now = DateTime.Now;
   timeLabel.Text = now.ToString("HH:mm:ss");
   dateLabel.Text = now.ToString("yyyy年M月d日 dddd");
  }

  void Draw() {
   monthLabel.Text = month.ToString("yyyy年M月");
   days.Children.Clear();
   int offset = ((int)month.DayOfWeek + 6) % 7;
   var start = month.AddDays(-offset);
   var today = DateTime.Today;
   for (int i = 0; i < 42; i++) {
    var d = start.AddDays(i);
    bool inMonth = d.Month == month.Month;
    bool isToday = d == today;
    var label = Ui.Label(d.Day.ToString(), 13, isToday ? Brushes.White : (inMonth ? Ui.LightInk : Ui.Brush("#8A9AAB")));
    label.HorizontalAlignment = HorizontalAlignment.Center;
    if (isToday) label.FontWeight = FontWeights.SemiBold;
    var cell = new Border {
     Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
     Background = isToday ? Ui.LightAccent : Brushes.Transparent,
     Child = label, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    days.Children.Add(cell);
   }
  }

  internal void Toggle(FrameworkElement anchor, double dockTop) {
   if (IsVisible) { Hide(); return; }
   if ((DateTime.UtcNow - lastHidden).TotalMilliseconds < 250) return;
   month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
   UpdateTime();
   Draw();
   double center = SystemParameters.PrimaryScreenWidth - Width / 2 - 12;
   var source = anchor != null ? PresentationSource.FromVisual(anchor) : null;
   if (source != null) {
    center = source.CompositionTarget.TransformFromDevice.Transform(anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0))).X;
   }
   Left = Math.Max(12, Math.Min(SystemParameters.PrimaryScreenWidth - Width - 12, center - Width / 2));
   Top = Math.Max(12, dockTop - Height - 6);
   Show();
   Activate();
   Ui.FadeIn(this);
  }
 }
}
