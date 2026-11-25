using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DesktopSpace {
 public static class Ui {
  public static double Scale=1;
  public static readonly Brush Text=Brush("#F5F7FC"),Muted=Brush("#BBC1CE"),Surface=Brush("#F2222731"),Line=Brush("#24FFFFFF");
  internal static readonly Brush LightInk=Brush("#303B4A"),LightMuted=Brush("#536375"),LightSurface=Brush("#F2F1F4F7"),LightOutline=Brush("#F9FFFFFF"),LightHover=Brush("#183B5677"),LightPressed=Brush("#303B5677");
  internal static readonly Brush LightTile=Brush("#B8FFFFFF"),LightAccent=Brush("#2B5B8C");
  internal static readonly Brush LightGlass=MakeGlass();
  static Brush MakeGlass(){var b=new LinearGradientBrush(Color.FromArgb(246,250,252,255),Color.FromArgb(238,226,236,248),new Point(0,0),new Point(0,1));b.Freeze();return b;}
  internal static Border LightPanel(UIElement child,Thickness padding){var p=Panel(child,padding);p.Background=LightGlass;p.BorderBrush=LightOutline;p.CornerRadius=new CornerRadius(22);return p;}
  public static SolidColorBrush Brush(string color){var result=(SolidColorBrush)new BrushConverter().ConvertFromString(color);result.Freeze();return result;}
  public static TextBlock Label(string text,double size,Brush color){return new TextBlock{Text=text,FontSize=size,Foreground=color,VerticalAlignment=VerticalAlignment.Center};}
  public static Button Button(object content,Action action){var b=new Button{Content=content,Background=Brushes.Transparent,Foreground=Text,BorderThickness=new Thickness(0),Padding=new Thickness(8),Cursor=System.Windows.Input.Cursors.Hand,FontSize=13};b.Click+=delegate{action();};return b;}
  public static Image Image(ImageSource source,double size){return new Image{Source=source,Width=size,Height=size,Stretch=Stretch.Uniform,SnapsToDevicePixels=true};}
  public static FrameworkElement Symbol(string name,double size,Brush color=null){
   color=color??Text;
   string path=name=="search"?"M10 3 A7 7 0 1 0 10 17 A7 7 0 1 0 10 3 M15 15 L22 22":name=="close"?"M6 6 L18 18 M18 6 L6 18":name=="grid"?"M3 3 H9 V9 H3 Z M15 3 H21 V9 H15 Z M3 15 H9 V21 H3 Z M15 15 H21 V21 H15 Z":name=="wifi"?"M2 8 Q12 0 22 8 M5 12 Q12 6 19 12 M9 16 Q12 13 15 16 M12 20 L12 20.2":name=="volume"?"M3 9 H7 L12 5 V19 L7 15 H3 Z M16 8 Q22 12 16 16 M19 5 Q29 12 19 19":name=="mute"?"M3 9 H7 L12 5 V19 L7 15 H3 Z M16 9 L22 15 M22 9 L16 15":name=="tray"?"M7 15 L12 10 L17 15 M5 20 H19 M5 20 V17 M19 20 V17":name=="settings"?"M9 3 H15 L16 6 L19 7 V10 L21 12 L19 14 V17 L16 18 L15 21 H9 L8 18 L5 17 V14 L3 12 L5 10 V7 L8 6 Z M12 9 A3 3 0 1 0 12 15 A3 3 0 1 0 12 9":name=="left"?"M15 5 L8 12 L15 19":name=="right"?"M9 5 L16 12 L9 19":name=="windows"?"M3 3 H11 V11 H3 Z M13 3 H21 V11 H13 Z M3 13 H11 V21 H3 Z M13 13 H21 V21 H13 Z":"M3 5 H21 V19 H3 Z M3 9 H21";
   var shape=new System.Windows.Shapes.Path{Data=Geometry.Parse(path),Stroke=color,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Stretch=Stretch.Uniform,Width=size,Height=size};if(name=="windows"){shape.Fill=color;shape.StrokeThickness=0;}
   return new Viewbox{Width=size,Height=size,Child=shape,Stretch=Stretch.Uniform};
  }
  public static ImageSource SymbolImage(string text){var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){var formatted=new FormattedText(text,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Symbol"),48,Text,1.0);dc.DrawText(formatted,new Point(6,0));}var result=new RenderTargetBitmap(64,64,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return result;}
  public static void Configure(Application app){
   app.Resources[typeof(Window)]=new Style(typeof(Window)){Setters={new Setter(Control.FontFamilyProperty,new FontFamily("Microsoft YaHei UI")),new Setter(Control.FontSizeProperty,14.0),new Setter(Control.ForegroundProperty,Text)}};
   app.Resources[typeof(Button)]=ButtonStyle(Brush("#1CFFFFFF"),Brush("#30FFFFFF"));
   var itemStyle=new Style(typeof(MenuItem));itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,LightInk));itemStyle.Setters.Add(new Setter(Control.FontSizeProperty,13.0));app.Resources[typeof(MenuItem)]=itemStyle;
   var menuStyle=new Style(typeof(ContextMenu));menuStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#FAF7F9FC")));menuStyle.Setters.Add(new Setter(Control.ForegroundProperty,LightInk));menuStyle.Setters.Add(new Setter(Control.BorderBrushProperty,Brush("#33405A78")));menuStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5)));app.Resources[typeof(ContextMenu)]=menuStyle;
  }
  internal static Style ButtonStyle(Brush hoverColor,Brush pressColor,bool fadeHover=false){
   var style=new Style(typeof(Button));style.Setters.Add(new Setter(Control.FocusVisualStyleProperty,null));
   var template=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.Name="surface";border.SetValue(Border.CornerRadiusProperty,new CornerRadius(11));border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
   var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);presenter.SetBinding(FrameworkElement.MarginProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
   var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};
   if(fadeHover&&SystemParameters.ClientAreaAnimation){
    var layers=new FrameworkElementFactory(typeof(Grid));
    var highlight=new FrameworkElementFactory(typeof(Border),"hoverHighlight");
    highlight.SetValue(Border.CornerRadiusProperty,new CornerRadius(11));
    highlight.SetValue(Border.BackgroundProperty,hoverColor);
    highlight.SetValue(UIElement.OpacityProperty,0d);
    highlight.SetValue(UIElement.IsHitTestVisibleProperty,false);
    layers.AppendChild(highlight);
    layers.AppendChild(presenter);
    border.AppendChild(layers);
    hover.EnterActions.Add(HoverTransition(1,110));
    hover.ExitActions.Add(HoverTransition(0,150));
   }else{
    border.AppendChild(presenter);
    hover.Setters.Add(new Setter(Border.BackgroundProperty,hoverColor,"surface"));
   }
   template.VisualTree=border;
   template.Triggers.Add(hover);
   var press=new Trigger{Property=ButtonBase.IsPressedProperty,Value=true};
   press.Setters.Add(new Setter(Border.BackgroundProperty,pressColor,"surface"));
   template.Triggers.Add(press);
   style.Setters.Add(new Setter(Control.TemplateProperty,template));
   return style;
  }
  static BeginStoryboard HoverTransition(double opacity,int milliseconds){
   var fade=new DoubleAnimation{To=opacity,Duration=TimeSpan.FromMilliseconds(milliseconds)};
   Storyboard.SetTargetName(fade,"hoverHighlight");
   Storyboard.SetTargetProperty(fade,new PropertyPath(UIElement.OpacityProperty));
   var timeline=new Storyboard();
   timeline.Children.Add(fade);
   return new BeginStoryboard{Storyboard=timeline};
  }
  public static Border Panel(UIElement child,Thickness padding){return new Border{CornerRadius=new CornerRadius(25),Background=Surface,BorderBrush=Line,BorderThickness=new Thickness(1),Padding=padding,Child=child};}
  public static void PopupWindow(Window window,string title){window.Title=title;window.WindowStyle=WindowStyle.None;window.ResizeMode=ResizeMode.NoResize;window.AllowsTransparency=true;window.Background=Brushes.Transparent;window.ShowInTaskbar=false;window.Topmost=true;window.SizeToContent=SizeToContent.Manual;window.SnapsToDevicePixels=true;}
  public static void FadeIn(Window window){window.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(110)));}
  public static void Export(FrameworkElement element,string path,double width,double height){element.Measure(new Size(width,height));element.Arrange(new Rect(0,0,width,height));element.UpdateLayout();var image=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);image.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(path))encoder.Save(file);}
 }
}

