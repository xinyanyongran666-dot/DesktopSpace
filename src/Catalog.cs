using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopSpace {
 // Keep the most recently used entries so browsing large folders cannot grow these caches indefinitely.
 sealed class RecentCache<T> {
  readonly int capacity;
  readonly LinkedList<KeyValuePair<string,T>> entries=new LinkedList<KeyValuePair<string,T>>();
  readonly Dictionary<string,LinkedListNode<KeyValuePair<string,T>>> lookup=
   new Dictionary<string,LinkedListNode<KeyValuePair<string,T>>>(StringComparer.OrdinalIgnoreCase);
  internal RecentCache(int capacity){this.capacity=capacity;}
  internal bool TryGetValue(string key,out T value){
   LinkedListNode<KeyValuePair<string,T>> node;
   if(!lookup.TryGetValue(key,out node)){value=default(T);return false;}
   entries.Remove(node);
   entries.AddFirst(node);
   value=node.Value.Value;
   return true;
  }
  internal void Add(string key,T value){
   if(lookup.Count==capacity){
    lookup.Remove(entries.Last.Value.Key);
    entries.RemoveLast();
   }
   lookup.Add(key,entries.AddFirst(new KeyValuePair<string,T>(key,value)));
  }
  internal void Clear(){lookup.Clear();entries.Clear();}
 }
 public class LinkInfo {public string Target="",IconFile="",AppUserModelId="";public int IconIndex;}
 public sealed class IconService {
  readonly RecentCache<ImageSource> cache=new RecentCache<ImageSource>(96);
  readonly RecentCache<LinkInfo> links=new RecentCache<LinkInfo>(256);
  object shell;
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct INFO{public IntPtr Icon;public int Index;public uint Attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string Type;}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SHGetFileInfo(string path,uint attributes,out INFO info,uint size,uint flags);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint PrivateExtractIcons(string file,int index,int width,int height,IntPtr[] icons,uint[] ids,uint count,uint flags);
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern uint ExtractIconEx(string file,int index,out IntPtr large,out IntPtr small,uint count);
  public LinkInfo Resolve(string path){LinkInfo found;if(links.TryGetValue(path,out found))return found;found=new LinkInfo{Target=path};
   try{string ext=Path.GetExtension(path);if(ext.Equals(".lnk",StringComparison.OrdinalIgnoreCase)){
    if(shell==null)shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
    object shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});
    try{
     Type shortcutType=shortcut.GetType();
     found.Target=(string)shortcutType.InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null);
     if(String.Equals(Path.GetFileName(found.Target),"explorer.exe",StringComparison.OrdinalIgnoreCase)){
      const string appsFolder=@"shell:AppsFolder\";
      string args=((string)shortcutType.InvokeMember("Arguments",BindingFlags.GetProperty,null,shortcut,null)??"").Trim().Trim('"');
      if(args.StartsWith(appsFolder,StringComparison.OrdinalIgnoreCase))found.AppUserModelId=args.Substring(appsFolder.Length);
     }
     string icon=(string)shortcutType.InvokeMember("IconLocation",BindingFlags.GetProperty,null,shortcut,null);
     int comma=icon.LastIndexOf(',');if(comma>=0){found.IconFile=Environment.ExpandEnvironmentVariables(icon.Substring(0,comma).Trim('"'));int.TryParse(icon.Substring(comma+1),out found.IconIndex);}
    }finally{Marshal.FinalReleaseComObject(shortcut);}
   }else if(ext.Equals(".url",StringComparison.OrdinalIgnoreCase)){foreach(string line in File.ReadAllLines(path)){if(line.StartsWith("IconFile=",StringComparison.OrdinalIgnoreCase))found.IconFile=Environment.ExpandEnvironmentVariables(line.Substring(9).Trim('"'));if(line.StartsWith("IconIndex=",StringComparison.OrdinalIgnoreCase))int.TryParse(line.Substring(10),out found.IconIndex);}}
   }catch{ }
   links.Add(path,found);return found;
  }
  public static ImageSource FromHandle(IntPtr icon){if(icon==IntPtr.Zero)return null;try{var result=Imaging.CreateBitmapSourceFromHIcon(icon,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());result.Freeze();return result;}catch{return null;}}
  public ImageSource Get(string path){ImageSource image;if(cache.TryGetValue(path,out image))return image;var info=Resolve(path);string source=File.Exists(info.IconFile)?info.IconFile:File.Exists(info.Target)?info.Target:path;
   string ext=Path.GetExtension(source).ToLowerInvariant();
   if(ext==".png"||ext==".jpg"||ext==".jpeg"||ext==".bmp"||ext==".webp"){
    try{
     BitmapImage bitmap;
     using(var stream=File.OpenRead(source)){
      bitmap=new BitmapImage();
      bitmap.BeginInit();
      bitmap.StreamSource=stream;
      bitmap.CacheOption=BitmapCacheOption.OnLoad;
      bitmap.DecodePixelWidth=96;
      bitmap.EndInit();
      bitmap.Freeze();
     }
     cache.Add(path,bitmap);
     return bitmap;
    }catch{ }
   }
   IntPtr[] icons={IntPtr.Zero};try{PrivateExtractIcons(source,info.IconIndex,96,96,icons,new uint[1],1,0);}catch{ }
   if(icons[0]!=IntPtr.Zero){try{image=FromHandle(icons[0]);}finally{Native.DestroyIcon(icons[0]);}}
   else {IntPtr large,small;ExtractIconEx(source,info.IconIndex,out large,out small,1);try{image=FromHandle(large);}finally{if(large!=IntPtr.Zero)Native.DestroyIcon(large);if(small!=IntPtr.Zero)Native.DestroyIcon(small);}}
   if(image==null){INFO shellInfo;SHGetFileInfo(path,0,out shellInfo,(uint)Marshal.SizeOf(typeof(INFO)),0x100);try{image=FromHandle(shellInfo.Icon);}finally{if(shellInfo.Icon!=IntPtr.Zero)Native.DestroyIcon(shellInfo.Icon);}}
   if(image==null)image=Ui.SymbolImage("▦");cache.Add(path,image);return image;
  }
  public ImageSource TaskIcon(TaskEntry task){UIntPtr icon;Native.SendMessageTimeout(task.Handle,0x7f,(UIntPtr)1,IntPtr.Zero,2,30,out icon);ImageSource result=FromHandle(new IntPtr(unchecked((long)icon.ToUInt64())));if(result==null&&File.Exists(task.Executable))result=Get(task.Executable);return result??Ui.SymbolImage("▣");}
  public void Clear(){cache.Clear();links.Clear();}
 }
 public sealed class CatalogService:IDisposable {
  public readonly ShellSettings Settings;public readonly IconService Icons;
  readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
  public event EventHandler Changed;
  public CatalogService(ShellSettings settings,IconService icons){Settings=settings;Icons=icons;foreach(string source in Roots()){if(!Directory.Exists(source))continue;var w=new FileSystemWatcher(source){NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite,IncludeSubdirectories=false};FileSystemEventHandler change=delegate{OnChanged();};w.Created+=change;w.Deleted+=change;w.Changed+=change;w.Renamed+=delegate{OnChanged();};w.Error+=delegate{OnChanged();};w.EnableRaisingEvents=true;watchers.Add(w);}}
  void OnChanged(){var changed=Changed;if(changed!=null)changed(this,EventArgs.Empty);}
  public static string[] Roots(){return new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)};}
  public AppEntry Entry(string path,bool includeIcon=true){var link=Icons.Resolve(path);string name=Path.GetFileName(path);string ext=Path.GetExtension(name);if(ext.Equals(".lnk",StringComparison.OrdinalIgnoreCase)||ext.Equals(".url",StringComparison.OrdinalIgnoreCase))name=Path.GetFileNameWithoutExtension(name);bool folder=Directory.Exists(path)||(!String.IsNullOrEmpty(link.Target)&&Directory.Exists(link.Target));string category=folder?"文件":(ext.Equals(".lnk",StringComparison.OrdinalIgnoreCase)||ext.Equals(".url",StringComparison.OrdinalIgnoreCase))?"工具":"文件";
   if(Regex.IsMatch(name,"Office|Word|Excel|PowerPoint|WPS|OneNote|Notion|Obsidian|Anki|Keil|SOLIDWORKS|MATLAB|LibreOffice|学习",RegexOptions.IgnoreCase))category="学习";
   else if(Regex.IsMatch(name,"Steam|Epic|Ubisoft|WeGame|GOG|Spotify|Music|音乐|网易云|哔哩|Bilibili|YouTube|Video|Player|游戏|崩坏|原神|米哈游|鸣潮",RegexOptions.IgnoreCase))category="娱乐";
   if(Settings.Categories.ContainsKey(path))category=Settings.Categories[path];
   return new AppEntry{Id=path.ToLowerInvariant(),Name=name,Source=path,Target=link.Target,AppUserModelId=link.AppUserModelId,IsFolder=folder,Category=category,Icon=includeIcon?Icons.Get(path):null};
  }
  public List<AppEntry> Desktop(bool includeIcons=true){var excluded=new HashSet<string>(Settings.Pinned,StringComparer.OrdinalIgnoreCase);var names=new HashSet<string>(Settings.ExcludedNames,StringComparer.OrdinalIgnoreCase);foreach(string p in Settings.Pinned)names.Add(Path.GetFileName(p));var result=new List<AppEntry>();
   foreach(string root in Roots()){if(!Directory.Exists(root))continue;foreach(string path in Directory.GetFileSystemEntries(root)){try{FileAttributes attr=File.GetAttributes(path);if((attr&(FileAttributes.Hidden|FileAttributes.System))!=0||excluded.Contains(path)||names.Contains(Path.GetFileName(path)))continue;result.Add(Entry(path,includeIcons));}catch(Exception e){Store.Error("catalog",e);}}}
   var order=Settings.ItemOrder;return result.OrderBy(a=>order.Contains(a.Id)?order.IndexOf(a.Id):Int32.MaxValue).ThenBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
  }
  public List<AppEntry> Folder(string path){var result=new List<AppEntry>();foreach(string p in Directory.GetFileSystemEntries(path)){try{if((File.GetAttributes(p)&(FileAttributes.Hidden|FileAttributes.System))==0)result.Add(Entry(p,false));}catch{ }}return result.OrderByDescending(a=>a.IsFolder).ThenBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToList();}
  public void Dispose(){foreach(var w in watchers)w.Dispose();}
 }
}
