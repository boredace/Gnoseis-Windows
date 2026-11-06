/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Gnoseis.App.ViewModels;

/// <summary>One record in a section's trail of opened records.</summary>
public sealed class TrailRow
{
    private static readonly AccessibilitySettings Accessibility = new();

    public TrailRow(RecordRef record, int index, int count, int currentIndex, string title, string sectionTitle)
    {
        Record = record;
        Index = index;
        Title = title;
        IsFirst = index == 0;
        IsLast = index == count - 1;
        IsCurrent = index == currentIndex;
        IsAfterCurrent = index > currentIndex;
        TypeName = record.Type.DisplayName();
        Subtitle = IsFirst ? $"In the {sectionTitle} list" : TypeName;
        AutomationName = string.Join(", ", new[] { title, TypeName, IsAfterCurrent ? "forward" : "" }.Where(s => s.Length > 0));
        // The line to the row above is dashed when this row is after the current one; the line to the
        // row below when that row is.
        TopLineDashed = IsAfterCurrent;
        BottomLineDashed = index >= currentIndex;
    }

    public RecordRef Record { get; }
    public RecordType Type => Record.Type;
    public int Index { get; }
    public string Title { get; }
    public string TypeName { get; }
    public string Subtitle { get; }
    public string AutomationName { get; }
    public bool IsFirst { get; }
    public bool IsLast { get; }
    public bool IsCurrent { get; }

    /// <summary>A record stepped back from: dimmed, still clickable, back with Forward.</summary>
    public bool IsAfterCurrent { get; }

    public bool IsPerson => Type == RecordType.Contact;
    public bool ShowTile => !IsPerson;

    public Visibility TopLineVisibility => IsFirst ? Visibility.Collapsed : Visibility.Visible;
    public Visibility BottomLineVisibility => IsLast ? Visibility.Collapsed : Visibility.Visible;
    public bool TopLineDashed { get; }
    public bool BottomLineDashed { get; }

    /// <summary>Dimmed rows are drawn at half opacity, except in high contrast, where their text is drawn in the disabled-text colour.</summary>
    public double RowOpacity => IsAfterCurrent && !Accessibility.HighContrast ? 0.5 : 1;

    public bool UsesDisabledText => IsAfterCurrent && Accessibility.HighContrast;
}

/// <summary>
/// An item of the narrow window's breadcrumb: the section (index -1, back to the list) or a record of
/// the trail up to the one shown, which is last and semibold.
/// </summary>
public sealed class TrailCrumb(string text, int index, bool isCurrent, string automationName)
{
    public string Text { get; } = text;
    public int Index { get; } = index;
    public bool IsCurrent { get; } = isCurrent;
    public string AutomationName { get; } = automationName;
    public Windows.UI.Text.FontWeight Weight => IsCurrent ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    public override string ToString() => Text;
}

/// <summary>
/// The trail column of a section: a row per record opened from one another, with their titles loaded
/// from the database and refreshed when data changes.
/// </summary>
public sealed class TrailViewModel(RecordType sectionType, RecordTrail<RecordRef> trail) : PageViewModel
{
    private readonly Dictionary<RecordRef, string> _titles = [];
    private List<TrailRow> _rows = [];
    private int _loadVersion;

    public string SectionTitle { get; } = sectionType.PluralDisplayName();

    public List<TrailRow> Rows
    {
        get => _rows;
        private set
        {
            _rows = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(Hint));
        }
    }

    public string Subtitle => $"From {SectionTitle} · {trail.Count} records";

    public string Hint => trail.CanGoForward
        ? "Dimmed records come back with Alt+→, until you open another link."
        : "Records you open from a record's links are added here. Alt+← steps back; from the first row, back to the list.";

    /// <summary>The breadcrumb up to the record shown: the section, then the trail's records.</summary>
    public List<TrailCrumb> Crumbs()
    {
        var crumbs = new List<TrailCrumb> { new(SectionTitle, -1, false, $"{SectionTitle} list") };
        foreach (var row in Rows.Where(r => !r.IsAfterCurrent))
        {
            crumbs.Add(new TrailCrumb(row.Title, row.Index, row.IsCurrent, $"{row.Title}, {row.TypeName}"));
        }
        return crumbs;
    }

    /// <summary>Raised after titles were loaded, with the records of the trail that no longer exist.</summary>
    public event EventHandler<IReadOnlyList<RecordRef>>? RecordsMissing;

    /// <summary>The title shown for a record, if it was loaded.</summary>
    public string? TitleOf(RecordRef record) => _titles.GetValueOrDefault(record);

    /// <summary>Rebuilds the rows after the trail changed, loading titles not known yet.</summary>
    public void Refresh()
    {
        BuildRows();
        if (trail.Items.Any(r => !_titles.ContainsKey(r)))
        {
            _ = LoadTitlesAsync();
        }
    }

    protected override void OnDataChanged(DataChangedEventArgs e)
    {
        if (trail.Count > 0)
        {
            _ = LoadTitlesAsync(reportMissing: true);
        }
    }

    private void BuildRows()
    {
        var items = trail.Items;
        Rows = items.Select((r, i) => new TrailRow(r, i, items.Count, trail.CurrentIndex, _titles.GetValueOrDefault(r) ?? "", SectionTitle)).ToList();
    }

    private async Task LoadTitlesAsync(bool reportMissing = false)
    {
        var version = ++_loadVersion;
        var records = trail.Items.ToList();
        var summaries = await Data.Home.GetRecordSummariesAsync(records.Select(r => (r.Type, r.Id)).ToList());
        if (version != _loadVersion)
        {
            return;
        }
        foreach (var summary in summaries)
        {
            _titles[new RecordRef(summary.Type, summary.Id)] = TitleOf(summary);
        }
        var missing = records.Where(r => !summaries.Any(s => s.Type == r.Type && s.Id == r.Id)).ToList();
        BuildRows();
        if (reportMissing && missing.Count > 0)
        {
            RecordsMissing?.Invoke(this, missing);
        }
    }

    /// <summary>A record's title as the trail shows it: its name, or a note's text when it has no title.</summary>
    public static string TitleOf(LinkedRecordSummary summary) =>
        summary.Name.Length > 0 ? summary.Name
        : summary.Text.Length > 0 ? summary.Text
        : summary.FirstLine.Length > 0 ? summary.FirstLine
        : $"Untitled {summary.Type.DisplayName().ToLowerInvariant()}";
}
