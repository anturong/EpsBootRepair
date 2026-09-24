using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EpsBootRepair
{
    /// <summary>
    /// 极简 ini 读取：段名 + 键名拼成 "Section.Key"，注释用 ; 或 #。
    /// 只读，不做写入、不做继承。
    /// </summary>
    public static class Ini
    {
        public static Dictionary<string, string> Read(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return map;

            string section = "";
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (key.Length == 0) continue;

                map[section + "." + key] = val;
            }
            return map;
        }
    }

    /// <summary>
    /// 界面文字全部走这里，代码里不允许出现中文字面量。
    /// 先加载当前语言，再用另一种语言补齐缺键，最后仍缺就返回 !!Key!! 让人一眼看见。
    /// </summary>
    public static class Lang
    {
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> Loaded = new List<string>();

        public static string Current { get; private set; } = "zh-CN";
        public static IList<string> LoadedLanguages => Loaded;

        public static void Load(string langDir, string language)
        {
            Map.Clear();
            Loaded.Clear();
            Current = language;

            Add(langDir, language);
            Add(langDir, language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN");
        }

        static void Add(string dir, string lang)
        {
            var m = Ini.Read(Path.Combine(dir, lang + ".ini"));
            if (m.Count == 0) return;

            Loaded.Add(lang);
            foreach (var kv in m)
                if (!Map.ContainsKey(kv.Key)) Map[kv.Key] = kv.Value;
        }

        /// <summary>取一条界面文字，缺键返回 !!Key!! 而不是静默回退。</summary>
        public static string T(string key)
        {
            return Map.TryGetValue(key, out string v) ? v : "!!" + key + "!!";
        }

        /// <summary>带占位符的取法，例：Lang.T("Log.Running", exePath)</summary>
        public static string T(string key, params object[] args)
        {
            string v = T(key);
            if (args == null || args.Length == 0) return v;
            try { return string.Format(v, args); }
            catch (FormatException) { return v; }
        }
    }
}
