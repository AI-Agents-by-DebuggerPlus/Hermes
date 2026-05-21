using System.Windows.Controls;
using System.Windows.Input;
using Hermes.Wpf.ViewModels;

namespace Hermes.Wpf.Views;

public partial class AgentSkillsView
{
    public AgentSkillsView()
    {
        InitializeComponent();
    }

    private void GeneratedSkillItem_OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem { Content: GeneratedSkillListItem item })
        {
            return;
        }

        if (DataContext is MainViewModel vm && vm.GeneratedSkills.RunSkillCommand.CanExecute(item))
        {
            vm.GeneratedSkills.RunSkillCommand.Execute(item);
        }
    }
}
