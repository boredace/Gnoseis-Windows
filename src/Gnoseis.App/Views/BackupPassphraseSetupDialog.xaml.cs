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
using Windows.ApplicationModel.DataTransfer;

namespace Gnoseis.App.Views;

/// <summary>
/// Sets up (or changes) the backup passphrase in two steps: choose one (generated, recommended, or the
/// user's own), then enter it again. Only then is the key made and stored.
/// </summary>
public sealed partial class BackupPassphraseSetupDialog : ContentDialog
{
    private const int WordsPerRow = 4;

    private readonly bool _isChange;
    private string _generated = "";
    private bool _useAnywayConfirmed;
    private bool _confirming;

    public BackupPassphraseSetupDialog(bool isChange)
    {
        InitializeComponent();
        _isChange = isChange;
        if (isChange)
        {
            Title = "Change the backup passphrase";
            ChangeNote.Visibility = Visibility.Visible;
        }
        ShowNewWords();
        Closed += (_, _) => Clear();
    }

    /// <summary>True when the passphrase was set and its key stored.</summary>
    public bool Succeeded { get; private set; }

    private bool UsesOwn => KindButtons.SelectedIndex == 1;

    private string Chosen => UsesOwn ? OwnBox.Password : _generated;

    private void ShowNewWords()
    {
        _generated = BackupPassphrase.Generate();
        var words = _generated.Split(' ');
        WordsGrid.Children.Clear();
        WordsGrid.ColumnDefinitions.Clear();
        WordsGrid.RowDefinitions.Clear();
        for (var c = 0; c < WordsPerRow; c++)
        {
            WordsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var r = 0; r < words.Length / WordsPerRow; r++)
        {
            WordsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var i = 0; i < words.Length; i++)
        {
            var word = new TextBlock
            {
                Text = words[i],
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            };
            Grid.SetRow(word, i / WordsPerRow);
            Grid.SetColumn(word, i % WordsPerRow);
            WordsGrid.Children.Add(word);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(WordsGrid, "Generated passphrase: " + _generated);
        CopiedText.Text = "";
    }

    private void NewWords_Click(object sender, RoutedEventArgs e) => ShowNewWords();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(_generated);
        try
        {
            // Kept out of clipboard history and off other devices.
            Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false });
            CopiedText.Text = "Copied";
        }
        catch (Exception)
        {
            CopiedText.Text = "Couldn't copy";
        }
    }

    private void Kind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneratedPanel == null)
        {
            return; // still loading
        }
        GeneratedPanel.Visibility = UsesOwn ? Visibility.Collapsed : Visibility.Visible;
        OwnPanel.Visibility = UsesOwn ? Visibility.Visible : Visibility.Collapsed;
        ResetUseAnyway();
        UpdateFirstStep();
        if (UsesOwn)
        {
            OwnBox.Focus(FocusState.Programmatic);
        }
    }

    private void Own_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ResetUseAnyway();
        UpdateFirstStep();
    }

    private void UpdateFirstStep()
    {
        if (!UsesOwn)
        {
            OwnProblemText.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = true;
            return;
        }
        var problem = BackupPassphrase.Check(OwnBox.Password);
        OwnProblemText.Text = problem switch
        {
            PassphraseProblem.UnusableCharacter => "This passphrase contains a character that can't be used.",
            PassphraseProblem.TooShort when OwnBox.Password.Length > 0 => "Use at least 12 characters.",
            _ => "",
        };
        OwnProblemText.Visibility = OwnProblemText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        IsPrimaryButtonEnabled = problem == PassphraseProblem.None;
    }

    private void ResetUseAnyway()
    {
        _useAnywayConfirmed = false;
        UseAnywayInfoBar.IsOpen = false;
        PrimaryButtonText = "Next";
    }

    private async void Primary_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!_confirming)
        {
            args.Cancel = true;
            // No strength estimate yet, so a passphrase the user chose always needs the extra confirmation.
            if (UsesOwn && !_useAnywayConfirmed)
            {
                _useAnywayConfirmed = true;
                UseAnywayInfoBar.IsOpen = true;
                PrimaryButtonText = "Use it anyway";
                return;
            }
            ShowConfirmStep(true);
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            WorkingPanel.Visibility = Visibility.Visible;
            IsPrimaryButtonEnabled = false;
            IsSecondaryButtonEnabled = false;
            Succeeded = await App.BackupEncryption.SetPassphraseAsync(Chosen);
            if (!Succeeded)
            {
                args.Cancel = true;
                ShowConfirmProblem("This passphrase can't be used.");
            }
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ShowConfirmProblem($"Couldn't store the key ({ex.Message}).");
        }
        finally
        {
            WorkingPanel.Visibility = Visibility.Collapsed;
            IsSecondaryButtonEnabled = true;
            UpdateConfirmStep();
            deferral.Complete();
        }
    }

    private void Secondary_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Back, from step 2.
        args.Cancel = true;
        ShowConfirmStep(false);
    }

    private void ShowConfirmStep(bool show)
    {
        _confirming = show;
        ChoosePanel.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        ConfirmPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SecondaryButtonText = show ? "Back" : "";
        if (show)
        {
            PrimaryButtonText = _isChange ? "Change passphrase" : "Turn on encryption";
            ConfirmBox.Password = "";
            ConfirmProblemText.Visibility = Visibility.Collapsed;
            UpdateConfirmStep();
            ConfirmBox.Focus(FocusState.Programmatic);
        }
        else
        {
            ResetUseAnyway();
            UpdateFirstStep();
        }
    }

    private void Confirm_PasswordChanged(object sender, RoutedEventArgs e) => UpdateConfirmStep();

    /// <summary>The two match when they normalise to the same passphrase (line breaks and double spaces don't matter).</summary>
    private void UpdateConfirmStep()
    {
        var matches = BackupPassphrase.TryNormalise(Chosen, out var chosen)
            && BackupPassphrase.TryNormalise(ConfirmBox.Password, out var typed)
            && chosen == typed;
        IsPrimaryButtonEnabled = matches;
        if (!matches && BackupPassphrase.TryNormalise(ConfirmBox.Password, out var soFar) && soFar.Length >= chosen.Length && soFar.Length > 0)
        {
            ShowConfirmProblem("This doesn't match the passphrase.");
        }
        else
        {
            ConfirmProblemText.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowConfirmProblem(string text)
    {
        ConfirmProblemText.Text = text;
        ConfirmProblemText.Visibility = Visibility.Visible;
    }

    private void Clear()
    {
        _generated = "";
        OwnBox.Password = "";
        ConfirmBox.Password = "";
        WordsGrid.Children.Clear();
    }
}
