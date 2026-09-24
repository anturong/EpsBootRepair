using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace EpsBootRepair
{
    /// <summary>
    /// 界面只有一件事：选 Windows 系统分区 → 点一键修复 → ESP 里的引导文件重建完。
    /// ESP 不用用户挑，从所选分区那块盘上自动认（一块盘正常只有一个 ESP）。
    /// </summary>
    public sealed class MainForm : Form
    {
        readonly AppConfig _cfg;
        readonly Encoding _enc;
        readonly Runner _run;
        readonly string _baseDir;
        readonly string _logDir;
        readonly string _logFile;

        ToolSet _tools;
        List<VolumeInfo> _vols = new List<VolumeInfo>();
        bool _busy;
        bool _filling;

        readonly RichTextBox _log = new RichTextBox();
        readonly ComboBox _cmb = new ComboBox();
        readonly Label _lblEsp = new Label();
        readonly Label _warn = new Label();
        readonly Label _status = new Label();
        readonly Label _version = new Label();

        Button _btnRefresh, _btnRepair, _btnOpenLog;

        public MainForm(AppConfig cfg)
        {
            _cfg = cfg;
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _logDir = ResolveLogDir(cfg.LogDir);
            _logFile = Path.Combine(_logDir, "eps-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            _enc = CodePageEncoding.Get(cfg.CodePage);
            _run = new Runner(Log, _enc);

            BuildUi();
            Load += OnLoad;
        }

        /// <summary>
        /// 日志目录。程序可能就躺在只读的 PE 光盘上，那儿写不进去，
        /// 那就退到 %TEMP%（PE 里是 X: 的 ramdisk，一定能写）。
        /// config.ini 里指定了 [Log] Dir 就先试那儿 —— PE 的 %TEMP% 是内存盘，
        /// 一重启日志就没了，想留证据得往真盘/U 盘上写。
        /// </summary>
        static string ResolveLogDir(string configured)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(configured)) candidates.Add(configured);
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"));
            candidates.Add(Path.Combine(Path.GetTempPath(), "eps-logs"));

            foreach (string d in candidates)
            {
                try
                {
                    Directory.CreateDirectory(d);
                    string probe = Path.Combine(d, "w.tmp");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    return d;
                }
                catch { }
            }
            return Path.GetTempPath();
        }

        // ------------------------------------------------------------------ 界面

        void BuildUi()
        {
            SuspendLayout();

            Text = Lang.T("App.Title");
            ClientSize = new Size(1000, 668);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            Font = Theme.Ui;
            DoubleBuffered = true;

            // ---------------- 左：选系统分区（顺带把认出来的 ESP 摆出来给用户核对）
            Panel env = Theme.Card(Lang.T("Main.EnvSection"), 10, 10, 590, 148);

            _cmb.Location = new Point(14, 46);
            _cmb.Size = new Size(562, 26);
            _cmb.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmb.FlatStyle = FlatStyle.Flat;
            _cmb.BackColor = Theme.Bg;
            _cmb.ForeColor = Theme.Fg;
            _cmb.Font = Theme.Ui;
            _cmb.SelectedIndexChanged += OnComboChanged;
            env.Controls.Add(_cmb);

            env.Controls.Add(Theme.Caption(Lang.T("Main.EspLabel"), 14, 84));

            _lblEsp.Text = "-";
            _lblEsp.Location = new Point(14, 102);
            _lblEsp.AutoSize = true;
            _lblEsp.ForeColor = Theme.Muted;
            _lblEsp.Font = Theme.Big;
            _lblEsp.BackColor = Color.Transparent;
            env.Controls.Add(_lblEsp);

            // ---------------- 左：操作
            Panel act = Theme.Card(Lang.T("Main.ActionLabel"), 10, 168, 590, 140);

            _btnRefresh = Theme.Btn(Lang.T("Button.Refresh"), 14, 44, 270, 34, Theme.BtnBg, Theme.Fg, OnRefresh);
            _btnOpenLog = Theme.Btn(Lang.T("Button.OpenLog"), 306, 44, 270, 34, Theme.BtnBg, Theme.Fg, OnOpenLog);
            _btnRepair = Theme.Btn(Lang.T("Button.Repair"), 14, 88, 562, 40, Theme.Green, Theme.Dark, OnRepair);

            act.Controls.Add(_btnRefresh);
            act.Controls.Add(_btnOpenLog);
            act.Controls.Add(_btnRepair);

            // ---------------- 左：警告
            Panel warnCard = Theme.Card(Lang.T("Main.WarningLabel"), 10, 318, 590, 300);
            _warn.Location = new Point(14, 42);
            _warn.Size = new Size(562, 246);
            _warn.AutoSize = false;
            _warn.ForeColor = Theme.Muted;
            _warn.Font = Theme.Ui;
            _warn.BackColor = Color.Transparent;
            warnCard.Controls.Add(_warn);

            // ---------------- 左下角状态
            _status.Location = new Point(14, 632);
            _status.AutoSize = true;
            _status.ForeColor = Theme.Muted;
            _status.Font = Theme.Ui;
            _status.BackColor = Color.Transparent;

            _version.Location = new Point(700, 632);
            _version.Size = new Size(286, 20);
            _version.TextAlign = ContentAlignment.MiddleRight;
            _version.Text = Lang.T("App.VersionLabel") + " " + Application.ProductVersion;
            _version.ForeColor = Theme.Muted;
            _version.Font = Theme.Ui;
            _version.BackColor = Color.Transparent;

            // ---------------- 右：日志
            Panel logCard = Theme.Card(Lang.T("Main.LogLabel"), 610, 10, 380, 618);
            _log.Location = new Point(14, 38);
            _log.Size = new Size(352, 566);
            _log.BackColor = Theme.LogBg;
            _log.ForeColor = Theme.Fg;
            _log.Font = Theme.Mono;
            _log.BorderStyle = BorderStyle.None;
            _log.ReadOnly = true;
            _log.WordWrap = true;
            _log.ScrollBars = RichTextBoxScrollBars.Vertical;
            _log.DetectUrls = false;
            logCard.Controls.Add(_log);

            Controls.Add(env);
            Controls.Add(act);
            Controls.Add(warnCard);
            Controls.Add(logCard);
            Controls.Add(_status);
            Controls.Add(_version);

            ResumeLayout(false);
            UpdateState();
        }

        void OnLoad(object sender, EventArgs e)
        {
            try { Directory.CreateDirectory(_logDir); } catch { }
            Log(Lang.T("Log.Start"), LogKind.Info);
            RefreshAll(true);
        }

        // ------------------------------------------------------------------ 环境

        void RefreshAll(bool first)
        {
            Log(Lang.T("Log.ScanningPartitions"), LogKind.Info);
            _vols = Volumes.Enumerate();
            FillCombo();

            if (first)
            {
                _tools = Tools.Resolve(_baseDir, _cfg.PreferBundled, Log);
                Log(Lang.T("Log.Arch", Tools.ArchDir()), LogKind.Info);
                if (_tools.Get("bcdboot.exe") == null)
                {
                    Log(Lang.T("Error.NoTools"), LogKind.Error);
                    MessageBox.Show(this, Lang.T("Error.NoTools"), Lang.T("App.Title"),
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            UpdateState();
        }

        void FillCombo()
        {
            string keep = null;
            VolumeInfo cur = Selected();
            if (cur != null) keep = cur.VolumePath;

            _filling = true;
            _cmb.Items.Clear();

            foreach (VolumeInfo v in _vols)
            {
                if (string.IsNullOrEmpty(v.FileSystem)) continue;      // MSR 之类没文件系统的不列

                // 日志里照样报出来（光驱 / 内存盘也有用），但只有真分区才进候选
                Log(Lang.T("Log.PartitionRow", v.DiskNumber, v.PartitionNumber,
                           v.HasLetter ? v.DriveLetter + ":" : "-", Volumes.StyleName(v.Style)), LogKind.Info);
                if (!v.CanBeSystem) continue;

                // ESP 不进候选：它是引导分区，Windows 永远不会装在里头，摆在这一列只会让人误选
                if (v.IsEsp()) continue;

                string mid = v.HasLetter ? v.DriveLetter + ":" : v.FileSystem;
                _cmb.Items.Add(new Item(v, Lang.T("Partition.ListRow", v.DiskNumber, v.PartitionNumber, mid, Fmt.Size(v.Size))));
            }

            int restore = -1, withWin = -1, winCount = 0;
            for (int i = 0; i < _cmb.Items.Count; i++)
            {
                var it = (Item)_cmb.Items[i];
                if (keep != null && it.V.VolumePath == keep) restore = i;
                if (it.V.HasWindows) { winCount++; if (withWin < 0) withWin = i; }
            }

            if (restore >= 0)
            {
                _cmb.SelectedIndex = restore;
            }
            else if (winCount == 1)
            {
                _cmb.SelectedIndex = withWin;      // 只有一个候选就直接选上，多义时不猜
                Log(Lang.T("Log.SystemPartitionSelected", ((Item)_cmb.Items[withWin]).V.Root), LogKind.Ok);
            }
            else
            {
                _cmb.SelectedIndex = -1;
            }

            // 一个含 Windows 的都没有：只是提醒一声，不妨碍手动选分区照样写。
            if (winCount == 0 && _cmb.Items.Count > 0)
                Log(Lang.T("Log.NoWindowsFound"), LogKind.Info);

            _filling = false;
        }

        /// <summary>
        /// 要写入的 ESP：先看所选分区**同一块盘**上的；那块盘上真没有（跨盘双系统）才退一步，
        /// 全机只有一个 ESP 时用它。多个又不同盘 —— 不猜，返回 null 让修复按钮保持禁用。
        /// </summary>
        VolumeInfo EspFor(VolumeInfo sys)
        {
            if (sys == null) return null;

            var offDisk = new List<VolumeInfo>();
            foreach (VolumeInfo v in _vols)
            {
                if (v.VolumePath == sys.VolumePath) continue;
                if (!v.IsEsp()) continue;
                if (v.DiskNumber == sys.DiskNumber) return v;
                offDisk.Add(v);
            }
            return offDisk.Count == 1 ? offDisk[0] : null;
        }

        VolumeInfo Selected()
        {
            var it = _cmb.SelectedItem as Item;
            return it == null ? null : it.V;
        }

        void OnComboChanged(object sender, EventArgs e)
        {
            if (_filling) { UpdateState(); return; }
            VolumeInfo v = Selected();
            if (v != null) Log(Lang.T("Log.SystemPartitionSelected", v.Root), LogKind.Info);
            UpdateState();
        }

        void UpdateState()
        {
            VolumeInfo v = Selected();
            VolumeInfo esp = EspFor(v);
            bool haveSys = v != null;
            bool pickedEsp = haveSys && v.IsEsp();

            if (esp != null)
            {
                _lblEsp.Text = Lang.T("Main.EspLine", esp.DiskNumber, esp.PartitionNumber,
                                      esp.HasLetter ? esp.DriveLetter + ":" : Fmt.Size(esp.Size));
                _lblEsp.ForeColor = Theme.Green;
            }
            else
            {
                _lblEsp.Text = haveSys ? Lang.T("Main.EspNone") : "-";
                _lblEsp.ForeColor = haveSys ? Theme.Orange : Theme.Muted;
            }

            if (!haveSys)
                Warn(Lang.T("Main.NoSelection"), Theme.Muted);
            else if (pickedEsp)
                Warn(Lang.T("Warning.SelectedEsp"), Theme.Red);
            else if (esp == null)
                Warn(Lang.T("Warning.NoEsp"), Theme.Red);
            else if (!v.HasWindows)
                // 不拦，只是说清楚后果：bcdboot 要拿这个分区当源，里头没有 Windows
                // 就没有可复制的引导文件，它必然报错退出（193 / 0x80092003）。
                Warn(Lang.T("Warning.SystemPartitionNoWindows"), Theme.Orange);
            else
                Warn(Lang.T("Warning.Ok"), Theme.Green);

            bool tools = _tools != null && _tools.Get("bcdboot.exe") != null;
            _btnRefresh.Enabled = !_busy;
            _btnOpenLog.Enabled = !_busy;
            _btnRepair.Enabled = !_busy && haveSys && !pickedEsp && esp != null && tools;

            _status.Text = _busy ? Lang.T("Status.Working") : Lang.T("Status.Ready");
        }

        void Warn(string text, Color color)
        {
            _warn.Text = text;
            _warn.ForeColor = color;
        }

        // ------------------------------------------------------------------ 按钮

        void OnRefresh(object sender, EventArgs e)
        {
            RefreshAll(false);
        }

        void OnRepair(object sender, EventArgs e)
        {
            if (Selected() == null)
            {
                MessageBox.Show(this, Lang.T("Error.NoSystemPartition"), Lang.T("App.Title"),
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this, Lang.T("Confirm.Destructive"), Lang.T("App.Title"),
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            Repair rep = new Repair(_run, _tools, _cfg, _baseDir, Log, Selected,
                                    delegate { return EspFor(Selected()); });
            Busy(delegate { return rep.DoRepair(); }, true, true);
        }

        void OnOpenLog(object sender, EventArgs e)
        {
            try { Process.Start("explorer.exe", "\"" + _logDir + "\""); }
            catch
            {
                try { Process.Start(_logDir); }
                catch (Exception ex) { Log(ex.Message, LogKind.Error); }
            }
        }

        /// <summary>干活的都在后台线程，界面只在 BeginInvoke 回来时动。</summary>
        void Busy(Func<bool> work, bool refreshAfter, bool showResult)
        {
            if (_busy) return;
            _busy = true;
            UpdateState();

            var t = new Thread(() =>
            {
                bool ok = false;
                try { ok = work(); }
                catch (Exception ex) { Log(ex.Message, LogKind.Error); }
                finally
                {
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            _busy = false;
                            if (refreshAfter && ok) RefreshAll(false);
                            UpdateState();
                            if (showResult)
                                MessageBox.Show(this, Lang.T(ok ? "Done.Success" : "Done.Failed"), Lang.T("App.Title"),
                                                MessageBoxButtons.OK,
                                                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                        });
                    }
                    catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ------------------------------------------------------------------ 日志

        void Log(string text, LogKind kind)
        {
            if (text == null) return;

            if (InvokeRequired)
            {
                if (!IsHandleCreated) return;
                try { BeginInvoke((Action<string, LogKind>)Log, text, kind); } catch { }
                return;
            }
            Append(text, kind);
            AppendFile(text, kind);
        }

        void Append(string text, LogKind kind)
        {
            string line = (kind == LogKind.Command ? "> " : kind == LogKind.Error || kind == LogKind.Warn ? "! " : "  ") + text;

            int start = _log.TextLength;
            _log.AppendText(line + "\r\n");
            _log.Select(start, line.Length);
            _log.SelectionColor = ColorFor(kind);
            _log.Select(_log.TextLength, 0);
            _log.SelectionColor = Theme.Fg;
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
            Trim();
        }

        void AppendFile(string text, LogKind kind)
        {
            try
            {
                File.AppendAllText(_logFile,
                    DateTime.Now.ToString("HH:mm:ss") + " [" + kind + "] " + text + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>界面日志只留最近几百行，老的从头部切掉（颜色跟着保留）。</summary>
        void Trim()
        {
            if (_log.Lines.Length <= 600) return;
            _log.ReadOnly = false;
            _log.Select(0, _log.GetFirstCharIndexFromLine(100));
            _log.SelectedText = "";
            _log.ReadOnly = true;
        }

        static Color ColorFor(LogKind kind)
        {
            switch (kind)
            {
                case LogKind.Command: return Theme.Accent;
                case LogKind.Ok: return Theme.Green;
                case LogKind.Warn: return Theme.Orange;
                case LogKind.Error: return Theme.Red;
                case LogKind.Tool: return Theme.Muted;
                default: return Theme.Fg;
            }
        }

        sealed class Item
        {
            public readonly VolumeInfo V;
            readonly string _text;

            public Item(VolumeInfo v, string text) { V = v; _text = text; }

            public override string ToString() { return _text; }
        }
    }
}
