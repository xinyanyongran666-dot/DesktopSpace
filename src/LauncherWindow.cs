using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Data;

namespace DesktopSpace {
 public sealed class LauncherWindow:Window {
  static readonly Brush SelectedFilter=Ui.Brush("#FFD8E3EE");
  readonly ShellHost host;
  readonly ScaleTransform openingScale=new ScaleTransform();
  readonly TranslateTransform openingOffset=new TranslateTransform();
  const double InitialScale=.92,InitialOffset=12;
  int openingFrames;
  IntPtr cloakedWindow;
  readonly TextBlock heading=Ui.Label("其他应用",22,Ui.LightInk),count=Ui.Label("",12,Ui.LightMuted),footer=Ui.Label("",12,Ui.LightMuted),pageLabel=Ui.Label("",12,Ui.LightMuted);
  readonly TextBox search=new TextBox();readonly UniformGrid grid=new UniformGrid();readonly StackPanel filterBar=new StackPanel{Orientation=Orientation.Horizontal};
  readonly Button previous,next,back;readonly Stack<string> folders=new Stack<string>();
  readonly ContextMenu itemMenu;
  readonly Dictionary<string,Button> filters=new Dictionary<string,Button>();
  static readonly string[] Categories={"全部","工具","学习","娱乐","文件"};
  List<AppEntry> items=new List<AppEntry>(),shown=new List<AppEntry>();string category="全部";int page,selectedIndex;Point dragStart;AppEntry dragItem;
  readonly List<Button> tiles=new List<Button>();
  bool catalogDirty,releasing;
  int renderedColumns;
  double renderedWidth;
  DateTime lastDeactivated=DateTime.MinValue;
  public bool JustDeactivated {get{return (DateTime.UtcNow-lastDeactivated).TotalMilliseconds<350;}}
  public FrameworkElement Surface{get;private set;}
  public LauncherWindow(ShellHost h){host=h;Ui.PopupWindow(this,"桌面空间 · 全部应用");ShowActivated=false;Width=790;Height=646;
   itemMenu=CreateItemMenu();
   Resources[typeof(Button)]=Ui.ButtonStyle(Ui.LightHover,Ui.LightPressed);
   SourceInitialized+=delegate{
    const int transitionsForceDisabled=3;
    int enabled=1;
    Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,transitionsForceDisabled,ref enabled,sizeof(int));
   };
   var layout=new Grid();foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})layout.RowDefinitions.Add(new RowDefinition{Height=height});
   var top=new Grid{Margin=new Thickness(0,0,0,17)};top.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});top.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   back=Ui.Button(Ui.Symbol("left",17,Ui.LightInk),Back);back.Visibility=Visibility.Collapsed;back.Margin=new Thickness(0,0,8,0);top.Children.Add(back);
   var titleRow=new StackPanel{Orientation=Orientation.Horizontal};heading.FontWeight=FontWeights.SemiBold;titleRow.Children.Add(heading);count.Margin=new Thickness(12,5,0,0);titleRow.Children.Add(count);Grid.SetColumn(titleRow,1);top.Children.Add(titleRow);
   var close=Ui.Button(Ui.Symbol("close",16,Ui.LightInk),Dismiss);close.ToolTip="收起 (Esc)";close.Width=30;close.Height=30;Grid.SetColumn(close,2);top.Children.Add(close);layout.Children.Add(top);
   var searchRow=new Grid();searchRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});searchRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});searchRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var magnify=Ui.Symbol("search",17,Ui.LightInk);magnify.Margin=new Thickness(13,0,7,0);searchRow.Children.Add(magnify);
   search.Background=Brushes.Transparent;search.Foreground=Ui.LightInk;search.BorderThickness=new Thickness(0);search.CaretBrush=Ui.LightInk;search.SelectionBrush=SelectedFilter;search.SelectionTextBrush=Ui.LightInk;search.FontSize=14;search.Padding=new Thickness(5,9,5,9);search.VerticalContentAlignment=VerticalAlignment.Center;search.ToolTip="搜索应用或文件";Grid.SetColumn(search,1);searchRow.Children.Add(search);var key=Ui.Label("搜索  /  Esc 收起",11,Ui.LightMuted);key.Margin=new Thickness(5,0,12,0);Grid.SetColumn(key,2);searchRow.Children.Add(key);
   var watermark=Ui.Label("搜索应用或文件（支持拼音首字母，如 wyy）",14,Ui.LightMuted);watermark.IsHitTestVisible=false;watermark.Margin=new Thickness(6,0,0,0);Grid.SetColumn(watermark,1);searchRow.Children.Add(watermark);search.TextChanged+=delegate{watermark.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;};
   var searchBorder=new Border{Background=Ui.Brush("#FCFBFCFD"),BorderBrush=Ui.Brush("#FFD1DAE3"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Height=43,Child=searchRow,Margin=new Thickness(0,0,0,16)};Grid.SetRow(searchBorder,1);layout.Children.Add(searchBorder);
   foreach(string c in new[]{"全部","工具","学习","娱乐","文件"}){string value=c;var b=Ui.Button(c,delegate{category=value;page=0;Draw();});b.Foreground=Ui.LightInk;b.Padding=new Thickness(14,7,14,7);b.Margin=new Thickness(0,0,6,0);filters[c]=b;filterBar.Children.Add(b);}filterBar.Margin=new Thickness(0,0,0,14);Grid.SetRow(filterBar,2);layout.Children.Add(filterBar);
   grid.Columns=h.Settings.Columns;grid.Rows=h.Settings.Rows;Grid.SetRow(grid,3);layout.Children.Add(grid);
   var bottom=new Grid{Margin=new Thickness(0,14,0,0)};bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});bottom.Children.Add(footer);
   var pages=new StackPanel{Orientation=Orientation.Horizontal};previous=Ui.Button(Ui.Symbol("left",13,Ui.LightInk),delegate{page--;Draw();});next=Ui.Button(Ui.Symbol("right",13,Ui.LightInk),delegate{page++;Draw();});pageLabel.Margin=new Thickness(9,0,9,0);pages.Children.Add(previous);pages.Children.Add(pageLabel);pages.Children.Add(next);Grid.SetColumn(pages,1);bottom.Children.Add(pages);Grid.SetRow(bottom,4);layout.Children.Add(bottom);
   var panel=Ui.Panel(layout,new Thickness(27,24,27,19));panel.Background=Ui.LightSurface;panel.BorderBrush=Ui.LightOutline;panel.Margin=new Thickness(7);Surface=panel;Content=panel;
   panel.RenderTransformOrigin=new Point(.5,.86);
   panel.RenderTransform=new TransformGroup{Children={openingScale,openingOffset}};
   IsVisibleChanged+=delegate{if(!IsVisible)ResetOpening();};
   search.TextChanged+=delegate{if(IsVisible){page=0;Draw();}};search.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter&&shown.Count>0){int idx=Math.Max(0,Math.Min(selectedIndex,shown.Count-1));Open(shown[idx]);e.Handled=true;}};
   PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e){
    if(e.Delta<0&&next.IsEnabled){page++;Draw();e.Handled=true;}
    else if(e.Delta>0&&previous.IsEnabled){page--;Draw();e.Handled=true;}
   };
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e){
    if(e.Key==Key.PageDown&&next.IsEnabled){page++;Draw();e.Handled=true;return;}
    if(e.Key==Key.PageUp&&previous.IsEnabled){page--;Draw();e.Handled=true;return;}
    if(e.Key==Key.Tab){
     int ci=(Array.IndexOf(Categories,category)+1)%Categories.Length;
     category=Categories[ci];page=0;Draw();e.Handled=true;return;
    }
    if(shown.Count==0)return;
    int cols=Math.Max(1,grid.Columns);
    if(e.Key==Key.Down){MoveSelection(cols);e.Handled=true;}
    else if(e.Key==Key.Up){MoveSelection(-cols);e.Handled=true;}
    else if(e.Key==Key.Right&&(search.Text.Length==0||!search.IsKeyboardFocused)){MoveSelection(1);e.Handled=true;}
    else if(e.Key==Key.Left&&(search.Text.Length==0||!search.IsKeyboardFocused)){MoveSelection(-1);e.Handled=true;}
   };
   KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){Dismiss();e.Handled=true;}};Deactivated+=delegate{if(IsVisible){lastDeactivated=DateTime.UtcNow;Hide();}};Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!releasing){e.Cancel=true;Hide();}};
   Refresh();
  }
  internal void Release(){releasing=true;Close();}
  internal void InvalidateCatalog(){catalogDirty=true;}
  public void Refresh(){try{items=folders.Count==0?host.Catalog.Desktop(false):host.Catalog.Folder(folders.Peek());catalogDirty=false;Draw();}catch(Exception e){Store.Error("launcher refresh",e);host.Notify("暂时无法读取该文件夹。");}}
  public void Reveal(){
   host.HidePreviews();
   bool refresh=catalogDirty||folders.Count>0;
   folders.Clear();
   category="全部";
   page=0;
   search.Clear();
   double maxWidth=SystemParameters.PrimaryScreenWidth-36;
   Width=Math.Min(790,maxWidth);
   Height=Math.Min(646,SystemParameters.PrimaryScreenHeight-150);
   Left=(SystemParameters.PrimaryScreenWidth-Width)/2;
   Top=Math.Max(18,(host.Dock!=null?host.Dock.Top:SystemParameters.PrimaryScreenHeight-90)-Height-18);
   if(refresh)Refresh();else Draw();
   bool animate=SystemParameters.ClientAreaAnimation;
   if(animate){
    Surface.Opacity=0;
    openingScale.ScaleX=openingScale.ScaleY=InitialScale;
    openingOffset.Y=InitialOffset;
    IntPtr handle=new WindowInteropHelper(this).EnsureHandle();
    if(SetCloak(handle,true))cloakedWindow=handle;
   }
   Show();
   if(animate&&IsVisible){
    openingFrames=2;
    CompositionTarget.Rendering+=PlayOpening;
   }else FocusSearch();
  }
  void PlayOpening(object sender,EventArgs args){
   // Give the layered window a frame in its starting state before advancing the animation.
   if(--openingFrames>0)return;
   CompositionTarget.Rendering-=PlayOpening;
   UncloakOpening();
   var duration=TimeSpan.FromMilliseconds(320);
   var easing=new QuadraticEase{EasingMode=EasingMode.EaseOut};
   var scale=new DoubleAnimation(InitialScale,1,duration){EasingFunction=easing};
   openingScale.BeginAnimation(ScaleTransform.ScaleXProperty,scale);
   openingScale.BeginAnimation(ScaleTransform.ScaleYProperty,scale);
   openingOffset.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(InitialOffset,0,duration){EasingFunction=easing});
   Surface.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(270)){EasingFunction=easing});
   FocusSearch();
  }
  void FocusSearch(){
   Activate();
   Dispatcher.BeginInvoke(new Action(delegate{if(IsVisible)search.Focus();}),System.Windows.Threading.DispatcherPriority.Input);
  }
  static bool SetCloak(IntPtr handle,bool cloak){
   const int cloakAttribute=13;
   int value=cloak?1:0;
   return Native.DwmSetWindowAttribute(handle,cloakAttribute,ref value,sizeof(int))==0;
  }
  void UncloakOpening(){
   if(cloakedWindow==IntPtr.Zero)return;
   SetCloak(cloakedWindow,false);
   cloakedWindow=IntPtr.Zero;
  }
  void ResetOpening(){
   CompositionTarget.Rendering-=PlayOpening;
   UncloakOpening();
   openingScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);
   openingScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
   openingOffset.BeginAnimation(TranslateTransform.YProperty,null);
   Surface.BeginAnimation(OpacityProperty,null);
   openingScale.ScaleX=openingScale.ScaleY=1;
   openingOffset.Y=0;
   Surface.Opacity=1;
  }
  public void Dismiss(){Hide();host.ReturnFocus();}
  void Back(){if(folders.Count>0)folders.Pop();page=0;category="全部";search.Clear();Refresh();}
  void Open(AppEntry item){try{if(item.IsFolder){folders.Push(Directory.Exists(item.Source)?item.Source:item.Target);page=0;category="全部";search.Clear();Refresh();return;}if(host.Settings.CloseAfterLaunch)Hide();LauncherService.Launch(item);}catch(Exception e){Store.Error("launch",e);host.Notify("无法打开“"+item.Name+"”："+e.Message);}}
  public void Draw(){if(grid==null)return;grid.Columns=host.Settings.Columns;grid.Rows=host.Settings.Rows;int size=grid.Columns*grid.Rows;string query=search.Text.Trim();var matches=items.Where(a=>(category=="全部"||a.Category==category)&&Pinyin.Matches(a.Name,query)).ToList();int pages=Math.Max(1,(matches.Count+size-1)/size);page=Math.Max(0,Math.Min(page,pages-1));var pageItems=matches.Skip(page*size).Take(size).ToList();count.Text=matches.Count+" 个项目";footer.Text=matches.Count+" 个项目";heading.Text=folders.Count==0?"其他应用":Path.GetFileName(folders.Peek());back.Visibility=folders.Count==0?Visibility.Collapsed:Visibility.Visible;pageLabel.Text=(page+1)+" / "+pages;previous.IsEnabled=page>0;next.IsEnabled=page<pages-1;
   foreach(var pair in filters)pair.Value.Background=pair.Key==category?SelectedFilter:Brushes.Transparent;
   if(renderedColumns==grid.Columns&&renderedWidth==Width&&shown.SequenceEqual(pageItems)){UpdateSelectionVisual();return;}
   renderedColumns=grid.Columns;renderedWidth=Width;shown=pageItems;selectedIndex=0;
   grid.Children.Clear();tiles.Clear();foreach(var item in shown){var btn=AppButton(item);tiles.Add(btn);grid.Children.Add(btn);}if(shown.Count==0){var empty=Ui.Label("没有找到匹配的项目",14,Ui.LightMuted);empty.HorizontalAlignment=HorizontalAlignment.Center;grid.Children.Add(empty);}
   UpdateSelectionVisual();
  }
  void MoveSelection(int delta){
   if(tiles.Count==0)return;
   selectedIndex=Math.Max(0,Math.Min(tiles.Count-1,selectedIndex+delta));
   UpdateSelectionVisual();
  }
  void UpdateSelectionVisual(){
   for(int i=0;i<tiles.Count;i++){
    tiles[i].Background=i==selectedIndex&&tiles.Count>0?SelectedFilter:Brushes.Transparent;
   }
  }
  Button AppButton(AppEntry item){var content=new Grid{Width=Math.Max(66,Math.Min(110,(Width-72)/host.Settings.Columns-8)),Height=96};content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(57)});content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});var image=Ui.Image(host.Icons.Get(item.Source),51);content.Children.Add(image);var label=Ui.Label(item.Name,12,Ui.LightInk);label.TextAlignment=TextAlignment.Center;label.TextWrapping=TextWrapping.Wrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxHeight=34;label.VerticalAlignment=VerticalAlignment.Top;label.Margin=new Thickness(1,5,1,0);Grid.SetRow(label,1);content.Children.Add(label);var button=Ui.Button(content,delegate{Open(item);});button.Padding=new Thickness(2);button.Margin=new Thickness(2,1,2,1);button.ToolTip=item.Name;
   button.Tag=item;button.ContextMenu=itemMenu;
   button.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){dragStart=e.GetPosition(this);dragItem=item;};button.MouseMove+=delegate(object sender,MouseEventArgs e){if(e.LeftButton==MouseButtonState.Pressed&&dragItem==item&&(e.GetPosition(this)-dragStart).Length>9){DragDrop.DoDragDrop(button,new DataObject(typeof(AppEntry),item),DragDropEffects.Move);dragItem=null;}};
   button.AllowDrop=true;button.Drop+=delegate(object sender,DragEventArgs e){var source=e.Data.GetData(typeof(AppEntry)) as AppEntry;if(source==null||source.Id==item.Id)return;var order=items.Select(a=>a.Id).ToList();order.Remove(source.Id);order.Insert(Math.Max(0,order.IndexOf(item.Id)),source.Id);host.Settings.ItemOrder=order;Store.Save(host.Settings);Refresh();e.Handled=true;};return button;
  }
  ContextMenu CreateItemMenu(){
   var menu=new ContextMenu();
   menu.SetBinding(DataContextProperty,new Binding("PlacementTarget.Tag"){RelativeSource=RelativeSource.Self});
   AddItemAction(menu.Items,"固定到 Dock",host.Pin);
   var move=new MenuItem{Header="归入分类"};
   foreach(string target in new[]{"工具","学习","娱乐","文件"}){
    AddItemAction(move.Items,target,delegate(AppEntry item){host.Settings.Categories[item.Source]=target;Store.Save(host.Settings);Refresh();});
   }
   menu.Items.Add(move);
   AddItemAction(menu.Items,"查看原项目的位置",delegate(AppEntry item){Hide();System.Diagnostics.Process.Start("explorer.exe","/select,\""+item.Source+"\"");});
   return menu;
  }
  static void AddItemAction(ItemCollection items,string header,Action<AppEntry> action){
   var option=new MenuItem{Header=header};
   option.Click+=delegate{var item=option.DataContext as AppEntry;if(item!=null)action(item);};
   items.Add(option);
  }
 }
}
