using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DesktopSpace {
 internal sealed class ToastWindow : Window {
  const long NoActivateStyle = 0x08000000, ClickThroughStyle = 0x20;
  readonly TextBlock message;
  readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };

  internal ToastWindow() {
   Ui.PopupWindow(this, "桌面空间 · 提示");
   ShowActivated = false;
   SizeToContent = SizeToContent.WidthAndHeight;
   IsHitTestVisible = false;
   message = Ui.Label("", 13, Ui.LightInk);
   message.FontWeight = FontWeights.Medium;
   message.MaxWidth = 420;
   message.TextWrapping = TextWrapping.Wrap;
   var panel = Ui.LightPanel(message, new Thickness(16, 9, 16, 9));
   panel.CornerRadius = new CornerRadius(14);
   Content = panel;
   SourceInitialized += delegate {
    IntPtr h = new WindowInteropHelper(this).Handle;
    long ex = Native.GetWindowLongPtr(h, -20).ToInt64();
    Native.SetWindowLongPtr(h, -20, new IntPtr(ex | 0x80 | NoActivateStyle | ClickThroughStyle));
   };
   timer.Tick += delegate {
    timer.Stop();
    var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
    fade.Completed += delegate { if (timer.IsEnabled == false) Hide(); };
    BeginAnimation(OpacityProperty, fade);
   };
  }

  internal void ShowText(string text, DockWindow dock) {
   if (String.IsNullOrWhiteSpace(text)) return;
   timer.Stop();
   BeginAnimation(OpacityProperty, null);
   message.Text = text;
   UpdateLayout();
   double w = ActualWidth > 0 ? ActualWidth : 220;
   double h = ActualHeight > 0 ? ActualHeight : 40;
   Left = Math.Max(12, (SystemParameters.PrimaryScreenWidth - w) / 2);
   double baseTop = dock != null ? dock.Top : SystemParameters.PrimaryScreenHeight - 86;
   Top = Math.Max(12, baseTop - h - 12);
   Opacity = 0;
   if (!IsVisible) Show();
   BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
   timer.Start();
  }
 }
}
