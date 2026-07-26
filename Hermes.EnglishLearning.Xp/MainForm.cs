using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Hermes.EnglishLearning.Xp;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly SupabasePoller _poller;
    private readonly Panel _cardHost;
    private readonly Label _statusLabel;
    private readonly Label _progressLabel;
    private readonly Label _titleLabel;
    private readonly Panel _chrome;
    private readonly Button _btnPrev;
    private readonly Button _btnNext;
    private readonly Button _btnFs;
    private readonly Button _btnOpen;
    private readonly Button _btnLog;
    private readonly Button _btnNet;

    private List<LessonScreen> _screens = new List<LessonScreen>();
    private int _index;
    private bool _fullscreen;
    private FormBorderStyle _prevBorder;
    private FormWindowState _prevState;
    private Rectangle _prevBounds;
    private LogForm _logForm;
    private double _scale = 1.0;

    public MainForm(AppSettings settings)
    {
        _settings = settings;
        _scale = settings.UiScale > 0 ? settings.UiScale : 1.0;
        Text = BuildWindowTitle();
        Width = 900;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 14, 17);
        ForeColor = Color.FromArgb(234, 236, 239);
        Font = new Font("Tahoma", 10f, FontStyle.Regular);
        KeyPreview = true;

        _chrome = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Color.FromArgb(18, 26, 40),
        };

        _titleLabel = new Label
        {
            AutoSize = false,
            Text = "English Learning XP — waiting for lesson…",
            ForeColor = Color.FromArgb(248, 209, 47),
            Location = new Point(10, 12),
            Width = 280,
            Height = 22,
        };

        _btnOpen = MakeBtn("Open", 0);
        _btnPrev = MakeBtn("◀", 1);
        _btnNext = MakeBtn("▶", 2);
        _btnFs = MakeBtn("Full", 3);
        _btnLog = MakeBtn("Log", 4);
        _btnNet = MakeBtn("Net", 5);
        _btnOpen.Click += (_, __) => OpenLocal();
        _btnPrev.Click += (_, __) => GoPrev();
        _btnNext.Click += (_, __) => GoNext();
        _btnFs.Click += (_, __) => ToggleFullscreen();
        _btnLog.Click += (_, __) => ShowLogWindow();
        _btnNet.Click += (_, __) => RunNetworkDiagnostics();

        _chrome.Controls.Add(_titleLabel);
        _chrome.Controls.Add(_btnOpen);
        _chrome.Controls.Add(_btnPrev);
        _chrome.Controls.Add(_btnNext);
        _chrome.Controls.Add(_btnFs);
        _chrome.Controls.Add(_btnLog);
        _chrome.Controls.Add(_btnNet);
        _chrome.Resize += (_, __) => LayoutChromeButtons();

        _cardHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(11, 14, 17),
            Padding = new Padding(24),
            AutoScroll = true,
        };

        var statusBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            BackColor = Color.FromArgb(18, 26, 40),
        };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(170, 178, 192),
            Text = "  Starting…",
            Padding = new Padding(8, 0, 0, 0),
        };
        _progressLabel = new Label
        {
            Dock = DockStyle.Right,
            Width = 200,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(248, 209, 47),
            Text = "0 / 0  ",
            Padding = new Padding(0, 0, 8, 0),
        };
        statusBar.Controls.Add(_statusLabel);
        statusBar.Controls.Add(_progressLabel);

        Controls.Add(_cardHost);
        Controls.Add(statusBar);
        Controls.Add(_chrome);

        KeyDown += MainForm_OnKeyDown;
        Resize += (_, __) => { if (_screens.Count > 0) RenderCurrent(); };

        _poller = new SupabasePoller(_settings);
        _poller.StatusChanged += s => BeginInvokeIfNeeded(() =>
        {
            // Keep nav/lesson lines from handlers; still show poller status otherwise.
            if (s != null && s.StartsWith("Nav:", StringComparison.OrdinalIgnoreCase))
                _statusLabel.Text = "  [" + DateTime.Now.ToString("HH:mm:ss") + "] " + s;
            else
                _statusLabel.Text = "  " + s;
        });
        _poller.LessonReceived += OnLesson;
        _poller.NavReceived += OnNav;

        Load += (_, __) =>
        {
            LayoutChromeButtons();
            UpdateScaleStatus();
            TryLoadSample();
            if (_poller.IsConfigured)
            {
                _poller.Start();
                // Quick net check at start (background)
                NetworkDiagnostics.RunAsync(_settings, () =>
                    BeginInvokeIfNeeded(() =>
                    {
                        if (_statusLabel.Text != null && _statusLabel.Text.IndexOf("listening", StringComparison.OrdinalIgnoreCase) < 0)
                            _statusLabel.Text = "  Diagnostics done — see Log";
                    }));
            }
            else
            {
                _statusLabel.Text = "  No Supabase keys in settings.json";
                AppLog.Warn("Supabase not configured");
            }
        };
        FormClosed += (_, __) =>
        {
            _poller.Dispose();
            if (_logForm != null)
            {
                try { _logForm.ForceClose(); } catch { /* ignore */ }
                _logForm = null;
            }
        };
    }

    private static string BuildWindowTitle()
    {
        try
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v != null)
                return "Hermes English Learning XP  v" + v.Major + "." + v.Minor + "." + v.Build;
        }
        catch
        {
            // ignore
        }

        return "Hermes English Learning XP";
    }

    private Button MakeBtn(string text, int slot)
    {
        var b = new Button
        {
            Text = text,
            Width = 56,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(31, 111, 235),
            ForeColor = Color.White,
            Tag = slot,
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(42, 77, 143);
        return b;
    }

    private void LayoutChromeButtons()
    {
        const int btnCount = 6;
        const int btnW = 60;
        var right = _chrome.ClientSize.Width - 8;
        foreach (Control c in _chrome.Controls)
        {
            var b = c as Button;
            if (b == null) continue;
            var slot = (int)b.Tag;
            b.Location = new Point(right - (btnCount - slot) * btnW, 8);
        }

        _titleLabel.Width = Math.Max(80, _chrome.ClientSize.Width - btnCount * btnW - 20);
    }

    private void ShowLogWindow()
    {
        if (_logForm == null || _logForm.IsDisposed)
            _logForm = new LogForm { Owner = this };
        _logForm.Reload();
        _logForm.Show();
        _logForm.BringToFront();
    }

    private void RunNetworkDiagnostics()
    {
        _statusLabel.Text = "  Network diagnostics…";
        AppLog.Info("Manual network diagnostics requested");
        ShowLogWindow();
        NetworkDiagnostics.RunAsync(_settings, () =>
            BeginInvokeIfNeeded(() =>
            {
                _statusLabel.Text = "  Diagnostics finished — see Log";
                if (_logForm != null && !_logForm.IsDisposed)
                    _logForm.Reload();
            }));
    }

    private void AdjustScale(double delta)
    {
        var next = Math.Round(_scale + delta, 2);
        if (next < 0.6) next = 0.6;
        if (next > 2.5) next = 2.5;
        if (Math.Abs(next - _scale) < 0.001) return;
        _scale = next;
        _settings.UiScale = _scale;
        try
        {
            SettingsStore.Save(_settings);
            AppLog.Info("UiScale saved=" + _scale.ToString("0.00"));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Save UiScale: " + ex.Message);
        }

        UpdateScaleStatus();
        RenderCurrent();
    }

    private void ResetScale()
    {
        _scale = 1.0;
        _settings.UiScale = 1.0;
        try { SettingsStore.Save(_settings); } catch { /* ignore */ }
        AppLog.Info("UiScale reset=1.00");
        UpdateScaleStatus();
        RenderCurrent();
    }

    private void UpdateScaleStatus()
    {
        _progressLabel.Text = (_screens.Count == 0 ? "0 / 0" : ((_index + 1) + " / " + _screens.Count))
            + "  ×" + _scale.ToString("0.00") + "  ";
    }

    private float S(float baseSize) => (float)(baseSize * _scale);

    private int Si(int basePx) => Math.Max(1, (int)Math.Round(basePx * _scale));

    private void TryLoadSample()
    {
        try
        {
            var sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleLessons",
                "Official_Hermes_Docs_lesson.md");
            if (!File.Exists(sample))
            {
                sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleLessons",
                    "If_I_Had_a_Heart_lesson.md");
            }

            if (File.Exists(sample))
                LoadLesson(File.ReadAllText(sample, Encoding.UTF8), Path.GetFileNameWithoutExtension(sample));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Sample: " + ex.Message);
        }
    }

    private void OpenLocal()
    {
        using (var dlg = new OpenFileDialog
        {
            Filter = "Markdown (*.md)|*.md|All|*.*",
            InitialDirectory = SettingsStore.ResolveLessonsFolder(_settings),
        })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            LoadLesson(File.ReadAllText(dlg.FileName, Encoding.UTF8), Path.GetFileNameWithoutExtension(dlg.FileName));
        }
    }

    private void OnLesson(string markdown, string title)
    {
        BeginInvokeIfNeeded(() =>
        {
            try
            {
                var dir = SettingsStore.ResolveLessonsFolder(_settings);
                var path = Path.Combine(dir, Sanitize(title) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md");
                File.WriteAllText(path, markdown, Encoding.UTF8);
                AppLog.Info("Lesson saved: " + path);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Save lesson: " + ex.Message);
            }

            LoadLesson(markdown, title);
            Activate();
            BringToFront();
        });
    }

    private void OnNav(NavCommand cmd)
    {
        BeginInvokeIfNeeded(() =>
        {
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            var label = NavStatusText(cmd);
            _statusLabel.Text = "  [" + stamp + "] Команда: " + label;
            AppLog.Info("Apply nav: " + cmd);
            switch (cmd)
            {
                case NavCommand.FullScreen:
                    ToggleFullscreen();
                    break;
                case NavCommand.Next:
                    GoNext();
                    break;
                case NavCommand.Previous:
                    GoPrev();
                    break;
                case NavCommand.Exit:
                    Close();
                    break;
            }
        });
    }

    private static string NavStatusText(NavCommand cmd)
    {
        switch (cmd)
        {
            case NavCommand.FullScreen: return "fullscreen (полный экран)";
            case NavCommand.Next: return "next (следующий экран)";
            case NavCommand.Previous: return "previous (предыдущий экран)";
            case NavCommand.Exit: return "exit (выход)";
            default: return cmd.ToString();
        }
    }

    private void LoadLesson(string markdown, string titleHint)
    {
        var doc = LessonMarkdownParser.Parse(markdown);
        _screens = LessonPager.Build(doc, _settings);
        _index = 0;
        var title = !string.IsNullOrWhiteSpace(doc.TitleEn) ? doc.TitleEn : titleHint;
        _titleLabel.Text = title ?? "Lesson";
        RenderCurrent();
        _statusLabel.Text = "  Lesson loaded: " + _titleLabel.Text;
        PublishCurrentPageTts();
    }

    private void GoNext()
    {
        if (_screens.Count == 0) return;
        if (_index < _screens.Count - 1) _index++;
        RenderCurrent();
        PublishCurrentPageTts();
    }

    private void GoPrev()
    {
        if (_screens.Count == 0) return;
        if (_index > 0) _index--;
        RenderCurrent();
        PublishCurrentPageTts();
    }

    private void PublishCurrentPageTts()
    {
        if (_screens.Count == 0 || _index < 0 || _index >= _screens.Count) return;
        if (!_poller.IsConfigured)
        {
            AppLog.Warn("TTS publish skipped — Supabase not configured");
            return;
        }

        var screen = _screens[_index];
        _poller.PublishPageTtsAsync(screen, _index, _screens.Count, status =>
            BeginInvokeIfNeeded(() =>
            {
                var stamp = DateTime.Now.ToString("HH:mm:ss");
                _statusLabel.Text = "  [" + stamp + "] " + status;
            }));
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _prevBorder = FormBorderStyle;
            _prevState = WindowState;
            _prevBounds = Bounds;
            _chrome.Visible = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _fullscreen = true;
        }
        else
        {
            _chrome.Visible = true;
            FormBorderStyle = _prevBorder;
            WindowState = _prevState == FormWindowState.Maximized ? FormWindowState.Normal : _prevState;
            Bounds = _prevBounds;
            _fullscreen = false;
        }

        RenderCurrent();
    }

    private void RenderCurrent()
    {
        _cardHost.SuspendLayout();
        _cardHost.Controls.Clear();
        if (_screens.Count == 0)
        {
            UpdateScaleStatus();
            _cardHost.ResumeLayout();
            return;
        }

        if (_index < 0) _index = 0;
        if (_index >= _screens.Count) _index = _screens.Count - 1;
        var screen = _screens[_index];
        UpdateScaleStatus();

        var section = new Label
        {
            AutoSize = true,
            Text = screen.SectionLabel,
            ForeColor = Color.FromArgb(170, 178, 192),
            Font = new Font(Font.FontFamily, S(11f), FontStyle.Regular),
            Location = new Point(Si(24), Si(16)),
        };
        _cardHost.Controls.Add(section);

        var y = Si(48);
        var cols = Math.Max(1, screen.ColumnCount);
        var pad = Si(48);
        var usable = Math.Max(200, _cardHost.ClientSize.Width - pad);
        var colW = usable / cols;
        var rowH = EstimateRowHeight(screen);
        for (var i = 0; i < screen.Cards.Count; i++)
        {
            var card = screen.Cards[i];
            var col = i % cols;
            var row = i / cols;
            var x = Si(24) + col * colW;
            var top = y + row * rowH;
            AddCard(card, x, top, colW - Si(16));
        }

        _cardHost.ResumeLayout();
    }

    private int EstimateRowHeight(LessonScreen screen)
    {
        if (screen.Section == LessonSection.Lyrics) return Si(110);
        if (screen.Section == LessonSection.Phrases) return Si(90);
        return Si(72);
    }

    private void AddCard(CardPair card, int x, int y, int width)
    {
        var enH = Si(36);
        var ruH = Si(28);
        var en = new Label
        {
            AutoSize = false,
            Text = card.En,
            ForeColor = Color.FromArgb(248, 209, 47),
            Font = new Font(Font.FontFamily, S(18f), FontStyle.Bold),
            Location = new Point(x, y),
            Width = width,
            Height = enH,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        var ru = new Label
        {
            AutoSize = false,
            Text = card.Ru,
            ForeColor = Color.FromArgb(208, 212, 220),
            Font = new Font(Font.FontFamily, S(13f), FontStyle.Regular),
            Location = new Point(x, y + enH),
            Width = width,
            Height = ruH,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _cardHost.Controls.Add(en);
        _cardHost.Controls.Add(ru);
    }

    private void MainForm_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add))
        {
            AdjustScale(0.1);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract))
        {
            AdjustScale(-0.1);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.D0)
        {
            ResetScale();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.L)
        {
            ShowLogWindow();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Right || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.Space)
        {
            GoNext();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.PageUp)
        {
            GoPrev();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F || e.KeyCode == Keys.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            if (_fullscreen) ToggleFullscreen();
            else Close();
            e.Handled = true;
        }
    }

    private void BeginInvokeIfNeeded(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(a);
        else a();
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "lesson" : name.Trim();
    }
}
