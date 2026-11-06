/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using ContactLogic = Gnoseis.Core.Domain.ContactPage;

namespace Gnoseis.App.Controls;

/// <summary>
/// A note linked to one record, written in place. Ctrl+Enter saves the note and its link in one
/// transaction; Esc closes the box and keeps the draft; Cancel drops it.
/// </summary>
public sealed partial class QuickNoteBox : UserControl
{
    private DateOnly _day;
    private TimeSpan _time;
    private bool _dayPicked;
    private bool _updatingPickers;
    private bool _saving;

    public QuickNoteBox()
    {
        InitializeComponent();
    }

    /// <summary>The record the note is linked to.</summary>
    public RecordRef? LinkTo { get; set; }

    /// <summary>Raised after a save, with the new note's id.</summary>
    public event EventHandler<string>? Saved;

    /// <summary>Raised when the box should close: after Save, Cancel or Esc.</summary>
    public event EventHandler? Closed;

    public bool HasDraft => !string.IsNullOrWhiteSpace(TextBox.Text) || !string.IsNullOrWhiteSpace(TitleBox.Text);

    /// <summary>Sets the texts that name the record: "Write a note about …", "Will be linked to …".</summary>
    public void SetRecordName(string name)
    {
        TextBox.PlaceholderText = $"Write a note about {name}…";
        AutomationProperties.SetName(TextBox, $"Write a note about {name}");
        WillLinkText.Text = $"Will be linked to {name}";
    }

    /// <summary>Puts the cursor in the text; the day and time are now unless picked.</summary>
    public void FocusText()
    {
        if (!_dayPicked)
        {
            var now = DateTime.Now;
            _day = DateOnly.FromDateTime(now);
            _time = new TimeSpan(now.Hour, now.Minute, 0);
        }
        ShowDay();
        TitleBox.Visibility = TitleBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        TextBox.Focus(FocusState.Keyboard);
    }

    public void Clear()
    {
        TextBox.Text = "";
        TitleBox.Text = "";
        _dayPicked = false;
    }

    private void ShowDay()
    {
        DayText.Text = ContactLogic.DayAndTimeText(_day, _time, DateOnly.FromDateTime(DateTime.Now));
        AutomationProperties.SetName(DayButton, $"Day and time: {DayText.Text}");
    }

    private void Box_TextChanged(object sender, TextChangedEventArgs e) => SaveButton.IsEnabled = HasDraft && !_saving;

    private void DayFlyout_Opening(object? sender, object e)
    {
        _updatingPickers = true;
        DatePicker.Date = new DateTimeOffset(_day.ToDateTime(TimeOnly.MinValue));
        TimePicker.Time = _time;
        _updatingPickers = false;
    }

    private void DatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_updatingPickers || args.NewDate is not { } date)
        {
            return;
        }
        _day = DateOnly.FromDateTime(date.Date);
        _dayPicked = true;
        ShowDay();
    }

    private void TimePicker_TimeChanged(object sender, TimePickerValueChangedEventArgs e)
    {
        if (_updatingPickers)
        {
            return;
        }
        _time = e.NewTime;
        _dayPicked = true;
        ShowDay();
    }

    private void TitleButton_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Visibility = Visibility.Visible;
        TitleBox.Focus(FocusState.Keyboard);
    }

    /// <summary>Ctrl+Enter saves; Esc closes the box and keeps the draft.</summary>
    private async void Box_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Enter && ctrl)
        {
            e.Handled = true;
            await SaveAsync();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Clear();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    /// <summary>Saves the note and its link; returns false when it couldn't be saved.</summary>
    public async Task<bool> SaveAsync()
    {
        if (!HasDraft || _saving || LinkTo is not { } record)
        {
            return !HasDraft;
        }
        _saving = true;
        SaveButton.IsEnabled = false;
        try
        {
            var note = await App.Container.CreateLinkedNote.InvokeAsync(record.Id, record.Type, TitleBox.Text, TextBox.Text, _day, _time);
            Clear();
            Saved?.Invoke(this, note.Id);
            Closed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Couldn't save the note", ex);
            return false;
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = HasDraft;
        }
    }

    /// <summary>Opens the note editor with the draft, for a new note linked to the record.</summary>
    private async void OpenFullEditor_Click(object sender, RoutedEventArgs e)
    {
        var draft = new NoteDraft(TitleBox.Text, TextBox.Text, _day, _time);
        Clear();
        Closed?.Invoke(this, EventArgs.Empty);
        await Navigator.ShowEditorAsync(new EditRequest(RecordType.Note, LinkFrom: LinkTo, NoteDraft: draft));
    }
}
