using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EpsBootRepair
{
    /// <summary>
    /// 重建 ESP 里的引导文件。BCD 和 EFI 文件一律不自己实现，全交给系统原生命令：
    ///
    ///     bcdboot &lt;系统分区&gt;\Windows /s &lt;ESP&gt; /f UEFI
    ///
    /// 这一条就把 ESP 该有的东西全铺回去：\EFI\Boot\bootx64.efi、
    /// \EFI\Microsoft\Boot\bootmgfw.efi、bootmgr.efi、字体和 BCD。
    /// ESP 被整个清空过也一样 —— 缺什么它补什么。
    /// </summary>
    public sealed class Repair
    {
        readonly Runner _run;
        readonly ToolSet _tools;
        readonly AppConfig _cfg;
        readonly string _baseDir;
        readonly Action<string, LogKind> _log;
        readonly Func<VolumeInfo> _sys;
        readonly Func<VolumeInfo> _esp;

        public Repair(Runner run, ToolSet tools, AppConfig cfg, string baseDir,
                      Action<string, LogKind> log, Func<VolumeInfo> sys, Func<VolumeInfo> esp)
        {
            _run = run;
            _tools = tools;
            _cfg = cfg;
            _baseDir = baseDir;
            _log = log;
            _sys = sys;
            _esp = esp;
        }

        // ------------------------------------------------------------------ 入口

        public bool DoRepair()
        {
            VolumeInfo sys = _sys();
            if (sys == null)
            {
                _log(Lang.T("Error.NoSystemPartition"), LogKind.Error);
                return false;
            }

            VolumeInfo esp = _esp();
            if (esp == null)
            {
                _log(Lang.T("Error.NoEspOnDisk", sys.DiskNumber), LogKind.Error);
                return false;
            }

            _log(Lang.T("Log.EspFound", esp.HasLetter ? esp.DriveLetter + ":" : Fmt.Size(esp.Size),
                        esp.DiskNumber, esp.PartitionNumber), LogKind.Info);

            string sysArg = EnsureLetter(sys, false);
            if (sysArg == null) return false;
            string espArg = EnsureLetter(esp, true);
            if (espArg == null) return false;

            string bcdboot = Exe("bcdboot.exe");
            if (bcdboot == null) return false;

            WarnNoSource(sys);

            _log(Lang.T("Log.Running", "bcdboot /f UEFI"), LogKind.Command);
            int rc = _run.Run(bcdboot, "\"" + sysArg + "\\Windows\" /s " + espArg + " /f UEFI",
                              _baseDir, null, 180000);
            if (rc != 0)
            {
                _log(Lang.T("Error.CommandFailed", rc, "bcdboot"), LogKind.Error);
                NoteBcdbootFailure(rc);
            }

            _log(Lang.T("Log.Finished", rc == 0 ? 0 : 1), rc == 0 ? LogKind.Ok : LogKind.Error);
            return rc == 0;
        }

        // ------------------------------------------------------------------ 诊断

        const int ErrorBadExeFormat = 193;              // 0xC1
        const int CryptEFileError = -2146885629;        // 0x80092003

        /// <summary>
        /// bcdboot 的源就是"系统分区"里的 Windows\Boot\*。选了个没有 Windows 的分区
        /// （比如刚快速分区出来、名字却叫"系统"的空卷），它找不到源，会以一个光秃秃的
        /// Win32 错误码退出、一个字都不打印。这里在跑之前先点破，免得又成悬案。
        /// </summary>
        void WarnNoSource(VolumeInfo sys)
        {
            if (sys.HasWindows) return;
            _log(Lang.T("Log.SourceNoWindows", sys.Root), LogKind.Warn);
        }

        void NoteBcdbootFailure(int rc)
        {
            if (rc == ErrorBadExeFormat || rc == CryptEFileError)
                _log(Lang.T("Log.BcdbootNoSource", rc), LogKind.Warn);
        }

        // ------------------------------------------------------------------ 内部

        string Exe(string name)
        {
            string p = _tools.Get(name);
            if (p == null) _log(Lang.T("Error.ToolMissing", name), LogKind.Error);
            return p;
        }

        /// <summary>
        /// 没盘符就分一个（PE 里系统盘、ESP 经常都没盘符）。
        /// 先试进程内挂载点，不行才回退 diskpart —— diskpart 依赖 VDS 服务，
        /// PE 里这份常是坏的（USM 那份直接 0x80070057 退出，零输出）。
        /// </summary>
        string EnsureLetter(VolumeInfo v, bool isEsp)
        {
            if (v.HasLetter) return v.RootArg;

            char c = Volumes.PickFreeLetter(_cfg.PreferredLetter);
            if (c == '\0')
            {
                _log(Lang.T("Error.NoFreeLetter"), LogKind.Error);
                return null;
            }

            int mountErr = Volumes.AssignLetter(v, c);
            if (mountErr != 0)
            {
                _log(Lang.T("Log.LetterMountFailed", mountErr), LogKind.Warn);
                if (!DiskpartLetter(v, c)) return null;
            }

            v.DriveLetter = c;
            _log(Lang.T(isEsp ? "Log.EspAssigned" : "Log.LetterAssigned", c + ":"), LogKind.Ok);
            return c + ":";
        }

        /// <summary>回退路径：让 diskpart 去 assign letter。</summary>
        bool DiskpartLetter(VolumeInfo v, char c)
        {
            string diskpart = Exe("diskpart.exe");
            if (diskpart == null) return false;
            if (v.DiskNumber < 0 || v.PartitionNumber <= 0)
            {
                _log(Lang.T("Error.BadPartition", v.VolumePath), LogKind.Error);
                return false;
            }

            string script = Path.Combine(Path.GetTempPath(), "eps_assign.txt");
            try
            {
                File.WriteAllText(script,
                    "select disk " + v.DiskNumber + "\r\n" +
                    "select partition " + v.PartitionNumber + "\r\n" +
                    "assign letter=" + c + "\r\n" +
                    "exit\r\n", Encoding.ASCII);
            }
            catch (Exception ex)
            {
                _log(ex.Message, LogKind.Error);
                return false;
            }

            int rc;
            try { rc = _run.Run(diskpart, "/s \"" + script + "\"", _baseDir, null, 120000); }
            finally { try { File.Delete(script); } catch { } }

            if (rc != 0)
            {
                _log(Lang.T("Error.CommandFailed", rc, "diskpart"), LogKind.Error);
                return false;
            }
            return true;
        }
    }
}
