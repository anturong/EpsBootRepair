using System;

namespace EpsBootRepair
{
    /// <summary>config.ini 的只读视图。读不到就用默认值，不抛异常。</summary>
    public sealed class AppConfig
    {
        public string Language = "zh-CN";
        public bool PreferBundled;
        public int CodePage = 936;
        public string LogDir = "";
        public string PreferredLetter = "";

        public static AppConfig Load(string path)
        {
            var c = new AppConfig();
            var m = Ini.Read(path);
            string v;

            if (m.TryGetValue("General.Language", out v) && v.Length > 0) c.Language = v;

            if (m.TryGetValue("Tools.PreferBundled", out v)) c.PreferBundled = v == "1";

            if (m.TryGetValue("Log.CodePage", out v))
            {
                int cp;
                if (int.TryParse(v, out cp) && cp > 0) c.CodePage = cp;
            }

            if (m.TryGetValue("Log.Dir", out v) && v.Trim().Length > 0) c.LogDir = v.Trim();

            if (m.TryGetValue("Esp.PreferredLetter", out v))
            {
                v = v.TrimEnd(':').Trim();
                if (v.Length > 0) c.PreferredLetter = v;
            }
            return c;
        }
    }
}
