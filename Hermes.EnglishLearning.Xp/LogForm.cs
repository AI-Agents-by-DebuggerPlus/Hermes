using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Hermes.EnglishLearning.Xp;

internal sealed class LogForm : Form
{
    private readonly TextBox _box;
    private readonly Label _pathLabel;
    private bool _hideOnClose = true;

    public LogForm()
    {
        Text = "Лог — English Learning XP";
        Width = 720;
        Height = 480;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(11, 14, 17);
        ForeColor = Color.FromArgb(234, 236, 239);
        Font = new Font("Consolas", 9f, FontStyle.Regular);
        MinimizeBox = true;
        ShowInTaskbar = false;

        _pathLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(170, 178, 192),
            BackColor = Color.FromArgb(18, 26, 40),
            Padding = new Padding(8, 0, 0, 0),
        };

        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            BackColor = Color.FromArgb(18, 26, 40),
        };

        var btnRefresh = MakeBarBtn("Обновить", 0);
        var btnFolder = MakeBarBtn("Папка…", 1);
        var btnClear = MakeBarBtn("Очистить UI", 2);
        var btnClose = MakeBarBtn("Закрыть", 3);
        btnRefresh.Click += (_, __) => Reload();
        btnFolder.Click += (_, __) => OpenLogFolder();
        btnClear.Click += (_, __) => _box.Clear();
        btnClose.Click += (_, __) => Hide();
        bar.Controls.Add(btnRefresh);
        bar.Controls.Add(btnFolder);
        bar.Controls.Add(btnClear);
        bar.Controls.Add(btnClose);
        bar.Resize += (_, __) =>
        {
            var right = bar.ClientSize.Width - 8;
            foreach (Control c in bar.Controls)
            {
                var b = c as Button;
                if (b == null) continue;
                var slot = (int)b.Tag;
                b.Location = new Point(right - (4 - slot) * 108, 6);
            }
        };

        _box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(234, 236, 239),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f, FontStyle.Regular),
        };

        Controls.Add(_box);
        Controls.Add(bar);
        Controls.Add(_pathLabel);

        Load += (_, __) =>
        {
            Reload();
            AppLog.LineAdded += OnLine;
        };
        FormClosing += (_, e) =>
        {
            if (_hideOnClose)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                AppLog.LineAdded -= OnLine;
            }
        };
    }

    public void ForceClose()
    {
        _hideOnClose = false;
        Close();
    }

    public void Reload()
    {
        var sb = new StringBuilder();
        foreach (var line in AppLog.GetSessionLines())
            sb.AppendLine(line);
        _box.Text = sb.ToString();
        _box.SelectionStart = _box.Text.Length;
        _box.ScrollToCaret();
        _pathLabel.Text = "  Файл: " + AppLog.LogPath + "  ·  строк: " + AppLog.GetSessionLines().Count;
    }

    private void OnLine(string line)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(OnLine), line);
                return;
            }

            _box.AppendText(line + Environment.NewLine);
            _box.SelectionStart = _box.Text.Length;
            _box.ScrollToCaret();
        }
        catch
        {
            // ignore
        }
    }

    private static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDir);
            Process.Start(AppLog.LogDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Папка логов", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Button MakeBarBtn(string text, int slot)
    {
        var b = new Button
        {
            Text = text,
            Width = 100,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(43, 49, 57),
            ForeColor = Color.FromArgb(234, 236, 239),
            Tag = slot,
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(56, 63, 73);
        return b;
    }
}
