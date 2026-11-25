using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopSpace {
 public sealed class PreviewWindow:Window {
  [StructLayout(LayoutKind.Sequential)]struct SIZE{public int Width,Height;}
  [StructLayout(LayoutKind.Sequential)]struct PROPS{public uint Flags;public Native.RECT Destination,Source;public byte Opacity;[MarshalAs(UnmanagedType.Bool)]public bool Visible;[MarshalAs(UnmanagedType.Bool)]public bool ClientOnly;}
  [DllImport("dwmapi.dll")]static extern int DwmRegisterThumbnail(IntPtr destination,IntPtr source,out IntPtr thumbnail);
  [DllImport("dwmapi.dll")]static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
  [DllImport("dwmapi.dll")]static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail,out SIZE size);
  [DllImport("dwmapi.dll")]static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,ref PROPS props);
  const int HeaderHeight=30,OuterPad=8,CardGap=8,ThumbInset=6;
  readonly List<IntPtr> thumbnails=new List<IntPtr>();
  readonly StackPanel cards=new StackPanel{Orientation=Orientation.Horizontal};

  public PreviewWindow(){
   Title="桌面空间 · 窗口预览";
   WindowStyle=WindowStyle.None;
   ResizeMode=ResizeMode.NoResize;
   ShowInTaskbar=false;
   ShowActivated=false;
   Topmost=true;
   Background=Ui.Brush("#FFF4F7FB");
   Resources[typeof(Button)]=Ui.ButtonStyle(Ui.LightHover,Ui.LightPressed);
   var frame=new Border{
    BorderBrush=Ui.Brush("#33405A78"),
    BorderThickness=new Thickness(1),
    Padding=new Thickness(OuterPad),
    Child=cards
   };
   Content=frame;
   SourceInitialized+=delegate{
    IntPtr h=new WindowInteropHelper(this).Handle;
    long ex=Native.GetWindowLongPtr(h,-20).ToInt64();
    Native.SetWindowLongPtr(h,-20,new IntPtr(ex|0x80|0x08000000));
    int round=2;
    try{Native.DwmSetWindowAttribute(h,33,ref round,sizeof(int));}catch{ }
   };
   Closed+=delegate{HidePreview();};
  }

  public void ShowFor(List<TaskEntry> items,FrameworkElement owner){
   HidePreview();
   if(items==null||items.Count==0)return;
   var valid=new List<TaskEntry>();
   foreach(var t in items)if(Native.IsWindow(t.Handle))valid.Add(t);
   if(valid.Count==0)return;
   int count=Math.Min(5,valid.Count);
   double maxScreenW=SystemParameters.PrimaryScreenWidth-24;
   double thumbW=count==1?240:Math.Max(155,Math.Min(215,(maxScreenW-OuterPad*2-CardGap*(count-1))/count-ThumbInset*2));
   double cardW=thumbW+ThumbInset*2;
   var hwnd=new WindowInteropHelper(this).EnsureHandle();
   var regs=new List<Tuple<IntPtr,double,double>>();
   double maxThumbH=96;
   for(int i=0;i<count;i++){
    IntPtr thumb;
    if(DwmRegisterThumbnail(hwnd,valid[i].Handle,out thumb)!=0){
     regs.Add(Tuple.Create(IntPtr.Zero,0.0,0.0));
     continue;
    }
    thumbnails.Add(thumb);
    SIZE size;
    double th=140;
    if(DwmQueryThumbnailSourceSize(thumb,out size)==0&&size.Width>0&&size.Height>0){
     th=Math.Max(68,Math.Min(count==1?185:145,thumbW*size.Height/size.Width));
    }
    if(th>maxThumbH)maxThumbH=th;
    regs.Add(Tuple.Create(thumb,thumbW,th));
   }
   double cardH=HeaderHeight+maxThumbH+ThumbInset;
   cards.Children.Clear();
   for(int i=0;i<count;i++){
    var task=valid[i];
    var card=BuildCard(task,cardW,cardH,i<count-1?CardGap:0);
    cards.Children.Add(card);
   }
   Width=OuterPad*2+count*cardW+(count-1)*CardGap;
   Height=OuterPad*2+cardH;
   Point anchor=owner.PointToScreen(new Point(owner.ActualWidth/2,0));
   var scale=PresentationSource.FromVisual(owner).CompositionTarget.TransformFromDevice;
   anchor=scale.Transform(anchor);
   Left=Math.Max(5,Math.Min(SystemParameters.PrimaryScreenWidth-Width-5,anchor.X-Width/2));
   Top=Math.Max(5,anchor.Y-Height-12);
   Show();
   for(int i=0;i<count;i++){
    IntPtr thumb=regs[i].Item1;
    if(thumb==IntPtr.Zero)continue;
    double actualH=regs[i].Item3;
    double cardLeft=OuterPad+i*(cardW+CardGap)+ThumbInset;
    double cardTop=OuterPad+HeaderHeight+(maxThumbH-actualH)/2.0;
    var props=new PROPS{
     Flags=1|4|8|16,
     Destination=new Native.RECT{
      Left=(int)Math.Round(cardLeft*Ui.Scale),
      Top=(int)Math.Round(cardTop*Ui.Scale),
      Right=(int)Math.Round((cardLeft+thumbW)*Ui.Scale),
      Bottom=(int)Math.Round((cardTop+actualH)*Ui.Scale)
     },
     Opacity=255,
     Visible=true,
     ClientOnly=true
    };
    DwmUpdateThumbnailProperties(thumb,ref props);
   }
  }

  Border BuildCard(TaskEntry task,double width,double height,double rightMargin){
   var titleLabel=Ui.Label(String.IsNullOrWhiteSpace(task.Title)?"窗口预览":task.Title,12,Ui.LightInk);
   titleLabel.TextTrimming=TextTrimming.CharacterEllipsis;
   titleLabel.Margin=new Thickness(6,0,4,0);
   var closeBtn=Ui.Button(Ui.Symbol("close",11,Ui.LightMuted),delegate{
    Native.PostMessage(task.Handle,0x10,IntPtr.Zero,IntPtr.Zero);
    HidePreview();
   });
   closeBtn.Width=22;closeBtn.Height=22;closeBtn.Padding=new Thickness(4);closeBtn.Margin=new Thickness(0,0,2,0);closeBtn.ToolTip="关闭此窗口";
   var header=new Grid{Height=HeaderHeight,VerticalAlignment=VerticalAlignment.Top};
   header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
   header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   header.Children.Add(titleLabel);
   Grid.SetColumn(closeBtn,1);
   header.Children.Add(closeBtn);
   var card=new Border{
    Width=width,Height=height,
    CornerRadius=new CornerRadius(8),
    Background=Brushes.Transparent,
    Margin=new Thickness(0,0,rightMargin,0),
    Cursor=System.Windows.Input.Cursors.Hand,
    Child=header
   };
   card.MouseEnter+=delegate{card.Background=Ui.LightHover;};
   card.MouseLeave+=delegate{card.Background=Brushes.Transparent;};
   card.MouseLeftButtonUp+=delegate{
    if(closeBtn.IsMouseOver)return;
    Native.Activate(task.Handle);
    HidePreview();
   };
   return card;
  }

  public void HidePreview(){
   foreach(IntPtr thumb in thumbnails){
    if(thumb!=IntPtr.Zero)DwmUnregisterThumbnail(thumb);
   }
   thumbnails.Clear();
   if(IsVisible)Hide();
  }
 }
}
