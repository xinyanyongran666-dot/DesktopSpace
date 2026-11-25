using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

namespace DesktopSpace {
 internal static class AudioVolume {
  [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface DeviceEnumerator {
   int EnumAudioEndpoints(int flow, int stateMask, out IntPtr devices);
   int GetDefaultAudioEndpoint(int flow, int role, out AudioDevice device);
  }

  [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface AudioDevice {
   int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
  }

  [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface EndpointVolume {
   int RegisterControlChangeNotify(IntPtr callback);
   int UnregisterControlChangeNotify(IntPtr callback);
   int GetChannelCount(out uint count);
   int SetMasterVolumeLevel(float decibels, ref Guid context);
   int SetMasterVolumeLevelScalar(float level, ref Guid context);
   int GetMasterVolumeLevel(out float decibels);
   int GetMasterVolumeLevelScalar(out float level);
   int SetChannelVolumeLevel(uint channel, float decibels, ref Guid context);
   int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
   int GetChannelVolumeLevel(uint channel, out float decibels);
   int GetChannelVolumeLevelScalar(uint channel, out float level);
   int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
   int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
  }

  static void WithEndpoint(Action<EndpointVolume> action) {
   var enumerator = (DeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
   AudioDevice device = null;
   object endpoint = null;
   try {
    foreach (int role in new[] { 0, 1 }) {
     if (enumerator.GetDefaultAudioEndpoint(0, role, out device) == 0 && device != null) break;
    }
    if (device == null) return;
    Guid iid = typeof(EndpointVolume).GUID;
    if (device.Activate(ref iid, 23, IntPtr.Zero, out endpoint) == 0 && endpoint != null)
     action((EndpointVolume)endpoint);
   } finally {
    if (endpoint != null) Marshal.ReleaseComObject(endpoint);
    if (device != null) Marshal.ReleaseComObject(device);
    Marshal.ReleaseComObject(enumerator);
   }
  }

  internal static int GetPercent() {
   float scalar = 0;
   WithEndpoint(delegate(EndpointVolume endpoint) { endpoint.GetMasterVolumeLevelScalar(out scalar); });
   return (int)Math.Round(scalar * 100);
  }

  internal static void SetPercent(double percent) {
   float target = (float)Math.Max(0, Math.Min(1, percent / 100.0));
   WithEndpoint(delegate(EndpointVolume endpoint) {
    Guid ctx = Guid.Empty;
    endpoint.SetMasterVolumeLevelScalar(target, ref ctx);
    if (target > 0) endpoint.SetMute(false, ref ctx);
   });
  }

  internal static bool GetMute() {
   bool muted = false;
   WithEndpoint(delegate(EndpointVolume endpoint) { endpoint.GetMute(out muted); });
   return muted;
  }

  internal static bool ToggleMute() {
   bool next = false;
   WithEndpoint(delegate(EndpointVolume endpoint) {
    bool muted;
    endpoint.GetMute(out muted);
    next = !muted;
    Guid ctx = Guid.Empty;
    endpoint.SetMute(next, ref ctx);
   });
   return next;
  }
 }

 internal sealed class QuickSettingsWindow : Window {
  readonly ShellHost host;
  readonly TextBlock titleLabel;
  readonly StackPanel networkSection;
  readonly StackPanel soundSection;
  readonly TextBlock networkStatus;
  readonly Slider volume;
  readonly TextBlock volumeStatus;
  readonly Button muteButton;
  readonly TextBlock muteLabel;
  readonly DispatcherTimer refresh = new DispatcherTimer();
  string currentMode = "";
  bool reading;

  internal QuickSettingsWindow(ShellHost shell) {
   host = shell;
   Ui.PopupWindow(this, "桌面空间 · 快捷控制");
   Resources[typeof(Button)] = Ui.ButtonStyle(Ui.LightHover, Ui.LightPressed);
   Width = 304;
   Height = 232;
   var content = new StackPanel();
   titleLabel = Ui.Label("网络设置", 16, Ui.LightInk);
   titleLabel.FontWeight = FontWeights.SemiBold;
   titleLabel.Margin = new Thickness(0, 0, 0, 12);
   content.Children.Add(titleLabel);

   // 1. Network-only section
   networkSection = new StackPanel();
   networkStatus = Ui.Label("正在检测网络连接...", 12, Ui.LightMuted);
   networkStatus.Margin = new Thickness(0, 0, 0, 12);
   networkSection.Children.Add(networkStatus);
   var connections = new UniformGrid { Columns = 3, Rows = 1, Margin = new Thickness(0, 0, 0, 10) };
   connections.Children.Add(Tile("Wi-Fi", "wifi", "ms-settings:network-wifi", new Thickness(0, 0, 4, 0)));
   connections.Children.Add(Tile("蓝牙", null, "ms-settings:bluetooth", new Thickness(4, 0, 4, 0)));
   connections.Children.Add(Tile("热点", null, "ms-settings:network-mobilehotspot", new Thickness(4, 0, 0, 0)));
   networkSection.Children.Add(connections);
   networkSection.Children.Add(Tile("网络与 Internet 设置", "settings", "ms-settings:network", new Thickness(0)));
   content.Children.Add(networkSection);

   // 2. Sound-only section
   soundSection = new StackPanel { Visibility = Visibility.Collapsed };
   var volHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
   volHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
   volHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   var volCaption = Ui.Label("主音量（支持滚轮调节）", 12, Ui.LightMuted);
   volHeader.Children.Add(volCaption);
   volumeStatus = Ui.Label("", 13, Ui.LightInk);
   volumeStatus.FontWeight = FontWeights.SemiBold;
   Grid.SetColumn(volumeStatus, 1);
   volHeader.Children.Add(volumeStatus);
   soundSection.Children.Add(volHeader);

   volume = new Slider {
    Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10,
    Height = 30, IsMoveToPointEnabled = true, Margin = new Thickness(0, 2, 0, 12),
    Template = (ControlTemplate)XamlReader.Parse(SliderTemplate)
   };
   AutomationProperties.SetName(volume, "系统音量");
   volume.ValueChanged += delegate {
    if (reading || !IsVisible) return;
    try {
     AudioVolume.SetPercent(volume.Value);
     volumeStatus.Text = Math.Round(volume.Value) + "%";
     UpdateMuteLabel(false);
    } catch (Exception error) {
     Store.Error("volume", error);
     volumeStatus.Text = "暂时无法调节音量";
    }
   };
   soundSection.Children.Add(volume);

   var audioActions = new UniformGrid { Columns = 2, Rows = 1, Margin = new Thickness(0, 0, 0, 8) };
   muteLabel = Ui.Label("静音切换", 13, Ui.LightInk);
   var muteRow = new StackPanel { Orientation = Orientation.Horizontal };
   var muteGlyph = Ui.Symbol("mute", 16, Ui.LightInk);
   muteGlyph.Margin = new Thickness(0, 0, 7, 0);
   muteRow.Children.Add(muteGlyph);
   muteRow.Children.Add(muteLabel);
   muteButton = Ui.Button(muteRow, delegate {
    try {
     bool muted = AudioVolume.ToggleMute();
     UpdateMuteLabel(muted);
     ReadVolume();
    } catch { }
   });
   muteButton.Height = 40;
   muteButton.Margin = new Thickness(0, 0, 4, 0);
   muteButton.Background = Ui.LightTile;
   audioActions.Children.Add(muteButton);
   audioActions.Children.Add(Tile("音量合成器", null, "ms-settings:apps-volume", new Thickness(4, 0, 0, 0), 40));
   soundSection.Children.Add(audioActions);
   soundSection.Children.Add(Tile("声音与输出设备设置", "settings", "ms-settings:sound", new Thickness(0), 40));
   content.Children.Add(soundSection);

   var panel = Ui.LightPanel(content, new Thickness(20));
   Content = panel;

   PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) {
    if (currentMode == "volume") {
     try {
      int next = Math.Max(0, Math.Min(100, AudioVolume.GetPercent() + (e.Delta > 0 ? 4 : -4)));
      AudioVolume.SetPercent(next);
      ReadVolume();
      e.Handled = true;
     } catch { }
    }
   };

   refresh.Interval = TimeSpan.FromMilliseconds(700);
   refresh.Tick += delegate {
    if (currentMode == "volume" && !Mouse.LeftButton.Equals(MouseButtonState.Pressed)) ReadVolume();
    else if (currentMode == "wifi") ReadNetwork();
   };
   IsVisibleChanged += delegate { if (IsVisible) refresh.Start(); else refresh.Stop(); };
   Deactivated += delegate { Hide(); };
   PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
  }

  void UpdateMuteLabel(bool muted) {
   if (muteLabel != null) muteLabel.Text = muted ? "取消静音" : "静音";
   if (muteButton != null) muteButton.Background = muted ? Ui.Brush("#4D5C8AE6") : Ui.LightTile;
  }

  Button Tile(string text, string icon, string uri, Thickness margin, double height = 43) {
   var row = new StackPanel { Orientation = Orientation.Horizontal };
   if (icon != null) {
    var glyph = Ui.Symbol(icon, 16, Ui.LightInk);
    glyph.Margin = new Thickness(0, 0, 7, 0);
    row.Children.Add(glyph);
   }
   row.Children.Add(Ui.Label(text, 13, Ui.LightInk));
   var button = Ui.Button(row, delegate {
    Hide();
    try { LauncherService.OpenUri(uri); }
    catch (Exception error) { Store.Error("system settings", error); host.Notify("暂时无法打开 Windows 设置。"); }
   });
   button.Height = height;
   button.Margin = margin;
   button.Background = Ui.LightTile;
   AutomationProperties.SetName(button, text);
   return button;
  }

  void ReadNetwork() {
   try {
    bool connected = NetworkInterface.GetIsNetworkAvailable();
    string activeName = null;
    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
     if (nic.OperationalStatus == OperationalStatus.Up &&
         nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
         nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel) {
      activeName = nic.Name;
      break;
     }
    }
    networkStatus.Text = connected && !String.IsNullOrEmpty(activeName)
     ? "已连接: " + activeName
     : (connected ? "网络已连接" : "未连接网络");
   } catch {
    networkStatus.Text = "快捷网络与蓝牙控制";
   }
  }

  internal void RefreshVolumeDisplay() {
   if (IsVisible && currentMode == "volume") ReadVolume();
  }

  void ReadVolume() {
   reading = true;
   try {
    int percent = AudioVolume.GetPercent();
    bool muted = AudioVolume.GetMute();
    volume.IsEnabled = true;
    volume.Value = percent;
    volumeStatus.Text = muted ? (percent + "% (已静音)") : (percent + "%");
    UpdateMuteLabel(muted);
   } catch (Exception error) {
    Store.Error("read volume", error);
    volume.IsEnabled = false;
    volumeStatus.Text = "未找到可用音频设备";
   } finally { reading = false; }
  }

  internal void Open(FrameworkElement anchor, string mode = "volume") {
   string requested = String.IsNullOrEmpty(mode) ? "volume" : mode;
   if (IsVisible && currentMode == requested) { Hide(); return; }
   currentMode = requested;
   if (currentMode == "wifi") {
    titleLabel.Text = "网络与连接";
    networkSection.Visibility = Visibility.Visible;
    soundSection.Visibility = Visibility.Collapsed;
    Height = 220;
    ReadNetwork();
   } else {
    titleLabel.Text = "声音与音量";
    networkSection.Visibility = Visibility.Collapsed;
    soundSection.Visibility = Visibility.Visible;
    Height = 236;
    ReadVolume();
   }
   var dock = host.Dock;
   double center = dock == null ? SystemParameters.PrimaryScreenWidth / 2 : dock.Left + dock.ActualWidth - Width / 2;
   if (anchor != null && PresentationSource.FromVisual(anchor) != null) {
    var transform = PresentationSource.FromVisual(anchor).CompositionTarget.TransformFromDevice;
    center = transform.Transform(anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0))).X;
   }
   Left = Math.Max(12, Math.Min(SystemParameters.PrimaryScreenWidth - Width - 12, center - Width / 2));
   Top = Math.Max(12, (dock == null ? SystemParameters.PrimaryScreenHeight - 86 : dock.Top) - Height - 10);
   if (!IsVisible) Show();
   Activate();
   Ui.FadeIn(this);
  }

  const string SliderTemplate = @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Slider'>
   <Grid Background='Transparent'>
    <Border Height='6' CornerRadius='3' Background='#D3DCE6' VerticalAlignment='Center'/>
    <Track Name='PART_Track' Height='28' VerticalAlignment='Center' Focusable='False'>
     <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Grid Background='Transparent'><Border Height='6' CornerRadius='3' Background='#3D6A99' VerticalAlignment='Center'/></Grid></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
     <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
     <Track.Thumb><Thumb Width='15' Height='28'><Thumb.Template><ControlTemplate TargetType='Thumb'><Grid Background='Transparent'><Ellipse Width='15' Height='15' Fill='#FFFFFF' Stroke='#3D6A99' StrokeThickness='2'/></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
    </Track>
   </Grid>
  </ControlTemplate>";
 }
}
