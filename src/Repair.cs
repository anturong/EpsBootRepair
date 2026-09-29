using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EpsBootRepair
{
    /// <summary>
    /// 重建引导文件。BCD 和引导文件一律不自己实现，全交给系统原生命令；
    /// 走哪条路由**目标磁盘的分区表**决定，与当前 PE 是 UEFI 还是 BIOS 起的无关：
    ///
    ///     GPT/UEFI   bcdboot &lt;系统分区&gt;\Windows /s &lt;ESP&gt; /f UEFI
    ///     MBR/Legacy bcdboot &lt;系统分区&gt;\Windows /s &lt;活动主分区&gt; /f BIOS
    ///                bootsect /nt60 &lt;活动主分区&gt; /mbr
    ///
    /// GPT 那条把 ESP 该有的东西全铺回去：\EFI\Boot\bootx64.efi、
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
        readonly Func<VolumeInfo> _target;

        public Repair(Runner run, ToolSet tools, AppConfig cfg, string baseDir,
                      Action<string, LogKind> log, Func<VolumeInfo> sys, Func<VolumeInfo> target)
        {
            _run = run;
            _tools = tools;
            _cfg = cfg;
            _baseDir = baseDir;
            _log = log;
            _sys = sys;
            _target = target;
        }

        // ------------------------------------------------------------------ 入口

        /// <summary>
        /// 该不该写 BIOS 引导代码，只看**目标那块盘的分区表**：GPT 走 UEFI/ESP 那条，
        /// MBR 走 Legacy/活动主分区那条。跟"当前这个 PE 是 UEFI 还是 BIOS 起的"无关 ——
        /// 用 UEFI 的 PE 去修一块死掉的 Legacy 盘是常规操作。
        /// </summary>
        public bool DoRepair()
        {
            VolumeInfo sys = _sys();
            if (sys == null)
            {
                _log(Lang.T("Error.NoSystemPartition"), LogKind.Error);
                return false;
            }

            VolumeInfo target = _target();
            if (target == null)
            {
                // 目标都没定下来，只能按源那块盘的分区表选一句措辞
                bool sysLegacy = sys.Style == DiskStyle.Mbr;
                _log(Lang.T(sysLegacy ? "Error.NoBootPartition" : "Error.NoEspOnDisk", sys.DiskNumber),
                     LogKind.Error);
                return false;
            }

            return IsLegacyTarget(target) ? DoRepairLegacy(sys, target) : DoRepairUefi(sys, target);
        }

        /// <summary>
        /// 走 BIOS 那条路，当且仅当目标是**非 ESP 的 MBR 分区**。源和目标是两块盘上也一样，
        /// 判据只看目标 —— UEFI 要的是 ESP，跟它挂在哪块盘无关。
        ///
        /// 不能只判 <c>target.Style == Mbr</c>：EspFor 的跨盘兜底不筛分区表类型，一个 MBR 分区
        /// 只要类型是 0xEF、或含 EFI\Microsoft\Boot，<c>IsEsp()</c> 就为真（见 VolumeInfo.IsEsp）。
        /// 那种目标必须走 UEFI，否则会把一条本来正确的修复切成 /f BIOS + bootsect。
        ///
        /// 反方向不存在歧义：GPT 的保护 MBR 里写不进引导代码，所以 GPT 上非 ESP 的分区
        /// 不能当 BIOS 目标，高级窗口的候选里也不会出现它。
        /// </summary>
        public static bool IsLegacyTarget(VolumeInfo target)
        {
            return target.Style == DiskStyle.Mbr && !target.IsEsp();
        }

        // ------------------------------------------------------------------ 命令行

        // 命令行只在这里拼一次：真正执行的和高级窗口预览的必须是同一个串。
        // sysArg / targetArg 形如 "V:"（带冒号），高级窗口在盘符还没分配时会用占位符顶替。

        public static string UefiArgs(string sysArg, string targetArg)
        {
            return "\"" + sysArg + "\\Windows\" /s " + targetArg + " /f UEFI";
        }

        public static string BcdBiosArgs(string sysArg, string targetArg)
        {
            return "\"" + sysArg + "\\Windows\" /s " + targetArg + " /f BIOS";
        }

        public static string BootsectArgs(string targetArg)
        {
            return "/nt60 " + targetArg + " /mbr";
        }

        /// <summary>UEFI/GPT：重铺 ESP。见类注释。</summary>
        bool DoRepairUefi(VolumeInfo sys, VolumeInfo esp)
        {
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
            int rc = _run.Run(bcdboot, UefiArgs(sysArg, espArg), _baseDir, null, 180000);
            if (rc != 0)
            {
                _log(Lang.T("Error.CommandFailed", rc, "bcdboot"), LogKind.Error);
                NoteBcdbootFailure(rc);
            }

            _log(Lang.T("Log.Finished", rc == 0 ? 0 : 1), rc == 0 ? LogKind.Ok : LogKind.Error);
            return rc == 0;
        }

        /// <summary>
        /// Legacy/MBR：没有 ESP，引导文件住在该磁盘的**活动主分区**上（单分区安装时就是
        /// Windows 分区自己）。两条命令，都不依赖任何服务（bootsect 是个纯 PE，bcdboot 也是）：
        ///
        ///     bcdboot &lt;系统分区&gt;\Windows /s &lt;引导分区&gt; /f BIOS   铺 \bootmgr、\Boot\BCD、字体
        ///     bootsect /nt60 &lt;引导分区&gt; /mbr                    引导代码写进它的 VBR 和磁盘的 MBR
        ///
        /// bcdboot 在前：它得先读目标分区的文件系统；bootsect 只写引导代码、不碰文件，放后面收尾。
        /// 活动标记本工具只读不写 —— 标错或没标属于另一种故障，不在本工具范围内。
        /// </summary>
        bool DoRepairLegacy(VolumeInfo sys, VolumeInfo boot)
        {
            _log(Lang.T("Log.BootPartFound", boot.HasLetter ? boot.DriveLetter + ":" : Fmt.Size(boot.Size),
                        boot.DiskNumber, boot.PartitionNumber), LogKind.Info);

            string sysArg = EnsureLetter(sys, false);
            if (sysArg == null) return false;
            string bootArg = EnsureLetter(boot, false);
            if (bootArg == null) return false;

            string bcdboot = Exe("bcdboot.exe");
            if (bcdboot == null) return false;
            string bootsect = Exe("bootsect.exe");
            if (bootsect == null) return false;

            WarnNoSource(sys);

            _log(Lang.T("Log.Running", "bcdboot /f BIOS"), LogKind.Command);
            int rc = _run.Run(bcdboot, BcdBiosArgs(sysArg, bootArg), _baseDir, null, 180000);
            if (rc != 0)
            {
                _log(Lang.T("Error.CommandFailed", rc, "bcdboot"), LogKind.Error);
                NoteBcdbootFailure(rc);
                // 引导文件都没铺成，再往 MBR 写引导代码没有意义（它要加载的 bootmgr 根本不在）
                _log(Lang.T("Log.Finished", 1), LogKind.Error);
                return false;
            }

            _log(Lang.T("Log.Running", "bootsect /nt60 /mbr"), LogKind.Command);
            int rc2 = _run.Run(bootsect, BootsectArgs(bootArg), _baseDir, null, 60000);
            if (rc2 != 0)
            {
                _log(Lang.T("Error.CommandFailed", rc2, "bootsect"), LogKind.Error);
                _log(Lang.T("Log.Finished", 1), LogKind.Error);
                return false;
            }

            _log(Lang.T("Log.Finished", 0), LogKind.Ok);
            return true;
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
