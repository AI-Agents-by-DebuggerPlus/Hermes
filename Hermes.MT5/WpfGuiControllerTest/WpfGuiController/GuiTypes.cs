namespace HermesWpfGuiController
{
    /// <summary>
    /// Тип события GUI. Значения должны совпадать с одноимённым enum в MQL5
    /// (int-эквивалент — единственное, что видит MQL5 через #import).
    /// </summary>
    public enum GuiEventType
    {
        Exception = 0,
        ClickOnElement = 1,
        TextChange = 2,
        CheckBoxChange = 3,
        ComboBoxChange = 4,
        SliderChange = 5,
        ElementEnable = 6,
        ElementHide = 7,
        SelectionChange = 8
    }

    /// <summary>
    /// Контейнер одного события, кладётся в очередь конкретного окна.
    /// </summary>
    internal sealed class GuiEvent
    {
        public string ElementName;
        public GuiEventType Id;
        public long LParam;
        public double DParam;
        public string SParam;
    }
}
