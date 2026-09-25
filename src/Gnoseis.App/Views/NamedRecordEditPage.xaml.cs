/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gnoseis.App.Views;

/// <summary>New / edit organization, category or item.</summary>
public sealed partial class NamedRecordEditPage : Page, IConfirmLeave
{
    public NamedRecordEditPage()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.FocusText();
    }

    public NamedRecordEditViewModel ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = new NamedRecordEditViewModel((EditRequest)e.Parameter);
        await EditPageFlow.LoadAsync(ViewModel);
    }

    public Task<bool> CanLeaveAsync() => EditPageFlow.CanLeaveAsync(ViewModel);

    private async void Save_Click(object sender, RoutedEventArgs e) => await EditPageFlow.SaveAndCloseAsync(ViewModel);

    private async void Cancel_Click(object sender, RoutedEventArgs e) => await EditPageFlow.CancelAsync(ViewModel);
}
