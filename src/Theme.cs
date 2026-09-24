using System;
using System.Drawing;
using System.Windows.Forms;

namespace EpsBootRepair
{
    /// <summary>
    /// 深色配色照搬 easy_webeav（GitHub dark），控件全部手工摆位。
    /// 不用 WPF：PE 里 WPF 要 milcore/d3d，WinForms 只靠 GDI 更稳。
    /// </summary>
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(0x0d, 0x11, 0x17);
        public static readonly Color CardBg = Color.FromArgb(0x16, 0x1b, 0x22);
        public static readonly Color LogBg = Color.FromArgb(0x01, 0x04, 0x09);
        public static readonly Color Border = Color.FromArgb(0x30, 0x36, 0x3d);
        public static readonly Color Fg = Color.FromArgb(0xe6, 0xed, 0xf3);
        public static readonly Color Muted = Color.FromArgb(0x8b, 0x94, 0x9e);
        public static readonly Color Accent = Color.FromArgb(0x58, 0xa6, 0xff);
        public static readonly Color Green = Color.FromArgb(0x3f, 0xb9, 0x50);
        public static readonly Color Red = Color.FromArgb(0xf8, 0x51, 0x49);
        public static readonly Color Orange = Color.FromArgb(0xd2, 0x9a, 0x22);
        public static readonly Color BtnBg = Color.FromArgb(0x21, 0x26, 0x2d);
        public static readonly Color Dark = Color.FromArgb(0x0d, 0x11, 0x17);

        public static readonly Font Ui = Pick(
            new[] { "Microsoft YaHei UI", "微软雅黑", "Microsoft YaHei", "SimSun", "宋体", "NSimSun", "新宋体", "SimHei", "黑体" },
            9.5f, FontStyle.Regular);

        public static readonly Font UiBold = new Font(Ui.FontFamily, 9.5f, FontStyle.Bold);
        public static readonly Font Title = new Font(Ui.FontFamily, 11f, FontStyle.Bold);
        public static readonly Font Big = new Font(Ui.FontFamily, 12f, FontStyle.Bold);
        public static readonly Font BtnFont = new Font(Ui.FontFamily, 10f, FontStyle.Regular);

        public static readonly Font Mono = Pick(
            new[] { "Consolas", "Courier New", "宋体", "SimSun" }, 10.5f, FontStyle.Regular);

        /// <summary>PE 里的字体不一定齐，按候选表挑第一个真装了的。</summary>
        static Font Pick(string[] names, float size, FontStyle style)
        {
            foreach (string want in names)
            {
                try
                {
                    foreach (FontFamily f in FontFamily.Families)
                    {
                        if (!string.Equals(f.Name, want, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!f.IsStyleAvailable(style)) continue;
                        return new Font(f, size, style);
                    }
                }
                catch { }
            }
            return new Font(FontFamily.GenericSansSerif, size, style);
        }

        public static Color Mix(Color c, int delta)
        {
            return Color.FromArgb(
                Math.Min(255, Math.Max(0, c.R + delta)),
                Math.Min(255, Math.Max(0, c.G + delta)),
                Math.Min(255, Math.Max(0, c.B + delta)));
        }

        /// <summary>
        /// 往卡片底色方向拉一把，做"不可用"的样子。WinForms 的平面按钮即使 Enabled=false
        /// 也照画 BackColor，只把文字变灰 —— 绿色的"一键修复"于是看上去永远可用，
        /// 用户点不动还以为是坏的。所以禁用态必须自己把底色压暗。
        /// </summary>
        static Color Faded(Color c)
        {
            return Color.FromArgb((c.R + CardBg.R * 2) / 3,
                                  (c.G + CardBg.G * 2) / 3,
                                  (c.B + CardBg.B * 2) / 3);
        }

        /// <summary>一块卡片：底色 + 左上角一个蓝色标题。</summary>
        public static Panel Card(string title, int x, int y, int w, int h)
        {
            var p = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = CardBg };
            p.Controls.Add(new Label
            {
                Text = title,
                Location = new Point(14, 12),
                AutoSize = true,
                ForeColor = Accent,
                Font = Title,
                BackColor = Color.Transparent
            });
            return p;
        }

        public static Label Caption(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = Muted,
                Font = Ui,
                BackColor = Color.Transparent
            };
        }

        public static Label Value(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = Fg,
                Font = Big,
                BackColor = Color.Transparent
            };
        }

        public static Button Btn(string text, int x, int y, int w, int h, Color back, Color fore, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = back,
                ForeColor = fore,
                Font = BtnFont,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Mix(back, 20);
            b.FlatAppearance.MouseDownBackColor = Mix(back, -20);

            b.EnabledChanged += delegate
            {
                b.BackColor = b.Enabled ? back : Faded(back);
                b.ForeColor = b.Enabled ? fore : Muted;
                b.Cursor = b.Enabled ? Cursors.Hand : Cursors.Default;
            };

            if (onClick != null) b.Click += onClick;
            return b;
        }
    }
}
