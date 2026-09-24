using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace EpsBootRepair
{
    /// <summary>这次运行实际要用的那几个 exe，名字 → 完整路径。</summary>
    public sealed class ToolSet
    {
        readonly Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void Set(string name, string path) { _paths[name] = path; }

        /// <summary>没找到返回 null。</summary>
        public string Get(string name)
        {
            string p;
            return _paths.TryGetValue(name, out p) ? p : null;
        }
    }

    /// <summary>
    /// 先探 PE 自带的（%SystemRoot%\System32），在就用系统的（版本最配当前 PE）；
    /// 不在才退回项目内置的 tools\&lt;arch&gt;\。每个都往日志里打一行来源，便于回溯。
    /// </summary>
    public static class Tools
    {
        public static readonly string[] Names =
        {
            "bcdboot.exe", "bootsect.exe", "diskpart.exe"
        };

        /// <summary>按 PE 自己的架构选目录：32 位 PE 跑不了 x64 exe。</summary>
        public static string ArchDir()
        {
            return RuntimeInformation.OSArchitecture == Architecture.X86 ? "x86" : "x64";
        }

        public static ToolSet Resolve(string baseDir, bool preferBundled, Action<string, LogKind> log)
        {
            var set = new ToolSet();
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string bundled = Path.Combine(baseDir, "tools", ArchDir());

            bool useSystem = !preferBundled && File.Exists(Path.Combine(sys, "bcdboot.exe"));

            foreach (string name in Names)
            {
                string sysPath = Path.Combine(sys, name);
                string bunPath = Path.Combine(bundled, name);
                string pick = null;
                bool isBundled = false;

                if (useSystem && File.Exists(sysPath))
                {
                    pick = sysPath;
                }
                else if (File.Exists(bunPath))
                {
                    pick = bunPath;
                    isBundled = true;
                }
                else if (File.Exists(sysPath))
                {
                    pick = sysPath;
                }

                if (pick == null) continue;
                set.Set(name, pick);
                log(Lang.T("Log.ToolSource", pick, Lang.T(isBundled ? "Log.ToolSourceBundled" : "Log.ToolSourceSystem")),
                    LogKind.Tool);
            }
            return set;
        }
    }
}
