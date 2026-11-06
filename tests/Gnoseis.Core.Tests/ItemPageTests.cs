/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;

namespace Gnoseis.Core.Tests;

/// <summary>The item page: header line and copy text.</summary>
public class ItemPageTests
{
    // ---------------------------------------------------------------- Header line

    [Fact]
    public void Header_line_shows_notes_and_linked()
    {
        Assert.Equal(("Item · 26 notes · 16 linked", false), ItemPage.HeaderLine(26, 16));
    }

    [Fact]
    public void Header_line_uses_singular_for_one()
    {
        Assert.Equal(("Item · 1 note · 1 linked", false), ItemPage.HeaderLine(1, 1));
    }

    [Fact]
    public void Header_line_omits_parts_at_zero()
    {
        Assert.Equal(("Item · 26 notes", false), ItemPage.HeaderLine(26, 0));
        Assert.Equal(("Item · 16 linked", false), ItemPage.HeaderLine(0, 16));
    }

    [Fact]
    public void Header_line_placeholder_when_no_notes_or_links()
    {
        Assert.Equal(("Item · no notes or links yet", true), ItemPage.HeaderLine(0, 0));
    }

    // ---------------------------------------------------------------- Copy text

    [Fact]
    public void Copy_text_has_name_and_comments()
    {
        var item = new Item { ItemName = "SRV-0007 Hyper-V host", Comments = "Production server, rack 4" };

        Assert.Equal(
            string.Join(Environment.NewLine, "SRV-0007 Hyper-V host", "", "Production server, rack 4"),
            ItemPage.CopyText(item));
    }

    [Fact]
    public void Copy_text_leaves_out_blank_comments()
    {
        var item = new Item { ItemName = "SRV-0007 Hyper-V host" };

        Assert.Equal("SRV-0007 Hyper-V host", ItemPage.CopyText(item));
    }

    // ---------------------------------------------------------------- Side types

    [Fact]
    public void Side_types_are_contacts_organizations_categories_and_items()
    {
        Assert.Equal(
            [RecordType.Contact, RecordType.Organization, RecordType.Category, RecordType.Item],
            ItemPage.SideTypes);
    }
}
