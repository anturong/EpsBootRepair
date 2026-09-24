using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace EpsBootRepair
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern uint SetErrorMode(uint mode);

        [STAThread]
        static void Main()
        {
            // SEM_FAILCRITICALERRORS(1) | SEM_NOOPENFILEERRORBOX(2)，子进程继承。
            // 精简 PE 里挑中的 exe 一旦缺个导入库，弹框的是加载器而不是程序自己，
            // 那个模态框会挡住界面、语言还跟着系统走。父进程这边唯一能关掉它的办法
            // 就是设错误模式。
            SetErrorMode(1 | 2);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            AppConfig cfg = AppConfig.Load(Path.Combine(baseDir, "config.ini"));

            // 界面文字必须在建任何窗口之前加载好
            Lang.Load(Path.Combine(baseDir, "lang"), cfg.Language);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!IsElevated())
            {
                // PE 里本来就是 SYSTEM/Administrator，走不到这里；
                // 普通 Windows 上试着弹一次 UAC，失败才报错退出
                if (RelaunchElevated()) return;

                MessageBox.Show(Lang.T("Error.NoAdmin"), Lang.T("App.Title"),
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.Run(new MainForm(cfg));
        }

        static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    if (new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)) return true;
                    // SYSTEM 不一定是 Administrators 组成员，单独放行
                    if (id.User != null && id.User.IsWellKnown(WellKnownSidType.LocalSystemSid)) return true;
                }
            }
            catch { }
            return false;
        }

        static bool RelaunchElevated()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                Process p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                if (p == null) return false;
                p.WaitForExit();
                return true;
            }
            catch { return false; }
        }
    }
}
