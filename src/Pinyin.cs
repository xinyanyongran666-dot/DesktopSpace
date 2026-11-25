using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopSpace {
 // Pinyin initials for Chinese characters (GB2312 level-1 + common app/game level-2 characters).
 internal static class Pinyin {
  static readonly int[] Areas = { 45217, 45253, 45761, 46318, 46826, 47010, 47297, 47614, 48119, 48119, 49062, 49324, 49896, 50371, 50614, 50622, 50906, 51387, 51446, 52218, 52698, 52698, 52698, 52980, 53689, 54481, 55290 };
  static readonly Dictionary<char, char> Extras = new Dictionary<char, char> {
   { '哔', 'b' }, { '哩', 'l' }, { '穹', 'q' }, { '崩', 'b' }, { '铁', 't' }, { '绝', 'j' },
   { '鸣', 'm' }, { '潮', 'c' }, { '舟', 'z' }, { '讯', 'x' }, { '腾', 't' }, { '钉', 'd' },
   { '飞', 'f' }, { '夸', 'k' }, { '克', 'k' }, { '剪', 'j' }, { '映', 'y' }, { '葵', 'k' },
   { '鲁', 'l' }, { '喵', 'm' }, { '鲨', 's' }, { '鱼', 'y' }, { '虎', 'h' }, { '斗', 'd' }
  };
  static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
  static Encoding gb;

  internal static string Initials(string text) {
   if (String.IsNullOrEmpty(text)) return "";
   string cached;
   lock (cache) { if (cache.TryGetValue(text, out cached)) return cached; }
   var sb = new StringBuilder(text.Length);
   foreach (char ch in text) {
    if (ch < 128) { if (Char.IsLetterOrDigit(ch)) sb.Append(Char.ToLowerInvariant(ch)); continue; }
    char letter = Initial(ch);
    if (letter != '\0') sb.Append(letter);
   }
   string result = sb.ToString();
   lock (cache) { if (cache.Count > 2048) cache.Clear(); cache[text] = result; }
   return result;
  }

  static char Initial(char ch) {
   char extra;
   if (Extras.TryGetValue(ch, out extra)) return extra;
   if (ch < 0x4E00 || ch > 0x9FA5) return '\0';
   try {
    if (gb == null) gb = Encoding.GetEncoding(936);
    byte[] bytes = gb.GetBytes(ch.ToString());
    if (bytes.Length < 2) return '\0';
    int code = bytes[0] * 256 + bytes[1];
    for (int i = 0; i < 26; i++) {
     if (code >= Areas[i] && code < Areas[i + 1]) return (char)('a' + i);
    }
   } catch { }
   return '\0';
  }

  internal static bool Matches(string name, string query) {
   if (String.IsNullOrEmpty(query)) return true;
   if (name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
   foreach (char c in query) if (c >= 128) return false;
   return Initials(name).IndexOf(query.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) >= 0;
  }
 }
}
