using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hermes.EnglishLearning.Models;
using Hermes.EnglishLearning.Services;
using Microsoft.Win32;

namespace Hermes.EnglishLearning;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly LocalTtsService _tts = new();
    private readonly SupabaseLessonPoller _poller = new();
    private readonly DispatcherTimer _pollTimer;
    private CancellationTokenSource? _pollCts;

    private LessonDocument? _lesson;
    private IReadOnlyList<LessonScreen> _screens = Array.Empty<LessonScreen>();
    private int _index;
    private bool _rebuildQueued;

    private const double EnglishFontSize = 42;
    private const double RussianFontSize = 28;
    private const double CardSpacing = 28;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.PollSeconds)) };
        _pollTimer.Tick += async (_, __) => await PollTickAsync().ConfigureAwait(true);
        _poller.LessonReceived += OnLessonReceived;
        _poller.StatusChanged += s => Dispatcher.BeginInvoke(new Action(() => StatusText.Text = s));
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        var sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleLessons", "If_I_Had_a_Heart_lesson.md");
        if (!string.IsNullOrWhiteSpace(_settings.LastLocalLessonPath) && File.Exists(_settings.LastLocalLessonPath))
        {
            LoadLessonFromMarkdown(File.ReadAllText(_settings.LastLocalLessonPath), Path.GetFileName(_settings.LastLocalLessonPath));
        }
        else if (File.Exists(sample))
        {
            LoadLessonFromMarkdown(File.ReadAllText(sample), "If I Had a Heart");
        }

        if (_poller.IsConfigured(_settings))
        {
            StartPolling();
        }
    }

    private void Window_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_lesson == null)
        {
            return;
        }

        if (_rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _rebuildQueued = false;
            RebuildScreens(keepRelativeProgress: true);
        }), DispatcherPriority.Background);
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right || e.Key == Key.Space || e.Key == Key.PageDown)
        {
            GoNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Left || e.Key == Key.PageUp || e.Key == Key.Back)
        {
            GoPrev();
            e.Handled = true;
        }
        else if (e.Key == Key.S)
        {
            SpeakCurrent();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _tts.Stop();
            e.Handled = true;
        }
    }

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
            Title = "Открыть урок (MD)",
        };
        if (!string.IsNullOrWhiteSpace(_settings.LastLocalLessonPath))
        {
            dlg.InitialDirectory = Path.GetDirectoryName(_settings.LastLocalLessonPath);
        }

        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        _settings.LastLocalLessonPath = dlg.FileName;
        SettingsStore.Save(_settings);
        LoadLessonFromMarkdown(File.ReadAllText(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName));
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings) { Owner = this };
        if (win.ShowDialog() == true)
        {
            SettingsStore.Save(_settings);
            _pollTimer.Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.PollSeconds));
            if (_poller.IsConfigured(_settings))
            {
                StartPolling();
            }
            else
            {
                StopPolling();
                StatusText.Text = "Supabase не настроен.";
            }
        }
    }

    private void SpeakButton_OnClick(object sender, RoutedEventArgs e) => SpeakCurrent();

    private void PrevButton_OnClick(object sender, RoutedEventArgs e) => GoPrev();

    private void NextButton_OnClick(object sender, RoutedEventArgs e) => GoNext();

    private void LoadLessonFromMarkdown(string markdown, string titleHint)
    {
        try
        {
            _lesson = LessonMarkdownParser.Parse(markdown);
            if (string.IsNullOrWhiteSpace(_lesson.TitleEn))
            {
                _lesson.TitleEn = titleHint;
            }

            RebuildScreens(keepRelativeProgress: false);
            StatusText.Text = "Урок: " + (_lesson.TitleEn ?? titleHint);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка разбора MD", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RebuildScreens(bool keepRelativeProgress)
    {
        if (_lesson == null)
        {
            return;
        }

        var ratio = _screens.Count == 0 ? 0.0 : (double)_index / _screens.Count;
        var size = new Size(
            Math.Max(200, CardHost.ActualWidth > 0 ? CardHost.ActualWidth : ActualWidth - 40),
            Math.Max(200, CardHost.ActualHeight > 0 ? CardHost.ActualHeight : ActualHeight - 120));

        _screens = LessonPager.BuildScreens(
            _lesson,
            size,
            EnglishFontSize,
            RussianFontSize,
            CardSpacing,
            CardHost.Padding);

        if (_screens.Count == 0)
        {
            CardsPanel.Children.Clear();
            ProgressText.Text = "Пустой урок";
            return;
        }

        if (keepRelativeProgress)
        {
            _index = Math.Min(_screens.Count - 1, Math.Max(0, (int)Math.Round(ratio * _screens.Count)));
        }
        else
        {
            _index = 0;
        }

        ShowCurrentScreen();
    }

    private void ShowCurrentScreen()
    {
        if (_screens.Count == 0)
        {
            return;
        }

        _index = Math.Max(0, Math.Min(_index, _screens.Count - 1));
        var screen = _screens[_index];
        ProgressText.Text = LessonPager.FormatProgress(screen, _index, _screens.Count);

        CardsPanel.Children.Clear();
        for (var i = 0; i < screen.Cards.Count; i++)
        {
            var card = screen.Cards[i];
            var block = CreateCardVisual(card, screen.Section);
            if (i > 0 && block is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, CardSpacing, 0, 0);
            }

            CardsPanel.Children.Add(block);
        }

        if (_settings.AutoSpeak)
        {
            SpeakCurrent();
        }
    }

    private UIElement CreateCardVisual(CardPair card, LessonSection section)
    {
        var enSize = section == LessonSection.Words ? EnglishFontSize * 0.92 : EnglishFontSize;
        var ruSize = section == LessonSection.Words ? RussianFontSize * 0.92 : RussianFontSize;

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = card.En,
            FontSize = enSize,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = (Brush)FindResource("EnglishTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(card.Ru))
        {
            stack.Children.Add(new TextBlock
            {
                Text = card.Ru,
                FontSize = ruSize,
                FontWeight = FontWeights.Normal,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = (Brush)FindResource("RussianTextBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            });
        }

        return stack;
    }

    private void GoNext()
    {
        if (_index + 1 < _screens.Count)
        {
            _index++;
            ShowCurrentScreen();
        }
    }

    private void GoPrev()
    {
        if (_index > 0)
        {
            _index--;
            ShowCurrentScreen();
        }
    }

    private void SpeakCurrent()
    {
        if (_screens.Count == 0)
        {
            return;
        }

        _tts.SpeakScreen(_screens[_index]);
    }

    private void OnLessonReceived(string markdown, string title)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HermesEnglishLearning",
                "lessons");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, SanitizeFileName(title) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md");
            File.WriteAllText(path, markdown);
            _settings.LastLocalLessonPath = path;
            SettingsStore.Save(_settings);
            LoadLessonFromMarkdown(markdown, title);
        }));
    }

    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        _pollTimer.Start();
        StatusText.Text = "Supabase poll started…";
        _ = PollTickAsync();
    }

    private void StopPolling()
    {
        _pollTimer.Stop();
        _pollCts?.Cancel();
        _pollCts = null;
    }

    private async Task PollTickAsync()
    {
        if (!_poller.IsConfigured(_settings))
        {
            return;
        }

        var ct = _pollCts?.Token ?? CancellationToken.None;
        try
        {
            await _poller.PollOnceAsync(_settings, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            StatusText.Text = "Supabase: " + ex.Message;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "lesson" : name.Trim();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPolling();
        _tts.Dispose();
        _poller.Dispose();
        base.OnClosed(e);
    }
}
