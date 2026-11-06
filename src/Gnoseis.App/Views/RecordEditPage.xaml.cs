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

/// <summary>The fields of one record type, shown inside <see cref="RecordEditPage"/>.</summary>
public interface IEditFields
{
    /// <summary>Moves keyboard focus to the first field.</summary>
    void FocusFirst();

    /// <summary>Moves keyboard focus to the field a search prefilled, with the caret at the end.</summary>
    void FocusPrefilled();
}

/// <summary>
/// New / edit record, for every type: the header with Save and Cancel, then the type's fields.
/// Load, save, cancel and the unsaved-changes prompt are in <see cref="EditPageFlow"/>.
/// </summary>
public sealed partial class RecordEditPage : Page, IConfirmLeave
{
    /// <summary>Below this width Save and Cancel go under the title.</summary>
    private const double StackedHeaderWidth = 520;

    private IEditFields? _fields;

    public RecordEditPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (ViewModel.Request.FocusField is { } field && _fields is ContactFields contactFields)
            {
                contactFields.FocusField(field);
            }
            else if (ViewModel.IsPrefilled)
            {
                _fields?.FocusPrefilled();
            }
            else
            {
                _fields?.FocusFirst();
            }
        };
    }

    public EditViewModelBase ViewModel { get; private set; } = null!;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var request = (EditRequest)e.Parameter;
        (ViewModel, var fields) = request.Type switch
        {
            RecordType.Note => Create(new NoteEditViewModel(request), vm => new NoteFields(vm)),
            RecordType.Contact => Create(new ContactEditViewModel(request), vm => new ContactFields(vm)),
            _ => Create(new NamedRecordEditViewModel(request), vm => new NamedRecordFields(vm)),
        };
        _fields = (IEditFields)fields;
        FieldsPresenter.Content = fields;
        await EditPageFlow.LoadAsync(ViewModel);
    }

    private static (EditViewModelBase, UIElement) Create<T>(T viewModel, Func<T, UIElement> fields) where T : EditViewModelBase =>
        (viewModel, fields(viewModel));

    public Task<bool> CanLeaveAsync() => EditPageFlow.CanLeaveAsync(ViewModel);

    private async void Save_Click(object sender, RoutedEventArgs e) => await EditPageFlow.SaveAndCloseAsync(ViewModel);

    private async void Cancel_Click(object sender, RoutedEventArgs e) => await EditPageFlow.CancelAsync(ViewModel);

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < StackedHeaderWidth;
        Grid.SetRow(Commands, stacked ? 1 : 0);
        Grid.SetColumn(Commands, stacked ? 0 : 1);
    }
}
