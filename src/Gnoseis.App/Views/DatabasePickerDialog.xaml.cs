/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Views;

/// <summary>
/// The start-up picker ("Always ask for a database at start-up"): shown before any database is opened.
/// Open (or double-click, or Enter) opens the selected database without changing the default; Add folder
/// adds a folder to the list and selects it; Quit leaves <see cref="Chosen"/> null.
/// </summary>
public sealed partial class DatabasePickerDialog : ContentDialog
{
    public DatabasePickerDialog(KnownDatabases databases)
    {
        InitializeComponent();
        Chooser.CanOpenChanged += (_, _) => IsPrimaryButtonEnabled = Chooser.Openable != null;
        Chooser.OpenRequested += (_, _) =>
        {
            Chosen = Chooser.Openable;
            Hide();
        };
        Chooser.Show(databases, folderInUse: null);
        Opened += (_, _) => Chooser.FocusList();
    }

    /// <summary>The database to open; null when the user quit.</summary>
    public KnownDatabase? Chosen { get; private set; }

    private void DatabasePickerDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Chosen = Chooser.Openable;
        args.Cancel = Chosen == null;
    }

    private async void DatabasePickerDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Add folder keeps the picker open.
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try
        {
            await Chooser.AddFolderAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }
}
