using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace HermesWpfGuiController
{
    /// <summary>
    /// Управляет одним WPF-окном в собственном STA-потоке: загружает сборку,
    /// поднимает окно, подписывается на события контролов и хранит их
    /// в приватной (per-window, не глобальной) потокобезопасной очереди.
    /// </summary>
    internal sealed class WpfWindowController : IDisposable
    {
        private readonly object _queueLock = new object();
        private readonly List<GuiEvent> _events = new List<GuiEvent>();
        private readonly Dictionary<string, FrameworkElement> _elements =
            new Dictionary<string, FrameworkElement>();
        // Элементы, которые сейчас обновляются программно из SendEvent —
        // подавляем эхо-событие, чтобы не зациклить MQL5 <-> WPF.
        private readonly HashSet<string> _suppressed = new HashSet<string>();

        private Thread _uiThread;
        private Dispatcher _dispatcher;
        private Window _window;
        private volatile bool _disposed;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private Exception _startupException;

        public bool IsDisposed => _disposed;

        public void Start(string assemblyPath, string windowTypeName)
        {
            _uiThread = new Thread(() => RunWindow(assemblyPath, windowTypeName))
            {
                IsBackground = true
            };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();

            // Ждём либо успешной загрузки окна, либо исключения из потока UI.
            if (!_ready.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("WPF window did not initialize in time: " + windowTypeName);

            if (_startupException != null)
                throw _startupException;
        }

        private void RunWindow(string assemblyPath, string windowTypeName)
        {
            try
            {
                // WPF-ресурсы (BAML/pack URI) требуют существования Application.Current.
                // Создаём только если ещё не создан — в одном процессе может открываться
                // несколько окон, второй Application бросит исключение.
                if (Application.Current == null)
                {
                    new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }

                if (!System.IO.File.Exists(assemblyPath))
                    throw new System.IO.FileNotFoundException(
                        "WPF UI DLL not found. Fix EA input path.", assemblyPath);

                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                Type windowType = FindWindowType(assembly, windowTypeName);

                _window = (Window)Activator.CreateInstance(windowType);
                _dispatcher = _window.Dispatcher;

                _window.Loaded += (s, e) =>
                {
                    SubscribeOnElements(_window);
                    SubscribeOnVisualTree(_window);
                    _ready.Set();
                };

                _window.Closed += (s, e) =>
                {
                    _disposed = true;
                    _dispatcher.InvokeShutdown();
                };

                _window.Show();
                Dispatcher.Run(); // собственный цикл сообщений для этого потока
            }
            catch (Exception ex)
            {
                _startupException = ex;
                _disposed = true;
                try
                {
                    MessageBox.Show(
                        ex.ToString(),
                        "HermesWpfGuiController — failed to load UI",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { /* no UI thread yet */ }
                _ready.Set();
            }
        }

        // IsAssignableFrom, а не BaseType == typeof(Window) — иначе не находятся
        // окна с промежуточным базовым классом (BaseWindow : Window, MyWindow : BaseWindow).
        private static Type FindWindowType(Assembly assembly, string windowTypeName)
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (typeof(Window).IsAssignableFrom(type) && type.Name == windowTypeName)
                    return type;
            }
            throw new Exception($"Window '{windowTypeName}' not found in assembly {assembly.FullName}");
        }

        private void SubscribeOnElements(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is FrameworkElement element)
                {
                    RegisterHandlers(element);

                    if (!string.IsNullOrEmpty(element.Name))
                        _elements[element.Name] = element;

                    SubscribeOnElements(element); // WPF почти всегда вкладывает контролы в контейнеры
                }
            }
        }

        /// <summary>
        /// Дополнительный обход VisualTree: подхватывает x:Name, если LogicalTree
        /// по какой-то причине не отдал элемент (редко, но для цен критично).
        /// Обработчики здесь не вешаем повторно — только словарь имён.
        /// </summary>
        private void SubscribeOnVisualTree(DependencyObject root)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement element
                    && !string.IsNullOrEmpty(element.Name)
                    && !_elements.ContainsKey(element.Name))
                {
                    _elements[element.Name] = element;
                }

                SubscribeOnVisualTree(child);
            }
        }

        private void RegisterHandlers(FrameworkElement element)
        {
            switch (element)
            {
                // CheckBox : ToggleButton : ButtonBase — более специфичный тип
                // должен быть выше ButtonBase, иначе case недостижим (CS8120).
                case CheckBox checkBox:
                    checkBox.Checked += (s, e) =>
                    {
                        if (IsSuppressed(checkBox.Name)) return;
                        PushEvent(checkBox.Name, GuiEventType.CheckBoxChange, 1, 0, null);
                    };
                    checkBox.Unchecked += (s, e) =>
                    {
                        if (IsSuppressed(checkBox.Name)) return;
                        PushEvent(checkBox.Name, GuiEventType.CheckBoxChange, 0, 0, null);
                    };
                    break;

                case ButtonBase button:
                    button.Click += (s, e) => PushEvent(button.Name, GuiEventType.ClickOnElement, 0, 0, null);
                    break;

                case TextBox textBox:
                    textBox.TextChanged += (s, e) =>
                    {
                        if (IsSuppressed(textBox.Name)) return;
                        PushEvent(textBox.Name, GuiEventType.TextChange, 0, 0, textBox.Text);
                    };
                    break;

                case ComboBox comboBox:
                    comboBox.SelectionChanged += (s, e) =>
                    {
                        if (IsSuppressed(comboBox.Name)) return;
                        PushEvent(comboBox.Name, GuiEventType.ComboBoxChange, comboBox.SelectedIndex, 0,
                            comboBox.SelectedItem?.ToString());
                    };
                    break;

                case Slider slider:
                    slider.ValueChanged += (s, e) =>
                    {
                        if (IsSuppressed(slider.Name)) return;
                        PushEvent(slider.Name, GuiEventType.SliderChange, 0, slider.Value, null);
                    };
                    break;

                case ListBox listBox:
                    listBox.SelectionChanged += (s, e) =>
                    {
                        if (IsSuppressed(listBox.Name)) return;
                        PushEvent(listBox.Name, GuiEventType.SelectionChange, listBox.SelectedIndex, 0,
                            listBox.SelectedItem?.ToString());
                    };
                    break;
            }
        }

        private bool IsSuppressed(string name)
        {
            lock (_queueLock) { return _suppressed.Contains(name); }
        }

        private void PushEvent(string elementName, GuiEventType id, long lparam, double dparam, string sparam)
        {
            lock (_queueLock)
            {
                _events.Add(new GuiEvent
                {
                    ElementName = elementName,
                    Id = id,
                    LParam = lparam,
                    DParam = dparam,
                    SParam = sparam
                });
            }
        }

        public int EventsTotal()
        {
            lock (_queueLock) { return _events.Count; }
        }

        public bool TryGetEvent(int index, out string elName, out int id, out long lparam, out double dparam, out string sparam)
        {
            lock (_queueLock)
            {
                if (index < 0 || index >= _events.Count)
                {
                    elName = null; id = 0; lparam = 0; dparam = 0; sparam = null;
                    return false;
                }
                GuiEvent e = _events[index];
                elName = e.ElementName;
                id = (int)e.Id;
                lparam = e.LParam;
                dparam = e.DParam;
                sparam = e.SParam;
                return true;
            }
        }

        public void ClearEvents()
        {
            lock (_queueLock) { _events.Clear(); }
        }

        /// <summary>
        /// Применяет изменение к элементу. В отличие от оригинального MtGuiController,
        /// ЛЮБАЯ мутация UI всегда идёт через Dispatcher.Invoke — там были ветки без
        /// маршалинга (ComboBox/Numeric/DateTime), что при вызове не из UI-потока
        /// нарушает потокобезопасность WinForms/WPF.
        /// </summary>
        public bool SendEvent(string elementName, GuiEventType id, long lparam, double dparam, string sparam)
        {
            if (_disposed || _dispatcher == null) return false;
            if (!_elements.TryGetValue(elementName, out FrameworkElement el)) return false;

            _dispatcher.Invoke(() =>
            {
                Suppress(elementName, true);
                try
                {
                    switch (id)
                    {
                        case GuiEventType.TextChange:
                            // Не полагаемся только на `is TextBlock`/`is TextBox`:
                            // при Assembly.LoadFrom типы WPF из разных контекстов
                            // иногда не совпадают → `is` даёт false и цена не пишется.
                            ApplyText(el, sparam);
                            break;

                        case GuiEventType.CheckBoxChange:
                            if (el is CheckBox cb) cb.IsChecked = lparam != 0;
                            else
                            {
                                var prop = el.GetType().GetProperty("IsChecked");
                                if (prop != null && prop.CanWrite)
                                    prop.SetValue(el, lparam != 0);
                            }
                            break;

                        case GuiEventType.ComboBoxChange:
                            if (el is ComboBox combo && combo.SelectedIndex != (int)lparam)
                                combo.SelectedIndex = (int)lparam;
                            break;

                        case GuiEventType.SliderChange:
                            if (el is Slider sl) sl.Value = dparam;
                            break;

                        case GuiEventType.ElementEnable:
                            el.IsEnabled = lparam != 0;
                            break;

                        case GuiEventType.ElementHide:
                            el.Visibility = lparam != 0 ? Visibility.Collapsed : Visibility.Visible;
                            break;
                    }
                }
                finally
                {
                    Suppress(elementName, false);
                }
            });
            return true;
        }

        private static void ApplyText(FrameworkElement el, string sparam)
        {
            var text = sparam ?? string.Empty;

            if (el is TextBox tb)
            {
                tb.Text = text;
                return;
            }

            if (el is TextBlock tblk)
            {
                tblk.Text = text;
                return;
            }

            if (el is Label lbl)
            {
                lbl.Content = text;
                return;
            }

            var textProp = el.GetType().GetProperty("Text");
            if (textProp != null && textProp.CanWrite && textProp.PropertyType == typeof(string))
            {
                textProp.SetValue(el, text);
                return;
            }

            var contentProp = el.GetType().GetProperty("Content");
            if (contentProp != null && contentProp.CanWrite)
                contentProp.SetValue(el, text);
        }

        private void Suppress(string name, bool value)
        {
            lock (_queueLock)
            {
                if (value) _suppressed.Add(name);
                else _suppressed.Remove(name);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _dispatcher?.Invoke(() => _window?.Close());
            }
            catch { /* поток мог уже завершиться */ }
        }
    }
}
