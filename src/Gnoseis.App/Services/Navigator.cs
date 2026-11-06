/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.App.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Gnoseis.App.Services;

/// <summary>
/// Navigation between the app's pages. Each record type has a section page (list + record);
/// a record opened from a record page is added to the section's trail of opened records.
/// </summary>
public static class Navigator
{
    private static Frame? _frame;

    private static Frame Frame => _frame ?? throw new InvalidOperationException("Navigator is not initialized.");

    public static void Initialize(Frame frame) => _frame = frame;

    /// <summary>The section page currently shown, if any.</summary>
    public static ISectionHost? ActiveSection { get; set; }

    /// <summary>Parameter of the page currently shown.</summary>
    public static object? CurrentParameter { get; set; }

    public static bool CanGoBack => _frame?.CanGoBack == true;

    /// <summary>True when the Back button has something to do (previous page, or back to the list).</summary>
    public static bool CanGoBackAnywhere => CanGoBack || ActiveSection?.CanHandleBack == true;

    /// <summary>Raised when <see cref="CanGoBackAnywhere"/> may have changed within a page.</summary>
    public static event EventHandler? BackStateChanged;

    public static void NotifyBackStateChanged() => BackStateChanged?.Invoke(null, EventArgs.Empty);

    public static Task ShowHomeAsync() => NavigateAsync(typeof(HomePage), null, new SuppressNavigationTransitionInfo());

    /// <summary>Shows a section, optionally with a record selected or an editor open.</summary>
    public static async Task ShowSectionAsync(RecordType type, string? selectedId = null, EditRequest? edit = null)
    {
        if (ActiveSection is { } section && section.Type == type && Frame.Content is SectionPage)
        {
            if (edit != null)
            {
                await section.ShowEditorAsync(edit);
            }
            else if (selectedId != null)
            {
                await section.ShowRecordAsync(selectedId);
            }
            else
            {
                // The section already shown was picked again: back to its list.
                await section.CloseTrailAsync();
            }
            return;
        }

        var transition = selectedId != null
            ? new DrillInNavigationTransitionInfo()
            : (NavigationTransitionInfo)new SuppressNavigationTransitionInfo();
        await NavigateAsync(typeof(SectionPage), new SectionArgs(type, selectedId, edit), transition);
    }

    /// <summary>Opens a record in its own section, with it selected in the list (search, Home, Show in … list).</summary>
    public static Task OpenRecordAsync(RecordRef record) => ShowSectionAsync(record.Type, record.Id);

    /// <summary>
    /// Opens a record from a link on a record page: the section stays as it is and the record is added to
    /// its trail of opened records. Outside a section the record opens in its own section.
    /// </summary>
    public static Task OpenLinkedRecordAsync(RecordRef record) =>
        ActiveSection != null && Frame.Content is SectionPage
            ? ActiveSection.OpenLinkedRecordAsync(record)
            : OpenRecordAsync(record);

    /// <summary>The trail of records opened from one another in the section shown, if one is open.</summary>
    public static Gnoseis.Core.Domain.RecordTrail<RecordRef>? ActiveTrail =>
        ActiveSection != null && Frame.Content is SectionPage ? ActiveSection.Trail : null;

    /// <summary>Opens an editor: in the current section's detail pane, or in the record type's section.</summary>
    public static Task ShowEditorAsync(EditRequest request) =>
        ActiveSection != null && Frame.Content is SectionPage
            ? ActiveSection.ShowEditorAsync(request)
            : ShowSectionAsync(request.Type, edit: request);

    public static Task CloseEditorAsync(EditRequest request, RecordRef? saved) =>
        ActiveSection?.CloseEditorAsync(request, saved) ?? Task.CompletedTask;

    public static void RecordDeleted(string id) => ActiveSection?.OnRecordDeleted(id);

    /// <summary>Shows the search page with a query, and from a section with that type's chip selected.</summary>
    public static Task ShowSearchAsync(string query, RecordType? scope = null) =>
        NavigateAsync(typeof(SearchPage), new SearchArgs(query, scope), new EntranceNavigationTransitionInfo(), allowSamePage: true);

    public static Task ShowSettingsAsync() =>
        NavigateAsync(typeof(SettingsPage), null, new SuppressNavigationTransitionInfo());

    /// <summary>Forward: one step forward along the section's trail of opened records, if there is one.</summary>
    public static async Task GoForwardAsync()
    {
        if (ActiveSection != null && Frame.Content is SectionPage)
        {
            await ActiveSection.TryGoForwardAsync();
        }
    }

    /// <summary>Back: first along the section's trail, then within the section (narrow window), then to the previous page.</summary>
    public static async Task GoBackAsync()
    {
        if (ActiveSection != null && Frame.Content is SectionPage && await ActiveSection.TryHandleBackAsync())
        {
            return;
        }
        if (!CanGoBack)
        {
            return;
        }
        if (Frame.Content is IConfirmLeave page && !await page.CanLeaveAsync())
        {
            return;
        }
        Frame.GoBack();
    }

    /// <summary>Shows Home with an empty back stack (start-up, and after the database was replaced).</summary>
    public static void ResetTo(Type pageType, object? parameter)
    {
        Frame.Navigate(pageType, parameter, new SuppressNavigationTransitionInfo());
        Frame.BackStack.Clear();
    }

    public static void ResetToHome() => ResetTo(typeof(HomePage), null);

    private static async Task NavigateAsync(Type pageType, object? parameter, NavigationTransitionInfo transition, bool allowSamePage = false)
    {
        if (!allowSamePage && Frame.CurrentSourcePageType == pageType && Equals(CurrentParameter, parameter))
        {
            return;
        }
        if (Frame.Content is IConfirmLeave page && !await page.CanLeaveAsync())
        {
            return;
        }
        Frame.Navigate(pageType, parameter, transition);
    }
}
