using System;
using System.Drawing;
using System.Windows.Forms;

namespace EpsBootRepair
{
    /// <summary>
    /// 高级修复的第二道确认。
    ///
    /// 头一道是一般的"确定/取消"框，手指快一点就过去了；这条高级路又刻意绕开了主界面
    /// "候选不唯一就不猜"的纪律（源和目标都是用户自己点的，程序一个都不校验），
    /// 于是一路回车就能把引导文件写到一块不相干的分区上。第二道因此要求**手动敲 YES**：
    /// 它挡不住真要走这条路的人，只把"随手按回车"和"我看清楚了"分开。
    ///
    /// 不匹配就地打回，窗口不关；匹配大小写不纠、前后空白不管。
    /// </summary>
    public sealed class YesPromptForm : Form
    {
        const string Token = "YES";

        readonly TextBox _box = new TextBox();
        readonly Label _hint = new Label();
        readonly string _wrong;

        public YesPromptForm(string title, string prompt, string detail, string wrong)
        {
            _wrong = wrong;

            Text = title;
            ClientSize = new Size(560, 252);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            Font = Theme.Ui;
            DoubleBuffered = true;

            Controls.Add(new Label
            {
                Text = prompt,
                Location = new Point(16, 16),
                Size = new Size(528, 40),
                ForeColor = Theme.Fg,
                Font = Theme.Ui,
                BackColor = Color.Transparent
            });

            // 要敲 YES 的同时再把命令行摊在眼前念一遍 —— 第二道确认的意义就在这儿
            Controls.Add(new Label
            {
                Text = detail,
                Location = new Point(16, 62),
                Size = new Size(528, 60),
                ForeColor = Theme.Muted,
                Font = Theme.Mono,
                BackColor = Color.Transparent
            });

            _box.Location = new Point(16, 134);
            _box.Size = new Size(528, 26);
            _box.BackColor = Theme.CardBg;
            _box.ForeColor = Theme.Fg;
            _box.Font = Theme.Mono;
            _box.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_box);

            _hint.Location = new Point(16, 168);
            _hint.Size = new Size(528, 20);
            _hint.ForeColor = Theme.Red;
            _hint.Font = Theme.Ui;
            _hint.BackColor = Color.Transparent;
            Controls.Add(_hint);

            Button ok = Theme.Btn(Lang.T("Button.Ok"), 16, 200, 250, 36,
                                  Theme.Red, Theme.Fg, delegate { Confirm(); });
            Button cancel = Theme.Btn(Lang.T("Button.Cancel"), 294, 200, 250, 36,
                                      Theme.BtnBg, Theme.Fg, delegate
                                      {
                                          DialogResult = DialogResult.Cancel;
                                          Close();
                                      });
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        void Confirm()
        {
            if (string.Equals(_box.Text.Trim(), Token, StringComparison.OrdinalIgnoreCase))
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            _hint.Text = _wrong;
            _box.SelectAll();
            _box.Focus();
        }
    }
}
