using System.Windows.Controls;
using Hermes.TradingPlatform.Shared.Mock;

namespace Hermes.TradingPlatform.Wpf.Controls;

public partial class PnlCard : UserControl
{
    public PnlCard() => InitializeComponent();

    public void Bind(PnlSummaryDto pnl) => DataContext = pnl;
}
