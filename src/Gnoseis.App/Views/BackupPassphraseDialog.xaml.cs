/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Backup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Views;

/// <summary>
/// Asks for the passphrase of an encrypted backup the stored key doesn't open. A wrong one is reported here,
/// so the user can try again without picking the file again.
/// </summary>
public sealed partial class BackupPassphraseDialog : ContentDialog
{
    private readonly string _path;
    private readonly EncryptedBackupHeader _header;

    public BackupPassphraseDialog(string fileName, string path, EncryptedBackupHeader header)
    {
        InitializeComponent();
        _path = path;
        _header = header;
        MessageText.Text = $"\"{fileName}\" is encrypted. Enter the passphrase it was made with.";
        Closed += (_, _) => PassphraseBox.Password = "";
        Opened += (_, _) => PassphraseBox.Focus(FocusState.Programmatic);
    }

    /// <summary>The key from the passphrase, once it opened the file. The caller disposes it.</summary>
    public BackupMasterKey? Key { get; private set; }

    /// <summary>Asks for the passphrase. Returns the key, or null if the user cancelled.</summary>
    public static async Task<BackupMasterKey?> AskAsync(string fileName, string path, EncryptedBackupHeader header)
    {
        var dialog = new BackupPassphraseDialog(fileName, path, header);
        return await Dialogs.ShowCustomAsync(dialog) == ContentDialogResult.Primary ? dialog.Key : null;
    }

    private void Passphrase_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ProblemText.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = PassphraseBox.Password.Length > 0;
    }

    private async void Primary_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            IsPrimaryButtonEnabled = false;
            ProblemText.Visibility = Visibility.Collapsed;
            WorkingPanel.Visibility = Visibility.Visible;
            Key = await BackupEncryption.TryPassphraseAsync(_path, _header, PassphraseBox.Password);
            if (Key == null)
            {
                args.Cancel = true;
                ProblemText.Visibility = Visibility.Visible;
                PassphraseBox.SelectAll();
                PassphraseBox.Focus(FocusState.Programmatic);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EncryptedBackupException)
        {
            args.Cancel = true;
            ProblemText.Text = ex.Message;
            ProblemText.Visibility = Visibility.Visible;
        }
        finally
        {
            WorkingPanel.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = PassphraseBox.Password.Length > 0;
            deferral.Complete();
        }
    }
}
