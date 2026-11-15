/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024-2026 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Data;
using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>A note written in the contact page's note box: the note and its link in one transaction.</summary>
public class CreateLinkedNoteTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly AppContainer _data;

    public CreateLinkedNoteTests()
    {
        _data = _test.Open();
    }

    public void Dispose() => _test.Dispose();

    private long Count(string table) => (long)TestDatabase.ScalarRaw(_test.FilePath, $"SELECT count(*) FROM {table};")!;

    [Fact]
    public async Task Writes_the_note_and_its_link_with_timestamps()
    {
        var contact = await _data.Contacts.AddContactAsync(new Contact { NameLast = "Bakker" });
        var day = new DateOnly(2026, 5, 26);
        var time = new TimeOnly(15, 40);
        var before = EpochTime.UtcNowMillis();

        var note = await _data.CreateLinkedNote.InvokeAsync(contact.Id, RecordType.Contact, "  ", "Called about\nthe order", day, time);

        var stored = await _data.Notes.GetNoteAsync(note.Id);
        Assert.NotNull(stored);
        Assert.Null(stored.Title);
        Assert.Equal("Called about\nthe order", stored.TextContents);
        Assert.Equal(NoteDates.ToStored(day, time, _data.Database.TimeZone), (stored.CreateDate, stored.CreateDateTime));
        var linked = await _data.LinkedRecords.GetRecordIdsLinkedToRecordIdAsync(contact.Id);
        Assert.Equal([note.Id], linked);

        var after = EpochTime.UtcNowMillis();
        Assert.InRange((long)TestDatabase.ScalarRaw(_test.FilePath, $"SELECT createdAt FROM Note WHERE id = '{note.Id}';")!, before, after);
        Assert.InRange((long)TestDatabase.ScalarRaw(_test.FilePath, $"SELECT updatedAt FROM Note WHERE id = '{note.Id}';")!, before, after);
        Assert.InRange((long)TestDatabase.ScalarRaw(_test.FilePath, "SELECT createdAt FROM LinkedRecord;")!, before, after);
        Assert.Equal((long)RecordType.Contact, (long)TestDatabase.ScalarRaw(_test.FilePath, "SELECT record1TypeId FROM LinkedRecord;")!);
        Assert.Equal((long)RecordType.Note, (long)TestDatabase.ScalarRaw(_test.FilePath, "SELECT record2TypeId FROM LinkedRecord;")!);
    }

    [Fact]
    public async Task Saves_line_breaks_as_line_feeds()
    {
        var note = await _data.CreateLinkedNote.InvokeAsync("c", RecordType.Contact, null, "Called\rabout\r\nthe\r\rorder", new DateOnly(2026, 1, 1), TimeOnly.MinValue);

        Assert.Equal("Called\nabout\nthe\n\norder", note.TextContents);
        Assert.Equal("Called\nabout\nthe\n\norder", (await _data.Notes.GetNoteAsync(note.Id))!.TextContents);
    }

    [Fact]
    public async Task Keeps_a_title_and_never_derives_one_from_the_text()
    {
        var withTitle = await _data.CreateLinkedNote.InvokeAsync("c", RecordType.Contact, " Visit ", "", new DateOnly(2026, 1, 1), TimeOnly.MinValue);
        var withoutTitle = await _data.CreateLinkedNote.InvokeAsync("c", RecordType.Contact, null, "Text only", new DateOnly(2026, 1, 1), TimeOnly.MinValue);

        Assert.Equal("Visit", (await _data.Notes.GetNoteAsync(withTitle.Id))!.Title);
        Assert.Null((await _data.Notes.GetNoteAsync(withoutTitle.Id))!.Title);
    }

    [Fact]
    public async Task A_failing_link_leaves_neither_the_note_nor_the_link()
    {
        TestDatabase.ExecuteRaw(_test.FilePath,
            "CREATE TRIGGER fail_link BEFORE INSERT ON LinkedRecord BEGIN SELECT RAISE(ABORT, 'link refused'); END;");
        var notesBefore = Count("Note");
        var linksBefore = Count("LinkedRecord");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _data.CreateLinkedNote.InvokeAsync("c", RecordType.Contact, "Title", "Text", new DateOnly(2026, 1, 1), TimeOnly.MinValue));

        Assert.Equal(notesBefore, Count("Note"));
        Assert.Equal(linksBefore, Count("LinkedRecord"));
    }

    [Fact]
    public async Task Notifies_note_and_link_changes()
    {
        var changed = new List<RecordType>();
        _data.Database.DataChanged += (_, e) => changed.AddRange(e.Types);

        await _data.CreateLinkedNote.InvokeAsync("c", RecordType.Contact, null, "Text", new DateOnly(2026, 1, 1), TimeOnly.MinValue);

        Assert.Contains(RecordType.Note, changed);
        Assert.Contains(RecordType.LinkedRecord, changed);
    }

    [Fact]
    public async Task A_quick_note_whose_day_and_time_nobody_picked_is_the_moment_of_saving()
    {
        using var test = new TestDatabase();
        var data = test.Open(NoteDatesTests.Zone("Asia/Tokyo"));
        var now = new DateTimeOffset(2026, 11, 13, 23, 15, 42, 123, TimeSpan.Zero); // 14 Nov 08:15:42 in Tokyo

        var note = await data.CreateLinkedNote.InvokeAsync("c", RecordType.Contact, null, "Text", now: now);

        var stored = (await data.Notes.GetNoteAsync(note.Id))!;
        Assert.Equal((1794582000000, now.ToUnixTimeMilliseconds()), (stored.CreateDate, stored.CreateDateTime));
    }

    [Fact]
    public async Task A_quick_note_with_a_picked_day_and_time_is_stored_by_the_rule()
    {
        using var test = new TestDatabase();
        var data = test.Open(NoteDatesTests.Zone("America/Toronto"));

        var note = await data.CreateLinkedNote.InvokeAsync("c", RecordType.Contact, null, "Text", new DateOnly(2026, 3, 8), new TimeOnly(2, 30));

        var stored = (await data.Notes.GetNoteAsync(note.Id))!;
        Assert.Equal((1772946000000, 1772955000000), (stored.CreateDate, stored.CreateDateTime));
    }
}
