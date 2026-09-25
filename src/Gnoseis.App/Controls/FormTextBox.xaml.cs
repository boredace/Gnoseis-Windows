/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Gnoseis.App.Controls;

public enum FormTextKind
{
    Text,
    Name,
    Phone,
    Email,
    Multiline,
}

/// <summary>A labelled text box bound to a <see cref="FormField"/>, with its validation message below.</summary>
public sealed partial class FormTextBox : UserControl
{
    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
        nameof(Field), typeof(FormField), typeof(FormTextBox), new PropertyMetadata(null, (d, _) => ((FormTextBox)d).UpdateHeader()));

    public static readonly DependencyProperty IsRequiredProperty = DependencyProperty.Register(
        nameof(IsRequired), typeof(bool), typeof(FormTextBox), new PropertyMetadata(false, (d, _) => ((FormTextBox)d).UpdateHeader()));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(FormTextKind), typeof(FormTextBox), new PropertyMetadata(FormTextKind.Text, (d, _) => ((FormTextBox)d).UpdateKind()));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(FormTextBox), new PropertyMetadata(""));

    public FormTextBox()
    {
        InitializeComponent();
        UpdateKind();
    }

    public FormField? Field
    {
        get => (FormField?)GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    public bool IsRequired
    {
        get => (bool)GetValue(IsRequiredProperty);
        set => SetValue(IsRequiredProperty, value);
    }

    public FormTextKind Kind
    {
        get => (FormTextKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Moves keyboard focus into the text box.</summary>
    public void FocusText() => Box.Focus(FocusState.Programmatic);

    private void UpdateHeader()
    {
        var label = Field?.Label ?? "";
        Box.Header = IsRequired ? $"{label} (required)" : label;
        AutomationPropertiesHelper.SetName(Box, label);
    }

    private void UpdateKind()
    {
        var multiline = Kind == FormTextKind.Multiline;
        Box.AcceptsReturn = multiline;
        Box.TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Box.MinHeight = multiline ? 200 : 0;
        Box.IsSpellCheckEnabled = Kind is FormTextKind.Text or FormTextKind.Multiline;
        Box.IsTextPredictionEnabled = Kind is FormTextKind.Text or FormTextKind.Multiline;

        var scope = Kind switch
        {
            FormTextKind.Phone => InputScopeNameValue.TelephoneNumber,
            FormTextKind.Email => InputScopeNameValue.EmailSmtpAddress,
            FormTextKind.Name => InputScopeNameValue.PersonalFullName,
            _ => InputScopeNameValue.Default,
        };
        var inputScope = new InputScope();
        inputScope.Names.Add(new InputScopeName(scope));
        Box.InputScope = inputScope;
    }
}

internal static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
}
