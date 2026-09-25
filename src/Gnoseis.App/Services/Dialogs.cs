/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gnoseis.App.Services;

/// <summary>Content dialogs. WinUI allows only one open dialog at a time, so calls are queued.</summary>
public static class Dialogs
{
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    public static XamlRoot? XamlRoot { get; set; }

    /// <summary>Asks a question. Returns true when the primary button was chosen.</summary>
    public static async Task<bool> ConfirmAsync(string title, object content, string primaryButtonText,
        string closeButtonText = "Cancel", string? secondaryButtonText = null, bool defaultToPrimary = false)
    {
        var dialog = Create(title, content);
        dialog.PrimaryButtonText = primaryButtonText;
        dialog.CloseButtonText = closeButtonText;
        if (secondaryButtonText != null)
        {
            dialog.SecondaryButtonText = secondaryButtonText;
        }
        dialog.DefaultButton = defaultToPrimary ? ContentDialogButton.Primary : ContentDialogButton.Close;
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>Asks a question with three answers.</summary>
    public static async Task<ContentDialogResult> AskAsync(string title, object content, string primaryButtonText,
        string secondaryButtonText, string closeButtonText)
    {
        var dialog = Create(title, content);
        dialog.PrimaryButtonText = primaryButtonText;
        dialog.SecondaryButtonText = secondaryButtonText;
        dialog.CloseButtonText = closeButtonText;
        dialog.DefaultButton = ContentDialogButton.Primary;
        return await ShowAsync(dialog);
    }

    public static async Task ShowMessageAsync(string title, object content)
    {
        var dialog = Create(title, content);
        dialog.CloseButtonText = "OK";
        dialog.DefaultButton = ContentDialogButton.Close;
        await ShowAsync(dialog);
    }

    public static Task ShowErrorAsync(string title, Exception exception) =>
        ShowMessageAsync(title, exception.Message);

    /// <summary>Shows a dialog built elsewhere (e.g. the link picker) with the app's root, style and theme.</summary>
    public static Task<ContentDialogResult> ShowCustomAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = XamlRoot;
        dialog.Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];
        if (XamlRoot?.Content is FrameworkElement root)
        {
            dialog.RequestedTheme = root.ActualTheme;
        }
        return ShowAsync(dialog);
    }

    private static ContentDialog Create(string title, object content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content is string text
                ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
                : content,
            XamlRoot = XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };
        if (XamlRoot?.Content is FrameworkElement root)
        {
            dialog.RequestedTheme = root.ActualTheme;
        }
        return dialog;
    }

    private static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await OneAtATime.WaitAsync();
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            OneAtATime.Release();
        }
    }
}
