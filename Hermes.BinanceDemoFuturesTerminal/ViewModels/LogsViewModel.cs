using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Hermes.BinanceDemoFuturesTerminal.MVVM;
using Hermes.BinanceDemoFuturesTerminal.Services;

namespace Hermes.BinanceDemoFuturesTerminal.ViewModels;

public sealed class LogsViewModel : ObservableObject
{
    public LogsViewModel()
    {
        Entries = AppServices.Log.Entries;
        LogFilePath = AppServices.Log.SessionFilePath;
        ClearViewCommand = new RelayCommand(ClearView);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        CopyPathCommand = new RelayCommand(CopyPath);
    }

    public ObservableCollection<string> Entries { get; }

    public string LogFilePath { get; }

    public ICommand ClearViewCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand CopyPathCommand { get; }

    private void ClearView()
    {
        AppServices.Log.ClearView();
        AppServices.Log.Info("Журнал UI очищен (файл сессии сохранён).");
    }

    private void OpenFolder()
    {
        Process.Start(new ProcessStartInfo(TerminalPaths.LogsDirectory) { UseShellExecute = true });
    }

    private void CopyPath()
    {
        Clipboard.SetText(LogFilePath);
    }
}
