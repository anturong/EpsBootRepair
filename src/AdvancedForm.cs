using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace EpsBootRepair
{
    /// <summary>
    /// 高级模式：源（Windows 系统分区）手动指定，目标是**磁盘**——选中一块盘，
    /// 引导分区由这块盘上的候选推导，程序不替用户挑盘。
    ///
    /// 引导方式**不由用户选**，由推导出的目标分区决定 —— 见 <see cref="Repair.IsLegacyTarget"/>：
    /// 目标是 ESP 就走 UEFI，是非 ESP 的 MBR 分区就走 Legacy BIOS。
    ///
    /// 一块盘上候选恰好一个就直接用它；一个都没有该行灰掉、选不中；两个以上时该行仍然可以选，
    /// 但下面会多出下拉让用户指定"是哪一个"（这正是"高级"的那一步，不是另一条独立入口）。
    ///
    /// 这条路是**刻意**绕过主界面那条"候选不唯一就不禁用/不猜"的纪律的（主界面上
    /// 没有 ESP、或有多个活动主分区时修复按钮直接禁用，正是这个窗口存在的理由）。
    /// 所以安全带得自己带：两项都不预选、两个都选齐才让点，点"开始修复"要过**两道**确认
    /// （第一道写清源、目标、要跑的命令；第二道要求手动敲 YES）。
    /// </summary>
    public sealed class AdvancedForm : Form
    {
        /// <summary>用户确认后由 <see cref="Accept"/> 填上。</summary>
        public VolumeInfo Source;
        public VolumeInfo Target;

        /// <summary>即将执行的命令行（预览与确认框共用同一份文本）。</summary>
        public string Command { get; private set; }

        readonly ComboBox _cmbSource = new ComboBox();
        readonly ComboBox _cmbDisk = new ComboBox();
        readonly ComboBox _cmbPart = new ComboBox();
        readonly Label _capPart = new Label();
        readonly Label _preview = new Label();
        readonly Label _warn = new Label();
        readonly Button _btnStart;
        readonly ToolSet _tools;

        bool _filling;                 // 往下拉里灌数据时压住事件
        int _lastDisk = -1;            // 灰掉的行不给选：选中了要退回上一次
        string _diskHint;              // "这块盘没得写"的提示，由 UpdatePreview 排进警告区

        public AdvancedForm(List<VolumeInfo> vols, ToolSet tools)
        {
            _tools = tools;

            Text = Lang.T("Advanced.Title");
            ClientSize = new Size(620, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            Font = Theme.Ui;
            DoubleBuffered = true;

            // ---------------- 选源 / 选盘
            Panel pick = Theme.Card(Lang.T("Advanced.Title"), 10, 10, 600, 258);

            var tip = new Label
            {
                Text = Lang.T("Advanced.Tip"),
                Location = new Point(14, 42),
                Size = new Size(572, 52),
                ForeColor = Theme.Muted,
                Font = Theme.Ui,
                BackColor = Color.Transparent
            };
            pick.Controls.Add(tip);

            pick.Controls.Add(Theme.Caption(Lang.T("Advanced.SourceLabel"), 14, 100));
            Setup(_cmbSource, 14, 118);
            pick.Controls.Add(_cmbSource);

            pick.Controls.Add(Theme.Caption(Lang.T("Advanced.TargetLabel"), 14, 154));
            Setup(_cmbDisk, 14, 172);
            // 灰掉的行要自己画：ComboBox 没法单独禁掉某一项。选中的项归 SelectedIndexChanged 管，
            // 这里只管上色。
            _cmbDisk.DrawMode = DrawMode.OwnerDrawFixed;
            _cmbDisk.ItemHeight = 22;
            _cmbDisk.DrawItem += OnDiskDraw;
            pick.Controls.Add(_cmbDisk);

            // 第二级：只有"这块盘候选不唯一"时才露出来
            Setup(_capPart, Lang.T("Advanced.PartLabel"), 14, 208);
            _capPart.Visible = false;
            pick.Controls.Add(_capPart);

            Setup(_cmbPart, 14, 226);
            _cmbPart.Visible = false;
            pick.Controls.Add(_cmbPart);

            FillSource(vols);
            FillDisks(vols);

            // 两项都不预选：预选了就是猜
            _cmbSource.SelectedIndex = -1;
            _cmbDisk.SelectedIndex = -1;

            // ---------------- 预览 + 警告
            Panel prev = Theme.Card(Lang.T("Advanced.PreviewLabel"), 10, 276, 600, 130);

            _preview.Location = new Point(14, 42);
            _preview.Size = new Size(572, 44);
            _preview.ForeColor = Theme.Fg;
            _preview.Font = Theme.Mono;
            _preview.BackColor = Color.Transparent;
            prev.Controls.Add(_preview);

            _warn.Location = new Point(14, 90);
            _warn.Size = new Size(572, 34);
            _warn.ForeColor = Theme.Muted;
            _warn.Font = Theme.Ui;
            _warn.BackColor = Color.Transparent;
            prev.Controls.Add(_warn);

            // ---------------- 按钮
            _btnStart = Theme.Btn(Lang.T("Button.StartRepair"), 10, 418, 300, 38,
                                  Theme.Green, Theme.Dark, delegate { Accept(); });
            Button cancel = Theme.Btn(Lang.T("Button.Cancel"), 320, 418, 290, 38,
                                      Theme.BtnBg, Theme.Fg, delegate
                                      {
                                          DialogResult = DialogResult.Cancel;
                                          Close();
                                      });
            Controls.Add(pick);
            Controls.Add(prev);
            Controls.Add(_btnStart);
            Controls.Add(cancel);

            AcceptButton = _btnStart;
            CancelButton = cancel;

            // 到这里 _btnStart 才存在，现在挂选中事件才不会撞空引用
            _cmbSource.SelectedIndexChanged += delegate { UpdatePreview(); };
            _cmbDisk.SelectedIndexChanged += OnDiskChanged;
            _cmbPart.SelectedIndexChanged += delegate { if (!_filling) UpdatePreview(); };

            UpdatePreview();
        }

        // ------------------------------------------------------------------ 候选

        /// <summary>源 = 主界面那个列表同一套过滤（含"ESP 不能当源"）。</summary>
        void FillSource(List<VolumeInfo> vols)
        {
            _filling = true;
            foreach (VolumeInfo v in vols)
            {
                if (string.IsNullOrEmpty(v.FileSystem)) continue;
                if (!v.CanBeSystem) continue;
                if (v.IsEsp()) continue;                 // 选 ESP 当源是那个老坑，别让它回来
                _cmbSource.Items.Add(new Row(v, Lang.T("Partition.ListRow",
                    v.DiskNumber, Disks.ModelOf(v), Fmt.DiskSize(v.DiskSize),
                    v.PartitionNumber, Mid(v), Fmt.Size(v.Size))));
            }
            _filling = false;
        }

        /// <summary>目标 = 盘。每行带这块盘上推出来的引导类别（EPS / MBR），推不出来就没有后缀、该行灰掉。</summary>
        void FillDisks(List<VolumeInfo> vols)
        {
            _filling = true;
            foreach (DiskInfo d in Disks.Enumerate(vols))
                _cmbDisk.Items.Add(new DiskRow(d, Disks.RowText(d)));
            _filling = false;
        }

        static string Mid(VolumeInfo v)
        {
            return v.HasLetter ? v.DriveLetter + ":" : v.FileSystem;
        }

        // ------------------------------------------------------------------ 盘 / 分区联动

        void OnDiskDraw(object sender, DrawItemEventArgs e)
        {
            Color back = (e.State & DrawItemState.Selected) != 0 ? Theme.BtnBg : Theme.Bg;
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index < 0) return;

            var row = _cmbDisk.Items[e.Index] as DiskRow;
            bool usable = row != null && row.D.Candidates.Count > 0;
            TextRenderer.DrawText(e.Graphics, row == null ? "" : row.ToString(), Theme.Ui, e.Bounds,
                                  usable ? Theme.Fg : Theme.Muted,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        void OnDiskChanged(object sender, EventArgs e)
        {
            if (_filling) return;

            var row = _cmbDisk.SelectedItem as DiskRow;
            if (row == null || row.D.Candidates.Count == 0)
            {
                // 一个候选都没有的盘不给修：退回上一次选中，并把原因摆到警告区
                _filling = true;
                _cmbDisk.SelectedIndex = _lastDisk;
                _filling = false;
                _diskHint = Lang.T("Advanced.NoCandidateOnDisk");
                UpdatePreview();
                return;
            }

            _diskHint = null;
            _lastDisk = _cmbDisk.SelectedIndex;
            FillParts(row);
            UpdatePreview();
        }

        /// <summary>候选不止一个才让用户接着指定；恰好一个就直接用。</summary>
        void FillParts(DiskRow row)
        {
            _filling = true;
            _cmbPart.Items.Clear();

            bool many = row != null && row.D.Candidates.Count > 1;
            if (many)
            {
                foreach (VolumeInfo v in row.D.Candidates)
                    _cmbPart.Items.Add(new Row(v, Lang.T("Target.Row", v.PartitionNumber,
                        Disks.KindName(Disks.KindOf(v)), Mid(v))));
                _cmbPart.SelectedIndex = -1;
            }

            _capPart.Visible = many;
            _cmbPart.Visible = many;
            _filling = false;
        }

        /// <summary>这块盘上要写的那一个分区。候选不唯一而用户还没指定时返回 null。</summary>
        VolumeInfo ResolveTarget()
        {
            var row = _cmbDisk.SelectedItem as DiskRow;
            if (row == null || row.D.Candidates.Count == 0) return null;
            if (row.D.Candidates.Count == 1) return row.D.Candidates[0];
            return Pick(_cmbPart);
        }

        // ------------------------------------------------------------------ 状态

        void Setup(ComboBox c, int x, int y)
        {
            c.Location = new Point(x, y);
            c.Size = new Size(572, 26);
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Theme.Bg;
            c.ForeColor = Theme.Fg;
            c.Font = Theme.Ui;
            // SelectedIndexChanged 故意不在这里挂：往空的下拉里加第一项就会自动选中并触发它，
            // 那会儿 _btnStart 还没建出来，UpdatePreview 会撞空引用。挂的事件放在构造函数末尾。
        }

        static void Setup(Label l, string text, int x, int y)
        {
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = true;
            l.ForeColor = Theme.Muted;
            l.Font = Theme.Ui;
            l.BackColor = Color.Transparent;
        }

        static VolumeInfo Pick(ComboBox c)
        {
            var row = c.SelectedItem as Row;
            return row == null ? null : row.V;
        }

        /// <summary>盘符还没分配（EnsureLetter 稍后才做）时不能假装知道，用占位符顶替。</summary>
        static string ArgFor(VolumeInfo v)
        {
            return v.RootArg != null ? v.RootArg : Lang.T("Advanced.LetterPending");
        }

        void UpdatePreview()
        {
            VolumeInfo s = Pick(_cmbSource);
            VolumeInfo t = ResolveTarget();

            List<string> warn = new List<string>();
            if (!string.IsNullOrEmpty(_diskHint)) warn.Add(_diskHint);

            if (s == null || t == null)
            {
                _preview.Text = Lang.T("Advanced.NeedBoth");
                _preview.ForeColor = Theme.Muted;
                Command = null;
            }
            else
            {
                // 命令行由 Repair 那几个 argv 构造函数拼 —— 和真正执行的是同一份代码，
                // 预览不可能跟实际跑的对不上
                string sa = ArgFor(s), ta = ArgFor(t);
                if (Repair.IsLegacyTarget(t))
                    _preview.Text = "bcdboot.exe " + Repair.BcdBiosArgs(sa, ta) + "\r\n" +
                                    "bootsect.exe " + Repair.BootsectArgs(ta);
                else
                    _preview.Text = "bcdboot.exe " + Repair.UefiArgs(sa, ta);

                _preview.ForeColor = Theme.Fg;
                Command = _preview.Text;

                if (Repair.IsLegacyTarget(t) && !t.IsActive)
                    warn.Add(Lang.T("Warning.AdvancedInactiveTarget"));
                if (!s.HasWindows)
                    warn.Add(Lang.T("Warning.SystemPartitionNoWindows"));
            }

            _warn.Text = string.Join("\r\n", warn.ToArray());
            _warn.ForeColor = warn.Count > 0 ? Theme.Orange : Theme.Muted;

            bool legacy = t != null && Repair.IsLegacyTarget(t);
            bool tools = _tools != null
                         && _tools.Get("bcdboot.exe") != null
                         && (!legacy || _tools.Get("bootsect.exe") != null);

            // 源==目标是合法的（单分区 Legacy 安装，引导分区就是 Windows 分区自己），
            // 所以这里不禁止两者相同
            _btnStart.Enabled = s != null && t != null && !s.IsEsp() && tools;
        }

        void Accept()
        {
            VolumeInfo s = Pick(_cmbSource);
            VolumeInfo t = ResolveTarget();
            if (s == null || t == null) return;

            // 两道确认，都在这里：第一道把源/目标/命令念一遍，第二道要求手动敲 YES。
            // 放在对话框里而不是调用方，是因为这一路的目标是用户手点的、程序一个都不校验，
            // 确认就是这条路唯一的安全带；把它和"选完点开始"放在同一个地方才不会被漏掉。
            if (MessageBox.Show(this, Lang.T("Confirm.DestructiveAdvanced", s.Root, t.Root, Command),
                                Lang.T("App.Title"),
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            // 标题不能复用 App.Title —— 那和主窗口同名，自动化/肉眼都分不出来
            using (var yes = new YesPromptForm(Lang.T("Confirm.AdvancedYesTitle"),
                                               Lang.T("Confirm.AdvancedTypeYes"),
                                               Command,
                                               Lang.T("Confirm.AdvancedWrongToken")))
            {
                if (yes.ShowDialog(this) != DialogResult.OK) return;
            }

            Source = s;
            Target = t;
            DialogResult = DialogResult.OK;
            Close();
        }

        sealed class Row
        {
            public readonly VolumeInfo V;
            readonly string _text;

            public Row(VolumeInfo v, string text) { V = v; _text = text; }

            public override string ToString() { return _text; }
        }

        sealed class DiskRow
        {
            public readonly DiskInfo D;
            readonly string _text;

            public DiskRow(DiskInfo d, string text) { D = d; _text = text; }

            public override string ToString() { return _text; }
        }
    }
}
