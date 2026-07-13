using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace Hermes.EnglishLearning;

public partial class LessonLibraryWindow : Window
{
    public string? SelectedPath { get; private set; }

    public LessonLibraryWindow()
    {
        InitializeComponent();
        RefreshList();
    }

    public static IEnumerable<string> EnumerateLessonPaths()
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        foreach (var dir in new[]
                 {
                     Path.Combine(root, "SampleLessons"),
                     Path.Combine(root, "lessons"),
                 })
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var f in Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
            {
                yield return f;
            }
        }
    }

    private void RefreshList()
    {
        var items = EnumerateLessonPaths()
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(p => new LessonFileItem(p))
            .ToList();
        FilesList.ItemsSource = items;
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e) => RefreshList();

    private void Open_OnClick(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is LessonFileItem item)
        {
            SelectedPath = item.FullPath;
            DialogResult = true;
            Close();
        }
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = FilesList.SelectedItems.Cast<LessonFileItem>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var msg = selected.Count == 1
            ? "Удалить файл?\n" + selected[0].Display
            : "Удалить выбранные файлы (" + selected.Count + ")?";
        if (MessageBox.Show(this, msg, "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var item in selected)
        {
            try
            {
                // Do not delete sample lessons in SampleLessons without confirm already done —
                // allow delete from both folders as user requested.
                File.Delete(item.FullPath);
                Services.AppLog.Info("Deleted lesson: " + item.FullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        RefreshList();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed class LessonFileItem
    {
        public LessonFileItem(string path)
        {
            FullPath = path;
            var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
            Display = folder + " / " + Path.GetFileName(path);
        }

        public string FullPath { get; }
        public string Display { get; }
    }
}
