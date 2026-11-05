/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using Gnoseis.Core.Domain;

namespace Gnoseis.App.ViewModels;

/// <summary>What a finished search offers to create: the query it searched for and where the offer goes.</summary>
public sealed record CreateOffer(string Query, CreateFromSearchOffer Offer)
{
    public static CreateOffer None { get; } = new("", CreateFromSearchOffer.None);
}
