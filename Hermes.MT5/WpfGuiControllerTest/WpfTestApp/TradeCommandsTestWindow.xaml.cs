using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WpfTestApp
{
    public partial class TradeCommandsTestWindow : Window
    {
        private sealed class CmdItem
        {
            public string Title { get; set; }
            public string Prompt { get; set; }
            public override string ToString() => Title + " — " + Prompt;
        }

        public TradeCommandsTestWindow()
        {
            InitializeComponent();
            Title = "Trade Commands → agent " + BuildInfo.Version;
            txtIpc.Text = "inject: " + Path.Combine(TerminalAgentIpc.ResolveIpcDir(), "agent_chat_inject.json");

            var items = new List<CmdItem>
            {
                new CmdItem { Title = "Скриншот графика", Prompt = "Сделай скриншот графика" },
                new CmdItem { Title = "Статус и позиции", Prompt = "Покажи статус терминала и открытые позиции" },
                new CmdItem { Title = "Баланс счёта", Prompt = "Какой сейчас баланс счёта?" },
                new CmdItem { Title = "Цена инструмента", Prompt = "Цена активного инструмента?" },
                new CmdItem { Title = "Real trading ON", Prompt = "Включи real trading" },
                new CmdItem { Title = "Real trading OFF", Prompt = "Выключи real trading" },
                new CmdItem { Title = "Лонг по маркету", Prompt = "Открой лонг по маркету лотом 0.01" },
                new CmdItem { Title = "Шорт по маркету", Prompt = "Открой шорт по маркету лотом 0.01" },
                new CmdItem { Title = "Закрой все", Prompt = "Закрой все позиции" },
                new CmdItem { Title = "Закрой слот 0", Prompt = "Закрой позицию в слоте 0" },
                new CmdItem { Title = "Лот 0.01", Prompt = "Установи лот 0.01" },
                new CmdItem { Title = "RemoteTerminal", Prompt = "Обнови RemoteTerminal" },
                new CmdItem { Title = "Список символов", Prompt = "Получи список символов MT5" },
                new CmdItem { Title = "Sell Limit XAUUSD", Prompt = "Поставь Sell Limit по XAUUSD: цена 4490, стоп 4505, тейк 4450, лот 0.01" },
            };
            lstCommands.ItemsSource = items;
            lstCommands.DisplayMemberPath = "Title";
            if (items.Count > 0)
                lstCommands.SelectedIndex = 0;
        }

        private void LstCommands_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => SendSelected();

        private void BtnSend_OnClick(object sender, RoutedEventArgs e) => SendSelected();

        private void SendSelected()
        {
            var custom = (txtCustom.Text ?? "").Trim();
            string prompt;
            if (custom.Length > 0)
            {
                prompt = custom;
            }
            else if (lstCommands.SelectedItem is CmdItem item)
            {
                prompt = item.Prompt;
            }
            else
            {
                SetStatus("Выберите команду или введите свою.", warn: true);
                return;
            }

            try
            {
                WriteInject(prompt);
                SetStatus("Отправлено агенту. Смотрите чат Hermes.Wpf (проект Mt5Terminal).", ok: true);
                txtCustom.Clear();
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка: " + ex.Message, warn: true);
            }
        }

        private void SetStatus(string text, bool ok = false, bool warn = false)
        {
            txtStatus.Text = text;
            if (ok)
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x43, 0xA0, 0x47));
            else if (warn)
                txtStatus.Foreground = System.Windows.Media.Brushes.Orange;
            else
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x84, 0x8E, 0x9C));
        }

        private static void WriteInject(string text)
        {
            var dir = TerminalAgentIpc.ResolveIpcDir();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "agent_chat_inject.json");
            var id = Guid.NewGuid().ToString("N");
            var escaped = EscapeJson(text);
            var json = "{\"id\":\"" + id + "\",\"text\":\"" + escaped
                       + "\",\"source\":\"hwt\",\"utc\":\"" + DateTime.UtcNow.ToString("o") + "\"}";
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
