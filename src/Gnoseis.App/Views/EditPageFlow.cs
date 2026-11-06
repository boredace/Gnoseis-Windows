/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Views;

/// <summary>Load / save / cancel behaviour of the edit page, which opens in a section's detail pane.</summary>
internal static class EditPageFlow
{
    /// <summary>Loads the record; if it no longer exists, tells the user and closes the editor.</summary>
    public static async Task LoadAsync(EditViewModelBase viewModel)
    {
        if (!await viewModel.LoadAsync())
        {
            await Dialogs.ShowMessageAsync($"{viewModel.Type.DisplayName()} not found",
                $"This {viewModel.Type.DisplayName().ToLowerInvariant()} no longer exists. It may have been deleted.");
            await Navigator.CloseEditorAsync(viewModel.Request, null);
        }
    }

    /// <summary>
    /// Saves and closes the editor: a new record is then selected and shown, a record created from
    /// another record returns to that record, and an edited record shows its updated details.
    /// </summary>
    public static async Task SaveAndCloseAsync(EditViewModelBase viewModel)
    {
        var saved = await TrySaveAsync(viewModel);
        if (saved != null)
        {
            await Navigator.CloseEditorAsync(viewModel.Request, saved);
        }
    }

    /// <summary>Cancel: asks about unsaved changes, then closes the editor.</summary>
    public static async Task CancelAsync(EditViewModelBase viewModel)
    {
        if (await CanLeaveAsync(viewModel))
        {
            await Navigator.CloseEditorAsync(viewModel.Request, null);
        }
    }

    /// <summary>Asks whether to save unsaved changes. Returns false if the user wants to stay.</summary>
    public static async Task<bool> CanLeaveAsync(EditViewModelBase viewModel)
    {
        if (viewModel.IsSaving)
        {
            return false;
        }
        if (!viewModel.IsDirty)
        {
            return true;
        }

        return await AskToLeaveAsync(viewModel.Type, viewModel.IsValid, async () => await TrySaveAsync(viewModel) != null);
    }

    /// <summary>
    /// The unsaved-changes prompt: save (when the record is valid), don't save, or stay. Returns
    /// false if the user wants to stay or the save failed.
    /// </summary>
    public static async Task<bool> AskToLeaveAsync(RecordType type, bool isValid, Func<Task<bool>> save)
    {
        var name = type.DisplayName().ToLowerInvariant();
        if (!isValid)
        {
            return await Dialogs.ConfirmAsync("Discard changes?",
                $"This {name} can't be saved yet because required information is missing.",
                "Discard", closeButtonText: "Keep editing");
        }

        var choice = await Dialogs.AskAsync("Save changes?",
            $"Do you want to save your changes to this {name}?",
            "Save", "Don't save", "Cancel");
        return choice switch
        {
            ContentDialogResult.Primary => await save(),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    private static async Task<RecordRef?> TrySaveAsync(EditViewModelBase viewModel)
    {
        try
        {
            return await viewModel.SaveAsync();
        }
        catch (Exception e)
        {
            await Dialogs.ShowErrorAsync("Couldn't save", e);
            return null;
        }
    }
}
