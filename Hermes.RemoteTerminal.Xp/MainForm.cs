using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hermes.RemoteTerminal.Xp;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _terminal;
    private readonly Label _status;
    private readonly SupabasePoller _poller;
    private LogForm _logForm;
    private int _lineCount;

    public MainForm(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException("settings");
        Text = "Hermes Remote Terminal XP — view only v" + AppVersion.Display;
        Width = 900;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 14, 17);
        ForeColor = Color.FromArgb(234, 236, 239);
        Font = new Font("Segoe UI", 9f);

        var top = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Color.FromArgb(18, 26, 40),
        };

        var btnSettings = MakeTopBtn("Настройки", 0);
        var btnLog = MakeTopBtn("Лог", 1);
        var btnClear = MakeTopBtn("Очистить", 2);
        btnSettings.Click += (_, __) => ShowSettings();
        btnLog.Click += (_, __) => ShowLog();
        btnClear.Click += (_, __) => { _terminal.Clear(); _lineCount = 0; };
        top.Controls.Add(btnSettings);
        top.Controls.Add(btnLog);
        top.Controls.Add(btnClear);
        top.Resize += (_, __) => LayoutTopButtons(top);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(170, 178, 192),
            BackColor = Color.FromArgb(18, 26, 40),
            Padding = new Padding(8, 0, 0, 0),
            Text = "Starting…",
        };

        _terminal = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = true,
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(234, 236, 239),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10f),
        };

        Controls.Add(_terminal);
        Controls.Add(_status);
        Controls.Add(top);

        _poller = new SupabasePoller(_settings);
        _poller.StatusChanged += OnStatus;
        _poller.LineReceived += OnLine;

        Shown += (_, __) =>
        {
            AppendLocal("RemoteTerminal XP ready. Recipient filter: " + _settings.RecipientName);
            if (!_poller.IsConfigured)
            {
                OnStatus("Заполните Supabase URL и anon key в Настройках");
                ShowSettings();
            }
            else
            {
                _poller.Start();
            }
        };

        FormClosed += (_, __) =>
        {
            _poller.Stop();
            if (_logForm != null)
            {
                try { _logForm.ForceClose(); } catch { /* ignore */ }
            }
        };
    }

    private static Button MakeTopBtn(string text, int slot)
    {
        var b = new Button
        {
            Text = text,
            Tag = slot,
            Width = 110,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(43, 49, 57),
            ForeColor = Color.FromArgb(234, 236, 239),
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(48, 54, 61);
        return b;
    }

    private static void LayoutTopButtons(Panel top)
    {
        var x = 8;
        foreach (Control c in top.Controls)
        {
            var b = c as Button;
            if (b == null) continue;
            b.Location = new Point(x + (int)b.Tag * 118, 6);
        }
    }

    private void OnStatus(string s)
    {
        if (IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            _status.Text = s ?? string.Empty;
            AppLog.Info("Status: " + s);
        }));
    }

    private void OnLine(TerminalLine line)
    {
        if (IsDisposed || line == null) return;
        BeginInvoke(new Action(() =>
        {
            var when = FormatTime(line.CreatedAt);
            var route = (line.Sender ?? "?") + " → " + (line.Recipient ?? "?");
            var body = FlattenContent(line.Content);
            AppendLocal(when + "  " + route + "  " + body);
        }));
    }

    private void AppendLocal(string text)
    {
        if (_lineCount >= _settings.MaxLines)
        {
            _terminal.Clear();
            _lineCount = 0;
            _terminal.AppendText("--- truncated ---" + Environment.NewLine);
        }

        _terminal.AppendText(text + Environment.NewLine);
        _terminal.SelectionStart = _terminal.TextLength;
        _terminal.ScrollToCaret();
        _lineCount++;
    }

    private static string FormatTime(string createdAt)
    {
        DateTimeOffset dto;
        if (DateTimeOffset.TryParse(createdAt, out dto))
            return dto.ToLocalTime().ToString("HH:mm:ss");
        return DateTime.Now.ToString("HH:mm:ss");
    }

    private static string FlattenContent(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        return content.Replace("\r\n", " ¶ ").Replace('\n', '¶').Replace('\r', '¶');
    }

    private void ShowLog()
    {
        if (_logForm == null || _logForm.IsDisposed)
            _logForm = new LogForm();
        _logForm.Show(this);
        _logForm.BringToFront();
    }

    private void ShowSettings()
    {
        using (var dlg = new SettingsForm(_settings))
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            SettingsStore.Save(_settings);
            _poller.Stop();
            if (_poller.IsConfigured)
            {
                _poller.Start();
                AppendLocal("Settings saved — poll restarted. Filter: " + _settings.RecipientName);
            }
            else
            {
                OnStatus("Supabase не настроен");
            }
        }
    }
}

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _s;
    private readonly TextBox _url;
    private readonly TextBox _key;
    private readonly TextBox _recipient;
    private readonly NumericUpDown _poll;
    private readonly CheckBox _all;

    public SettingsForm(AppSettings s)
    {
        _s = s;
        Text = "Настройки — Remote Terminal XP";
        Width = 560;
        Height = 320;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(11, 14, 17);
        ForeColor = Color.FromArgb(234, 236, 239);

        var y = 16;
        Controls.Add(MakeLabel("Supabase URL", 16, y));
        _url = MakeBox(160, y, 360, s.SupabaseUrl ?? string.Empty);
        Controls.Add(_url);
        y += 40;
        Controls.Add(MakeLabel("Anon key", 16, y));
        _key = MakeBox(160, y, 360, s.SupabaseAnonKey ?? string.Empty);
        Controls.Add(_key);
        y += 40;
        Controls.Add(MakeLabel("Recipient filter", 16, y));
        _recipient = MakeBox(160, y, 360, s.RecipientName ?? "RemoteTerminal");
        Controls.Add(_recipient);
        y += 40;
        Controls.Add(MakeLabel("Poll seconds", 16, y));
        _poll = new NumericUpDown
        {
            Left = 160,
            Top = y,
            Width = 80,
            Minimum = 3,
            Maximum = 60,
            Value = Math.Max(3, Math.Min(60, s.PollSeconds)),
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(234, 236, 239),
        };
        Controls.Add(_poll);
        y += 40;
        _all = new CheckBox
        {
            Left = 160,
            Top = y,
            Width = 360,
            Text = "Показывать все recipient (отладка)",
            Checked = s.ShowAllRecipients,
            ForeColor = Color.FromArgb(234, 236, 239),
        };
        Controls.Add(_all);

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Left = 320,
            Top = 240,
            Width = 90,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(43, 49, 57),
            ForeColor = Color.White,
        };
        var cancel = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            Left = 420,
            Top = 240,
            Width = 90,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(43, 49, 57),
            ForeColor = Color.White,
        };
        ok.Click += (_, __) =>
        {
            _s.SupabaseUrl = _url.Text.Trim();
            _s.SupabaseAnonKey = _key.Text.Trim();
            _s.RecipientName = string.IsNullOrWhiteSpace(_recipient.Text) ? "RemoteTerminal" : _recipient.Text.Trim();
            _s.PollSeconds = (int)_poll.Value;
            _s.ShowAllRecipients = _all.Checked;
        };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private static Label MakeLabel(string t, int x, int y)
    {
        return new Label
        {
            Text = t,
            Left = x,
            Top = y + 3,
            Width = 140,
            ForeColor = Color.FromArgb(170, 178, 192),
        };
    }

    private static TextBox MakeBox(int x, int y, int w, string text)
    {
        return new TextBox
        {
            Left = x,
            Top = y,
            Width = w,
            Text = text,
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.FromArgb(234, 236, 239),
            BorderStyle = BorderStyle.FixedSingle,
        };
    }
}
