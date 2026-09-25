/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

using System.Globalization;
using System.Text;
using Gnoseis.Core.Domain;
using Gnoseis.Core.Models;
using static Gnoseis.DemoData.Vocabulary;

namespace Gnoseis.DemoData;

internal enum OrgRole { Client, FormerClient, Prospect, Vendor, Partner, Other }

internal sealed class Org
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Short { get; init; }
    public required string Code { get; init; }
    public required OrgRole Role { get; init; }
    public Sector? Sector { get; init; }
    public required Country Country { get; init; }
    public required City City { get; init; }
    public required string Domain { get; init; }
    public required string Phone { get; init; }
    public required int Employees { get; init; }
    public required DateOnly Since { get; init; }
    public DateOnly? Until { get; init; }
    public required string Subnet { get; init; }
    public string? LeadTag { get; set; }
    public double Weight { get; set; }
    public List<ContactInfo> Contacts { get; } = [];
    public List<ItemInfo> Items { get; } = [];
    public List<ItemInfo> SuppliedItems { get; } = [];
    public List<ProjectInfo> Projects { get; } = [];
    public Dictionary<string, int> ItemNumbers { get; } = [];

    public bool IsClient => Role is OrgRole.Client or OrgRole.FormerClient;
}

internal sealed record ContactInfo(Contact Record, Org Org)
{
    public string FullName => string.IsNullOrEmpty(Record.NameFirst) ? Record.NameLast : $"{Record.NameFirst} {Record.NameLast}";
    public string First => Record.NameFirst ?? Record.NameLast;
    public string WithTitle => Record.JobTitle is null ? FullName : $"{FullName} ({Record.JobTitle})";
}

internal sealed record ItemInfo(Item Record, ItemKind Kind, Org? Owner, string Model, ContactInfo? AssignedTo);

internal sealed class ProjectInfo
{
    public required Org Org { get; init; }
    public required ProjectType Type { get; init; }
    public required DateOnly Start { get; init; }
    public required DateOnly End { get; init; }
    public required string Status { get; init; }
    public required int Hours { get; init; }
    public required int Rate { get; init; }
    public required string Pricing { get; init; }
    public ContactInfo? Lead { get; init; }
    public string Code { get; set; } = "";
    public Category Record { get; set; } = null!;
    public string Name => Clip($"{Code} {Type.Name}", TitleLength.Category);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd();
}

internal sealed record GeneratedData(
    List<Note> Notes, List<Contact> Contacts, List<Organization> Organizations, List<Category> Categories,
    List<Item> Items, List<LinkedRecord> Links);

/// <summary>
/// Builds the knowledge base of an independent IT consultant: clients, former clients, prospects,
/// vendors and partners (organizations), their people (contacts), a tag taxonomy and client projects
/// (categories), managed devices, licences and services (items), and years of call, ticket, meeting,
/// project, quote and invoice notes, all linked to each other the way they would be in daily use.
/// </summary>
internal sealed class DemoDataGenerator
{
    private const string OwnerDbId = "db1";
    private static readonly ItemKind Licences = ItemKinds.First(k => k.Prefix == "LIC");
    private static readonly ItemKind Printers = ItemKinds.First(k => k.Prefix == "PRN");
    private const string NumberToken = "####";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly int _count;
    private readonly Rng _rng;
    private readonly DateOnly _today;
    private readonly DateOnly _windowStart;

    private readonly List<Org> _orgs = [];
    private readonly List<ContactInfo> _contacts = [];
    private readonly List<ItemInfo> _items = [];
    private readonly List<ProjectInfo> _projects = [];
    private readonly List<Category> _categories = [];
    private readonly Dictionary<string, Category> _tags = new(StringComparer.Ordinal);
    private readonly List<Note> _notes = [];
    private readonly Dictionary<string, string> _numberedNotes = [];
    private readonly List<LinkedRecord> _links = [];
    private readonly HashSet<string> _linkKeys = [];
    private readonly HashSet<string> _orgNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _codes = [];
    private readonly HashSet<string> _domains = [];
    private Queue<(string Title, string Text, string Tag)>? _kbPool;

    public DemoDataGenerator(int count, int seed, DateOnly today)
    {
        _count = count;
        _rng = new Rng(seed);
        _today = today;
        _windowStart = today.AddYears(-5);
    }

    public GeneratedData Generate()
    {
        CreateTaxonomy();
        CreateOrganizations();
        CreateContacts();
        CreateItems();
        CreateProjects();
        CreateNotes();

        return new GeneratedData(
            _notes,
            _contacts.Select(c => c.Record).ToList(),
            _orgs.Select(o => new Organization { Id = o.Id, OwnerDbId = OwnerDbId, OrganizationName = o.Name, Comments = OrgComments(o) }).ToList(),
            _categories,
            _items.Select(i => i.Record).ToList(),
            _links);
    }

    // ------------------------------------------------------------------ categories: tag taxonomy

    private void CreateTaxonomy()
    {
        var planned = new List<Category>();
        foreach (var area in Taxonomy)
        {
            var parent = new Category { Id = _rng.Guid(), OwnerDbId = OwnerDbId, CategoryName = area.Area, Comments = area.Description };
            planned.Add(parent);
            planned.AddRange(area.Topics.Select(topic => new Category
            {
                Id = _rng.Guid(),
                OwnerDbId = OwnerDbId,
                ParentId = parent.Id,
                CategoryName = $"{area.Area}: {topic}",
                Comments = $"{topic} ({area.Area}). Link notes, devices and projects about this topic here.",
            }));
        }

        planned.AddRange(StatusTags.Select(tag => new Category
        {
            Id = _rng.Guid(),
            OwnerDbId = OwnerDbId,
            CategoryName = tag,
            Comments = tag.Split(':')[0] switch
            {
                "Status" => "Work status. Remove the link when the status changes.",
                "Billing" => "How the work is billed.",
                "Priority" => "Urgency of tickets and tasks.",
                "Client tier" => "A: large or strategic client, B: regular client, C: occasional work.",
                _ => "How likely the prospect is to become a client.",
            },
        }));

        planned.AddRange(Sectors.Select(s => new Category
        {
            Id = _rng.Guid(), OwnerDbId = OwnerDbId, CategoryName = $"Sector: {s.Name}",
            Comments = $"Clients and prospects in {s.Name.ToLowerInvariant()}. Typical systems: {string.Join(", ", s.Systems)}.",
        }));

        foreach (var kind in ItemKinds)
        {
            foreach (var model in kind.Models)
            {
                planned.Add(new Category
                {
                    Id = _rng.Guid(), OwnerDbId = OwnerDbId, CategoryName = Clip($"Tech: {model}", TitleLength.Category),
                    Comments = $"{kind.Kind}. Devices, licences and notes about {model}.",
                });
            }
        }

        foreach (var category in planned.Take(_count))
        {
            if (_tags.TryAdd(category.CategoryName, category))
            {
                _categories.Add(category);
            }
        }
    }

    private Category? Tag(string name) => _tags.GetValueOrDefault(name);

    private Category? TopicTag(string area, string topic) => Tag($"{area}: {topic}") ?? Tag(area);

    private Category? TechTag(string model) => Tag(Clip($"Tech: {model}", TitleLength.Category));

    // ------------------------------------------------------------------ organizations

    private void CreateOrganizations()
    {
        var countries = new WeightedPicker<Country>(Countries, c => c.Weight);
        var roles = new WeightedPicker<(OrgRole Role, double Weight)>(
            [(OrgRole.Client, 22), (OrgRole.FormerClient, 10), (OrgRole.Prospect, 33), (OrgRole.Vendor, 15),
             (OrgRole.Partner, 10), (OrgRole.Other, 10)], r => r.Weight);

        for (var i = 0; i < _count; i++)
        {
            var role = roles.Pick(_rng).Role;
            var country = countries.Pick(_rng);
            var city = _rng.Pick(country.Cities);
            var (shortName, name, sector) = OrgName(role, country, city);
            var employees = (int)Math.Round(Math.Exp(_rng.Double() * Math.Log(200))) + 2;

            var since = role switch
            {
                OrgRole.Client => _today.AddDays(-_rng.Between(30, 365 * 12)),
                OrgRole.FormerClient => _today.AddDays(-_rng.Between(900, 365 * 12)),
                OrgRole.Prospect => _today.AddDays(-_rng.Between(1, 365 * 3)),
                _ => _today.AddDays(-_rng.Between(60, 365 * 10)),
            };
            DateOnly? until = null;
            if (role == OrgRole.FormerClient)
            {
                var end = since.AddDays(_rng.Between(365, 365 * 6));
                until = end > _today.AddDays(-30) ? _today.AddDays(-_rng.Between(30, 400)) : end;
            }

            var org = new Org
            {
                Id = _rng.Guid(),
                Name = name,
                Short = shortName,
                Code = UniqueCode(shortName),
                Role = role,
                Sector = sector,
                Country = country,
                City = city,
                Domain = UniqueDomain(shortName, country, city),
                Phone = Landline(country, city),
                Employees = employees,
                Since = since,
                Until = until,
                Subnet = $"10.{_rng.Between(0, 254)}.{_rng.Between(0, 254)}",
            };
            org.Weight = Math.Sqrt(employees) * role switch
            {
                OrgRole.Client => 1.0,
                OrgRole.FormerClient => 0.25,
                OrgRole.Prospect => 0.12,
                OrgRole.Vendor => 0.35,
                OrgRole.Partner => 0.3,
                _ => 0.08,
            };
            _orgs.Add(org);

            if (sector != null)
            {
                LinkTo(org, Tag($"Sector: {sector.Name}"));
            }
            if (role == OrgRole.Client)
            {
                LinkTo(org, Tag(employees > 60 ? "Client tier: A" : employees > 15 ? "Client tier: B" : "Client tier: C"));
            }
            if (role == OrgRole.Prospect)
            {
                org.LeadTag = _rng.Pick(new[] { "Lead: Hot", "Lead: Warm", "Lead: Warm", "Lead: Cold", "Lead: Cold" });
                LinkTo(org, Tag(org.LeadTag));
            }
        }
    }

    private (string Short, string Name, Sector? Sector) OrgName(OrgRole role, Country country, City city)
    {
        for (var attempt = 0; ; attempt++)
        {
            Sector? sector = null;
            string shortName;
            var suffixChance = 0.8;
            var r = _rng.Double();
            switch (role)
            {
                case OrgRole.Vendor:
                    shortName = $"{_rng.Pick(Prefixes)} {_rng.Pick(VendorNouns)}";
                    break;
                case OrgRole.Partner:
                    shortName = r < 0.5
                        ? $"{_rng.Pick(country.Names.First)} {_rng.Pick(country.Names.Last)} {_rng.Pick(PartnerNouns)}"
                        : $"{_rng.Pick(Prefixes)} {_rng.Pick(PartnerNouns)}";
                    suffixChance = 0.5;
                    break;
                case OrgRole.Other:
                    shortName = $"{(r < 0.5 ? city.Name : _rng.Pick(Prefixes))} {_rng.Pick(OtherNouns)}";
                    suffixChance = 0;
                    break;
                default:
                    sector = _rng.Pick(Sectors);
                    var noun = _rng.Pick(sector.Nouns);
                    shortName = r switch
                    {
                        < 0.55 => $"{_rng.Pick(Prefixes)} {noun}",
                        < 0.75 => $"{_rng.Pick(country.Names.Last)} {noun}",
                        < 0.9 => $"{_rng.Pick(country.Names.Last)} & {_rng.Pick(country.Names.Last)} {noun}",
                        _ => $"{city.Name} {noun}",
                    };
                    break;
            }

            if (attempt > 30)
            {
                shortName = $"{shortName} {city.Name}";
            }
            if (attempt > 60)
            {
                shortName = $"{shortName} {attempt}";
            }

            var suffix = sector?.Name == "Non-profit"
                ? country.Code is "DE" or "AT" or "CH" ? "e.V." : null
                : _rng.Pick(country.LegalSuffixes);
            var name = suffix != null && _rng.Chance(suffixChance) ? $"{shortName} {suffix}" : shortName;
            if (name.Length > TitleLength.Organization)
            {
                name = shortName;
            }
            if (name.Length <= TitleLength.Organization && _orgNames.Add(name))
            {
                return (shortName, name, sector);
            }
        }
    }

    private string UniqueCode(string shortName)
    {
        var words = Ascii(shortName).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsAsciiLetterUpper).ToArray())).Where(w => w.Length > 0).ToList();
        var code = string.Concat(words.Take(3).Select(w => w[0]));
        if (code.Length < 3 && words.Count > 0)
        {
            code = (code + words[0][1..] + "XXX")[..3];
        }
        var candidate = code;
        for (var n = 2; !_codes.Add(candidate); n++)
        {
            candidate = $"{code}{n}";
        }
        return candidate;
    }

    private string UniqueDomain(string shortName, Country country, City city)
    {
        var words = Ascii(shortName).ToLowerInvariant().Replace("&", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsAsciiLetterOrDigit).ToArray())).Where(w => w.Length > 0).ToList();
        var joined = string.Concat(words);
        var label = joined.Length <= 16 ? joined : string.Join("-", words);
        if (label.Length > 40)
        {
            label = label[..40].TrimEnd('-');
        }
        var candidate = $"{label}.{country.Tld}";
        var citySlug = new string(Ascii(city.Name).ToLowerInvariant().Where(char.IsAsciiLetter).ToArray());
        for (var n = 1; !_domains.Add(candidate); n++)
        {
            candidate = n == 1 ? $"{label}-{citySlug}.{country.Tld}" : $"{label}{n}.{country.Tld}";
        }
        return candidate;
    }

    private string OrgComments(Org org)
    {
        var sb = new StringBuilder();
        var place = $"{org.City.Name}, {org.Country.Code}";
        switch (org.Role)
        {
            case OrgRole.Client:
                sb.AppendLine($"Client since {org.Since.Year} · {_rng.Pick(new[] { "Managed services contract", "Support retainer", "Time and materials", "Managed services contract" })}");
                sb.AppendLine($"{org.Sector!.Name} · about {org.Employees} employees · {place}");
                sb.AppendLine($"IT: {_rng.Pick(Licences.Models.Take(4).ToList())}, {_rng.Pick(org.Sector.Systems)}, {_rng.Pick(OperatingSystems.Take(3).ToList())} clients");
                sb.AppendLine($"Billing: {_rng.Pick(new[] { "monthly", "monthly", "quarterly" })} invoice, {_rng.Pick(new[] { "net 14 days", "net 30 days", "direct debit" })}");
                sb.AppendLine($"Response time: {_rng.Pick(new[] { "4 business hours", "next business day", "2 hours (critical)", "8 business hours" })}");
                break;
            case OrgRole.FormerClient:
                sb.AppendLine($"Former client ({org.Since.Year}–{org.Until!.Value.Year}). {_rng.Pick(new[] { "Moved to an in-house IT team.", "Company was acquired.", "Switched to a larger provider.", "Business closed.", "Moved to another region.", "Contract ended by mutual agreement." })}");
                sb.AppendLine($"{org.Sector!.Name} · about {org.Employees} employees · {place}");
                sb.AppendLine("Documentation handed over; admin access removed.");
                break;
            case OrgRole.Prospect:
                sb.AppendLine($"Prospect since {Day(org.Since)}, via {_rng.Pick(Referrals)}.");
                sb.AppendLine($"{org.Sector!.Name} · about {org.Employees} employees · {place}");
                sb.AppendLine($"Interested in: {_rng.Pick(ProjectTypes).Name}.");
                break;
            case OrgRole.Vendor:
                sb.AppendLine($"Supplier · customer number {_rng.Digits(6)} · {place}");
                sb.AppendLine($"Partner level: {_rng.Pick(new[] { "Registered", "Silver", "Gold", "none" })}");
                sb.AppendLine($"Payment: {_rng.Pick(new[] { "net 30 days", "credit card", "direct debit", "prepayment" })}");
                sb.AppendLine($"Support portal login stored in the password manager.");
                break;
            case OrgRole.Partner:
                sb.AppendLine($"Partner / subcontractor · {place}");
                sb.AppendLine($"Day rate: {Money(org, _rng.Between(55, 110) * 10)} · NDA signed {org.Since.Year}");
                sb.AppendLine($"Good for: {string.Join(", ", _rng.PickDistinct(ProjectTypes, 2).Select(t => t.Name))}.");
                break;
            default:
                sb.AppendLine($"Network / association · {place}");
                sb.AppendLine(_rng.Pick(new[] { "Member since " + org.Since.Year + ".", "Attended events here.", "Good place to meet new clients.", "Annual fee paid by direct debit." }));
                break;
        }
        sb.AppendLine($"Phone: {org.Phone}");
        sb.Append($"Web: www.{org.Domain}");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ contacts

    private void CreateContacts()
    {
        // Every client gets a main contact first, then the rest go mostly to the larger clients.
        foreach (var org in _orgs)
        {
            var chance = org.Role switch
            {
                OrgRole.Client => 1.0,
                OrgRole.FormerClient => 0.6,
                OrgRole.Vendor => 0.6,
                OrgRole.Partner => 0.7,
                OrgRole.Prospect => 0.4,
                _ => 0.3,
            };
            if (_contacts.Count < _count && _rng.Chance(chance))
            {
                AddContact(org, primary: true);
            }
        }

        var picker = new WeightedPicker<Org>(_orgs, o => o.Role == OrgRole.Client ? o.Weight * 4 : o.Weight);
        while (_contacts.Count < _count)
        {
            AddContact(picker.Pick(_rng), primary: false);
        }
    }

    private void AddContact(Org org, bool primary)
    {
        var country = org.Country;
        Contact contact;
        if (org.Role == OrgRole.Vendor && _rng.Chance(0.12))
        {
            var (team, mailbox) = _rng.Pick(new[] { ("Support Desk", "support"), ("Sales Team", "sales"), ("Accounts Receivable", "billing"), ("Order Desk", "orders") });
            contact = new Contact
            {
                Id = _rng.Guid(), OwnerDbId = OwnerDbId, NameLast = team, Company = org.Name,
                PhoneWork = org.Phone, EmailWork = $"{mailbox}@{org.Domain}",
                Comments = _rng.Chance(0.5) ? $"Opening hours: Mon–Fri 8:00–17:00. Have the customer number ready." : null,
            };
        }
        else
        {
            var first = _rng.Pick(country.Names.First);
            var last = _rng.Pick(country.Names.Last);
            if (country.Code == "PL" && first.EndsWith('a'))
            {
                last = last.EndsWith("ski") ? last[..^3] + "ska" : last.EndsWith("cki") ? last[..^3] + "cka" : last;
            }

            var title = org.Role switch
            {
                OrgRole.Vendor => _rng.Pick(VendorTitles),
                OrgRole.Partner => _rng.Pick(PartnerTitles),
                OrgRole.Other => _rng.Pick(OtherTitles),
                _ when primary => _rng.Pick(new[] { "Managing Director", "Owner", "Office Manager", "Head of IT", "CEO", org.Sector!.Titles[0] }),
                _ => _rng.Chance(0.3) ? _rng.Pick(org.Sector!.Titles) : _rng.Pick(GenericClientTitles),
            };

            var mailFirst = MailPart(first);
            var mailLast = MailPart(last);
            var local = _rng.Double() switch
            {
                < 0.7 => $"{mailFirst}.{mailLast}",
                < 0.85 => $"{mailFirst[0]}.{mailLast}",
                _ when org.Employees < 15 => mailFirst,
                _ => $"{mailFirst}{mailLast[0]}",
            };
            var workEmail = $"{local}@{org.Domain}";
            var workInMain = _rng.Chance(0.3);

            var remarks = new List<string>();
            if (_rng.Chance(0.55))
            {
                remarks.AddRange(_rng.PickDistinct(ContactRemarks, _rng.Between(1, 2)));
            }
            if (_rng.Chance(0.12))
            {
                remarks.Add($"Birthday: {new DateOnly(2000, _rng.Between(1, 12), _rng.Between(1, 28)).ToString("d MMMM", Inv)}.");
            }
            if (org.IsClient && _rng.Chance(0.15))
            {
                remarks.Add($"Has a key and alarm code for the office (code in the password manager).");
            }

            contact = new Contact
            {
                Id = _rng.Guid(),
                OwnerDbId = OwnerDbId,
                NameFirst = first,
                NameLast = last,
                JobTitle = title,
                Company = org.Name,
                PhoneMain = _rng.Chance(0.35) ? org.Phone : null,
                PhoneWork = _rng.Chance(0.8) ? DirectLine(org) : null,
                PhoneMobile = _rng.Chance(0.55) ? Mobile(country) : null,
                PhoneHome = _rng.Chance(0.03) ? Landline(country, org.City) : null,
                EmailMain = _rng.Chance(0.93) && workInMain ? workEmail : null,
                EmailWork = _rng.Chance(0.93) && !workInMain ? workEmail : null,
                EmailHome = _rng.Chance(0.08) ? $"{mailFirst}.{mailLast}{_rng.Between(1, 99)}@{_rng.Pick(PersonalMailDomains)}" : null,
                Comments = remarks.Count > 0 ? string.Join(Environment.NewLine, remarks) : null,
            };
        }

        var info = new ContactInfo(contact, org);
        org.Contacts.Add(info);
        _contacts.Add(info);
        Link(contact.Id, RecordType.Contact, org.Id, RecordType.Organization);
    }

    // ------------------------------------------------------------------ items: devices, licences, services

    private void CreateItems()
    {
        var owners = new WeightedPicker<Org>(_orgs.Where(o => o.IsClient).ToList(),
            o => o.Role == OrgRole.Client ? o.Weight : o.Weight * 0.3);
        var vendors = _orgs.Where(o => o.Role == OrgRole.Vendor).ToList();
        var kinds = new WeightedPicker<ItemKind>(ItemKinds, k => k.Weight);

        while (_items.Count < _count)
        {
            var owner = owners.IsEmpty || _rng.Chance(0.04) ? null : owners.Pick(_rng);
            var kind = kinds.Pick(_rng);
            if (kind.OncePerOrg && (owner == null || owner.ItemNumbers.ContainsKey(kind.Prefix)))
            {
                kind = Licences;
            }

            var code = owner?.Code ?? "LAB";
            var numbers = owner?.ItemNumbers ?? _labNumbers;
            var number = numbers[kind.Prefix] = numbers.GetValueOrDefault(kind.Prefix) + 1;
            var model = kind.OncePerOrg ? owner!.Domain : _rng.Pick(kind.Models);
            var itemName = kind.Prefix switch
            {
                "DOM" => $"DOM {model}",
                "CRT" => $"CRT *.{model}",
                "NB" or "PC" or "MOB" or "TEL" => $"{kind.Prefix}-{code}-{number:D3} {model}",
                _ => $"{kind.Prefix}-{code}-{number:D2} {model}",
            };

            var assigned = kind.Assignable && owner is { Contacts.Count: > 0 } && _rng.Chance(0.75) ? _rng.Pick(owner.Contacts) : null;
            var vendor = (kind.Hardware || kind.Prefix == "LIC") && vendors.Count > 0 && _rng.Chance(0.6) ? _rng.Pick(vendors) : null;
            var item = new Item
            {
                Id = _rng.Guid(),
                OwnerDbId = OwnerDbId,
                ItemName = Clip(itemName, TitleLength.Item),
                Comments = ItemComments(kind, model, owner, assigned, vendor, number),
            };

            var info = new ItemInfo(item, kind, owner, model, assigned);
            _items.Add(info);
            owner?.Items.Add(info);
            vendor?.SuppliedItems.Add(info);

            if (owner != null)
            {
                Link(item.Id, RecordType.Item, owner.Id, RecordType.Organization);
            }
            if (assigned != null)
            {
                Link(item.Id, RecordType.Item, assigned.Record.Id, RecordType.Contact);
            }
            if (vendor != null && _rng.Chance(0.8))
            {
                Link(item.Id, RecordType.Item, vendor.Id, RecordType.Organization);
            }
            LinkTo(item, kind.OncePerOrg ? TopicTag(kind.Area, kind.Topic) : TechTag(model));
        }
    }

    private readonly Dictionary<string, int> _labNumbers = [];

    private string ItemComments(ItemKind kind, string model, Org? owner, ContactInfo? assigned, Org? vendor, int number)
    {
        var sb = new StringBuilder();
        var from = owner == null ? _today.AddYears(-6) : Max(owner.Since, _today.AddYears(-7));
        var to = owner?.Until ?? _today;
        var purchased = _rng.DateBetween(from, to);
        var money = (int eur) => owner == null ? $"€{eur:N0}" : Money(owner, eur);
        var ownerLabel = owner?.Name ?? "Own lab / test equipment";

        switch (kind.Prefix)
        {
            case "LIC":
                var seats = Math.Max(1, owner == null ? 1 : (int)(owner.Employees * (0.3 + _rng.Double() * 0.8)));
                var annual = _rng.Chance(0.6);
                sb.AppendLine($"Licence: {model}");
                sb.AppendLine($"Owner: {ownerLabel}");
                sb.AppendLine($"Seats: {seats} · Billing: {(annual ? "annual" : "monthly")} · {money(_rng.Between(3, 45))} per seat/month");
                sb.AppendLine($"Start: {Iso(purchased)} · Next renewal: {Iso(NextAnniversary(purchased, annual ? 12 : 1))}");
                sb.AppendLine($"Bought via: {vendor?.Name ?? "Direct from the manufacturer"}");
                if (model.Contains("365") || model.Contains("Exchange") || model.Contains("Defender"))
                {
                    sb.AppendLine($"Tenant: {(owner?.Domain.Split('.')[0] ?? "lab")}.onmicrosoft.com");
                }
                break;

            case "SVC":
                sb.AppendLine($"Service: {model}");
                sb.AppendLine($"Client: {ownerLabel}");
                sb.AppendLine($"Contract start: {Iso(purchased)} · Term: {_rng.Pick(new[] { "12 months", "24 months", "36 months", "monthly, 3 months notice" })}");
                sb.AppendLine($"Monthly fee: {money(_rng.Between(4, 120) * 10)}");
                sb.AppendLine($"Includes: {_rng.Pick(new[] { "monitoring, patching, monthly report", "setup, daily checks, restore tests twice a year", "remote support Mon–Fri 8–17", "licence management and user changes" })}");
                break;

            case "DOM":
                sb.AppendLine($"Domain: {model}");
                sb.AppendLine($"Registrar: {_rng.Pick(Registrars)} · Registered: {Iso(purchased)} · Expires: {Iso(NextAnniversary(purchased, 12))}");
                sb.AppendLine($"DNS hosted at: {_rng.Pick(new[] { "Cloudflare", "registrar", "Azure DNS", "hosting provider" })}");
                sb.AppendLine($"MX: {_rng.Pick(new[] { "Microsoft 365", "Microsoft 365 via email security gateway", "Google Workspace", "hosting provider" })}");
                sb.AppendLine($"SPF: ok · DKIM: {_rng.Pick(new[] { "ok", "ok", "missing" })} · DMARC: {_rng.Pick(new[] { "p=reject", "p=quarantine", "p=none", "missing" })}");
                sb.AppendLine($"Auto-renew: {(_rng.Chance(0.8) ? "on" : "OFF — check before expiry")}");
                break;

            case "CRT":
                sb.AppendLine($"Certificate: *.{model}");
                sb.AppendLine($"Issuer: {_rng.Pick(new[] { "Let's Encrypt", "Sectigo", "DigiCert", "GlobalSign" })}");
                sb.AppendLine($"Valid until: {Iso(_today.AddDays(_rng.Between(-20, 380)))}");
                sb.AppendLine($"Installed on: {_rng.Pick(new[] { "web server, RD gateway", "firewall SSL-VPN", "Exchange, RD gateway", "reverse proxy" })}");
                sb.AppendLine($"Renewal: {(_rng.Chance(0.6) ? "automatic (ACME)" : "manual — reminder in calendar")}");
                break;

            default:
                var warrantyYears = _rng.Pick(new[] { 1, 3, 3, 5 });
                var warrantyEnd = purchased.AddYears(warrantyYears);
                sb.AppendLine($"{kind.Kind}: {model}");
                sb.AppendLine($"Owner: {ownerLabel}");
                sb.AppendLine($"Serial number: {_rng.Letters(2)}{_rng.Digits(2)}{_rng.Letters(1)}{_rng.Digits(5)}");
                sb.AppendLine($"Purchased: {Iso(purchased)}{(vendor != null ? $" from {vendor.Name}" : "")}, {money(PriceOf(kind))}");
                sb.AppendLine($"Warranty until: {Iso(warrantyEnd)}{(warrantyEnd < _today ? " (expired)" : "")}");
                if (kind.Prefix is "NB" or "PC")
                {
                    sb.AppendLine($"OS: {_rng.Pick(model.StartsWith("Mac") || model.StartsWith("iMac") ? OperatingSystems.Skip(3).Take(2).ToList() : OperatingSystems.Take(3).ToList())}");
                    sb.AppendLine($"Encryption: {(_rng.Chance(0.85) ? "BitLocker / FileVault on, key escrowed" : "not encrypted — to do")}");
                }
                else if (kind.Prefix is "SRV")
                {
                    sb.AppendLine($"OS: {_rng.Pick(OperatingSystems.Skip(5).ToList())} · Roles: {_rng.Pick(new[] { "Hyper-V host", "DC, file, print", "ERP database", "RDS host", "backup server" })}");
                    sb.AppendLine($"IP: {owner?.Subnet ?? "192.168"}.{_rng.Between(2, 20)}");
                    sb.AppendLine($"iDRAC/iLO: {owner?.Subnet ?? "192.168"}.{_rng.Between(21, 30)}");
                }
                else if (kind.Prefix is "FW" or "SW" or "AP" or "RT" or "NAS" or "PRN" or "UPS")
                {
                    sb.AppendLine($"IP: {owner?.Subnet ?? "192.168"}.{_rng.Between(1, 60)} · Firmware: {_rng.Between(1, 9)}.{_rng.Between(0, 9)}.{_rng.Between(0, 20)}");
                }
                if (kind.Prefix is not ("NB" or "PC" or "MOB" or "TEL"))
                {
                    sb.AppendLine($"Location: {_rng.Pick(Locations)}");
                }
                if (assigned != null)
                {
                    sb.AppendLine($"Assigned to: {assigned.FullName}");
                }
                if (kind.Prefix is "SRV" or "FW" or "SW" or "NAS" or "RT" or "AP")
                {
                    sb.AppendLine($"Admin credentials: password manager, entry \"{owner?.Code ?? "LAB"} {kind.Prefix}-{number:D2}\"");
                }
                break;
        }
        return sb.ToString().TrimEnd();
    }

    private int PriceOf(ItemKind kind) => kind.Prefix switch
    {
        "SRV" => _rng.Between(35, 120) * 100,
        "NB" => _rng.Between(90, 240) * 10,
        "PC" => _rng.Between(70, 180) * 10,
        "FW" => _rng.Between(6, 40) * 100,
        "SW" => _rng.Between(4, 30) * 100,
        "AP" => _rng.Between(15, 60) * 10,
        "RT" => _rng.Between(20, 90) * 10,
        "NAS" => _rng.Between(8, 45) * 100,
        "PRN" => _rng.Between(40, 400) * 10,
        "UPS" => _rng.Between(30, 180) * 10,
        "MOB" => _rng.Between(35, 120) * 10,
        _ => _rng.Between(15, 40) * 10,
    };

    // ------------------------------------------------------------------ categories: client projects

    private void CreateProjects()
    {
        var target = _count - _categories.Count;
        var clients = _orgs.Where(o => o.IsClient).ToList();
        if (target <= 0 || clients.Count == 0)
        {
            return;
        }
        var picker = new WeightedPicker<Org>(clients, o => o.Role == OrgRole.Client ? o.Weight : o.Weight * 0.4);

        for (var i = 0; i < target; i++)
        {
            var org = picker.Pick(_rng);
            var type = _rng.Pick(ProjectTypes);
            var (from, to) = Window(org);
            if (org.Until == null)
            {
                to = _today.AddDays(90);
            }
            var start = _rng.DateBetween(from, to);
            var end = start.AddDays(_rng.Between(10, 180));
            if (org.Until is { } until && end > until)
            {
                end = until;
            }
            var status = start > _today ? "Planned"
                : end < _today ? _rng.Double() switch { < 0.9 => "Completed", < 0.95 => "Cancelled", _ => "On hold" }
                : _rng.Chance(0.88) ? "In progress" : "On hold";

            var project = new ProjectInfo
            {
                Org = org,
                Type = type,
                Start = start,
                End = end,
                Status = status,
                Hours = _rng.Between(8, 160),
                Rate = _rng.Between(19, 30) * 5,
                Pricing = _rng.Pick(new[] { "fixed price", "time and materials", "time and materials", "fixed price per user" }),
                Lead = org.Contacts.Count > 0 ? _rng.Pick(org.Contacts) : null,
            };
            _projects.Add(project);
            org.Projects.Add(project);
        }

        // Project codes are numbered in start order, per year, as they would be in real life.
        var sequence = new Dictionary<int, int>();
        foreach (var project in _projects.OrderBy(p => p.Start))
        {
            var n = sequence[project.Start.Year] = sequence.GetValueOrDefault(project.Start.Year) + 1;
            project.Code = $"PRJ-{project.Start.Year}-{n:D4}";
        }

        foreach (var p in _projects)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Client: {p.Org.Name}");
            sb.AppendLine($"Status: {p.Status}");
            sb.AppendLine($"Start: {Iso(p.Start)} · {(p.Status == "Completed" ? "Finished" : "Planned end")}: {Iso(p.End)}");
            sb.AppendLine($"Budget: {Money(p.Org, RoundTo(p.Hours * p.Rate, 50))} ({p.Pricing}, {p.Hours} h)");
            if (p.Lead != null)
            {
                sb.AppendLine($"Client lead: {p.Lead.WithTitle}");
            }
            sb.AppendLine("Scope:");
            foreach (var line in p.Type.Scope)
            {
                sb.AppendLine($"- {line}");
            }
            if (p.Status == "Cancelled")
            {
                sb.AppendLine($"Cancelled: {_rng.Pick(new[] { "budget frozen", "client chose another solution", "merged into a larger project", "postponed indefinitely" })}.");
            }

            p.Record = new Category { Id = _rng.Guid(), OwnerDbId = OwnerDbId, CategoryName = p.Name, Comments = sb.ToString().TrimEnd() };
            _categories.Add(p.Record);

            Link(p.Record.Id, RecordType.Category, p.Org.Id, RecordType.Organization);
            if (p.Lead != null)
            {
                Link(p.Record.Id, RecordType.Category, p.Lead.Record.Id, RecordType.Contact);
            }
            LinkTo(p.Record, TopicTag(p.Type.Area, p.Type.Topic));
            LinkTo(p.Record, Tag(p.Status switch
            {
                "Completed" or "Cancelled" => "Status: Done",
                "In progress" => "Status: In progress",
                "Planned" => "Status: Open",
                _ => "Status: Follow-up",
            }));
        }
    }

    // ------------------------------------------------------------------ notes

    private void CreateNotes()
    {
        var active = Picker(o => o.IsClient && o.Contacts.Count > 0 && (o.Until ?? _today) > _windowStart.AddDays(30),
            o => o.Role == OrgRole.Client ? o.Weight : o.Weight * 0.4);
        var prospects = Picker(o => o.Role == OrgRole.Prospect && o.Contacts.Count > 0, o => 1);
        var vendors = Picker(o => o.Role == OrgRole.Vendor && o.Contacts.Count > 0, o => 1 + o.SuppliedItems.Count);
        var partners = Picker(o => o.Role == OrgRole.Partner && o.Contacts.Count > 0, o => 1);
        var projects = _projects.Where(p => p.Start <= _today && p.End >= _windowStart).ToList();

        var builders = new List<(Func<bool> Build, double Weight)>
        {
            (() => !active.IsEmpty && CallNote(active.Pick(_rng)), 17),
            (() => !active.IsEmpty && TicketNote(active.Pick(_rng)), 14),
            (() => !active.IsEmpty && MeetingNote(active.Pick(_rng)), 13),
            (() => !active.IsEmpty && MaintenanceNote(active.Pick(_rng)), 8),
            (() => projects.Count > 0 && ProjectNote(_rng.Pick(projects)), 14),
            (() => _projects.Count > 0 && QuoteNote(_rng.Pick(_projects)), 6),
            (() => !active.IsEmpty && InvoiceNote(active.Pick(_rng)), 5),
            (() => KnowledgeNote(), 1.5),
            (() => !prospects.IsEmpty && ProspectNote(prospects.Pick(_rng)), 6),
            (() => !vendors.IsEmpty && VendorNote(vendors.Pick(_rng)), 5),
            (() => !partners.IsEmpty && projects.Count > 0 && PartnerNote(partners.Pick(_rng), _rng.Pick(projects)), 3),
            (() => !active.IsEmpty && QuickNote(active.Pick(_rng)), 4),
            (() => BusinessNote(), 3.5),
        };
        var picker = new WeightedPicker<(Func<bool> Build, double Weight)>(builders, b => b.Weight);

        for (var attempts = 0; _notes.Count < _count && attempts < _count * 20; attempts++)
        {
            picker.Pick(_rng).Build();
        }

        NumberDocuments();
    }

    private WeightedPicker<Org> Picker(Func<Org, bool> filter, Func<Org, double> weight) =>
        new(_orgs.Where(filter).ToList(), weight);

    /// <summary>Tickets, quotes and invoices are numbered in date order, per year.</summary>
    private void NumberDocuments()
    {
        var sequence = new Dictionary<(string, int), int>();
        var ordered = _notes.Select((note, index) => (note, index)).OrderBy(x => x.note.CreateDateTime).ToList();
        foreach (var (note, index) in ordered)
        {
            if (!_numberedNotes.TryGetValue(note.Id, out var kind))
            {
                continue;
            }
            var year = EpochTime.ToUtcDate(note.CreateDateTime).Year;
            var n = sequence[(kind, year)] = sequence.GetValueOrDefault((kind, year)) + 1;
            var number = n.ToString("D4", Inv);
            _notes[index] = note with
            {
                Title = note.Title?.Replace(NumberToken, number),
                TextContents = note.TextContents?.Replace(NumberToken, number),
            };
        }
    }

    private bool CallNote(Org org)
    {
        var contact = _rng.Pick(org.Contacts);
        var issue = _rng.Pick(Issues);
        var day = RecentDay(org);
        var time = WorkTime();
        var item = FindItem(org, issue.ItemPrefix);
        var inbound = _rng.Chance(0.7);

        var sb = new StringBuilder();
        sb.AppendLine($"{(inbound ? "Call from" : "Called")} {contact.WithTitle}, {Sentence(org.Name)} {Clock(time)}, {_rng.Pick(new[] { 5, 10, 10, 15, 20, 30, 45 })} min.");
        sb.AppendLine();
        sb.AppendLine($"Problem: {issue.Title}.");
        if (item != null)
        {
            sb.AppendLine($"Device: {item.Record.ItemName}");
        }
        sb.AppendLine($"Cause: {_rng.Pick(issue.Findings)}");
        sb.AppendLine($"Done: {_rng.Pick(issue.Fixes)}");
        if (_rng.Chance(0.35))
        {
            sb.AppendLine();
            sb.AppendLine($"Follow-up: {_rng.Pick(FollowUps)} by {Day(day.AddDays(_rng.Between(1, 10)))}.");
        }
        sb.AppendLine();
        sb.Append($"Time: {_rng.Pick(new[] { "0.25", "0.25", "0.5", "0.5", "0.75", "1" })} h · {Billing(org)}");

        var note = AddNote(Fit(TitleLength.Note,
            $"Call {contact.FullName}: {issue.Title}", $"Call with {contact.First} – {issue.Title}",
            $"{org.Short}: {issue.Title}", $"Call {org.Short}: {issue.Title}"), sb.ToString(), day, time);
        LinkNote(note, org, contact);
        LinkNote(note, item, 0.8);
        LinkNote(note, TopicTag(issue.Area, issue.Topic), 0.5);
        LinkNote(note, Tag(_rng.Pick(new[] { "Status: Done", "Status: Follow-up", "Billing: Billable" })), 0.2);
        return true;
    }

    private bool TicketNote(Org org)
    {
        var contact = _rng.Pick(org.Contacts);
        var issue = _rng.Pick(Issues);
        var day = RecentDay(org);
        var time = _rng.Chance(0.1) ? TimeSpan.FromMinutes(_rng.Between(19 * 60, 23 * 60)) : WorkTime();
        var item = FindItem(org, issue.ItemPrefix);
        var priority = _rng.Pick(new[] { "Low", "Normal", "Normal", "Normal", "High", "High", "Critical" });
        var ticket = $"T-{day.Year}-{NumberToken}";
        var steps = new[] { 0, _rng.Between(5, 30), _rng.Between(35, 90), _rng.Between(95, 240) };

        var sb = new StringBuilder();
        sb.AppendLine($"Ticket {ticket} · Priority {priority}");
        sb.AppendLine($"Reported by {contact.FullName} via {_rng.Pick(new[] { "phone", "e-mail", "support portal", "Teams" })}.");
        sb.AppendLine($"Affected: {item?.Record.ItemName ?? _rng.Pick(new[] { "one user", "several users", "the whole office" })}");
        sb.AppendLine();
        sb.AppendLine($"{Clock(time + TimeSpan.FromMinutes(steps[0]))}  Ticket opened: {issue.Title}.");
        sb.AppendLine($"{Clock(time + TimeSpan.FromMinutes(steps[1]))}  {_rng.Pick(new[] { "Remote session started.", "Checked monitoring and logs.", "Called the user back." })}");
        sb.AppendLine($"{Clock(time + TimeSpan.FromMinutes(steps[2]))}  Cause: {_rng.Pick(issue.Findings)}");
        sb.AppendLine($"{Clock(time + TimeSpan.FromMinutes(steps[3]))}  {_rng.Pick(issue.Fixes)} {_rng.Pick(new[] { "User confirmed, ticket closed.", "Monitoring again, closed.", "Left open until tomorrow to check." })}");
        sb.AppendLine();
        sb.Append($"Time spent: {Math.Ceiling(steps[3] / 15.0) / 4:0.##} h · {Billing(org)}");

        var note = AddNote(Fit(TitleLength.Note,
            $"Ticket {ticket}: {issue.Title}", $"{ticket} {org.Short}: {issue.Title}", $"Ticket {ticket} – {org.Short}"),
            sb.ToString(), day, time);
        _numberedNotes[note.Id] = "T";
        LinkNote(note, org, contact);
        LinkNote(note, item, 0.9);
        LinkNote(note, Tag($"Priority: {priority}"), 0.5);
        LinkNote(note, TopicTag(issue.Area, issue.Topic), 0.5);
        LinkNote(note, Tag("Support: Helpdesk"), 0.15);
        return true;
    }

    private bool MeetingNote(Org org)
    {
        var topic = _rng.Pick(MeetingTopics);
        var attendees = _rng.PickDistinct(org.Contacts, _rng.Between(1, Math.Min(3, org.Contacts.Count)));
        var day = RecentDay(org);
        var time = TimeSpan.FromMinutes(_rng.Between(16, 34) * 30);
        var onsite = _rng.Chance(0.5);

        var sb = new StringBuilder();
        sb.AppendLine($"{(onsite ? $"Onsite at {org.Name}, {org.City.Name}" : _rng.Pick(new[] { "Teams meeting", "Zoom call", "Video call" }))}, {Clock(time)}–{Clock(time + TimeSpan.FromMinutes(_rng.Pick(new[] { 30, 45, 60, 90, 120 })))}");
        sb.AppendLine($"Attendees: {string.Join(", ", attendees.Select(a => a.WithTitle))}, me");
        sb.AppendLine();
        sb.AppendLine("Discussed:");
        foreach (var point in _rng.PickDistinct(topic.Points, _rng.Between(2, topic.Points.Length)))
        {
            sb.AppendLine($"- {point}");
        }
        if (org.Sector != null && _rng.Chance(0.4))
        {
            sb.AppendLine($"- Also talked about the {_rng.Pick(org.Sector.Systems)}.");
        }
        sb.AppendLine();
        sb.AppendLine("Next steps:");
        foreach (var action in _rng.PickDistinct(topic.Actions, _rng.Between(1, topic.Actions.Length)))
        {
            var owner = _rng.Chance(0.7) ? "me" : _rng.Pick(attendees).First;
            sb.AppendLine($"- [{(day.AddDays(14) < _today ? "x" : " ")}] {action} ({owner}, by {Day(day.AddDays(_rng.Between(3, 21)))})");
        }

        var note = AddNote(Fit(TitleLength.Note,
            $"{org.Short}: {topic.Title}", $"Meeting {attendees[0].FullName} – {topic.Title}", $"{topic.Title} – {org.Short}"),
            sb.ToString().TrimEnd(), day, time);
        LinkNote(note, org);
        foreach (var attendee in attendees)
        {
            LinkNote(note, attendee);
        }
        LinkNote(note, Tag(topic.Area), 0.5);
        if (topic.Title == "Kick-off meeting" && org.Projects.Count > 0)
        {
            LinkNote(note, _rng.Pick(org.Projects).Record);
        }
        return true;
    }

    private bool MaintenanceNote(Org org)
    {
        var day = RecentDay(org);
        var time = TimeSpan.FromMinutes(_rng.Pick(new[] { 7 * 60, 12 * 60 + 30, 18 * 60, 19 * 60 + 30 }));
        var infra = org.Items.Where(i => i.Kind.Prefix is "SRV" or "FW" or "NAS" or "SW").ToList();
        var servers = Math.Max(1, org.Items.Count(i => i.Kind.Prefix == "SRV"));
        var clients = Math.Max(2, org.Items.Count(i => i.Kind.Prefix is "NB" or "PC"));
        var jobs = _rng.Between(2, 8);
        var failed = _rng.Chance(0.15) ? 1 : 0;
        var firewall = infra.FirstOrDefault(i => i.Kind.Prefix == "FW");

        var sb = new StringBuilder();
        sb.AppendLine($"Maintenance window {Clock(time)}–{Clock(time + TimeSpan.FromMinutes(_rng.Pick(new[] { 60, 90, 120 })))} (remote).");
        sb.AppendLine();
        sb.AppendLine($"- Windows updates: {servers} server(s) and {clients} clients, {_rng.Between(0, 3)} reboot(s) pending");
        sb.AppendLine($"- Backup: {jobs - failed} of {jobs} jobs successful{(failed > 0 ? " — failed job re-run, OK now" : "")}");
        if (firewall != null)
        {
            sb.AppendLine($"- Firewall {firewall.Record.ItemName.Split(' ')[0]}: {(_rng.Chance(0.7) ? "firmware up to date" : "firmware update available, planned for next window")}");
        }
        sb.AppendLine($"- Disk space: C: {_rng.Between(18, 70)} % free, D: {_rng.Between(8, 60)} % free");
        sb.AppendLine($"- Endpoint protection: {clients} devices protected, {(_rng.Chance(0.85) ? "no detections" : "1 detection quarantined")}");
        sb.AppendLine($"- Event logs checked{(_rng.Chance(0.8) ? ", no critical errors" : "; recurring warning from the RAID controller, keep an eye on it")}.");
        sb.AppendLine();
        sb.Append(failed > 0 || _rng.Chance(0.2) ? "Result: action needed, see above." : "Result: all OK.");

        var note = AddNote(Fit(TitleLength.Note,
            $"Monthly maintenance – {org.Short}", $"Patch day {day.ToString("MMMM yyyy", Inv)}: {org.Short}",
            $"{org.Short}: maintenance {day.ToString("MMM yyyy", Inv)}"), sb.ToString(), day, time);
        LinkNote(note, org);
        foreach (var item in _rng.PickDistinct(infra, Math.Min(3, infra.Count)))
        {
            LinkNote(note, item);
        }
        LinkNote(note, Tag("Support: Maintenance windows"), 0.6);
        LinkNote(note, Tag("Security: Patch management"), 0.25);
        return true;
    }

    private bool ProjectNote(ProjectInfo project)
    {
        var org = project.Org;
        var from = Max(project.Start, _windowStart);
        var to = Min(project.End, _today);
        if (from > to)
        {
            return false;
        }
        var day = WeekdayWithin(_rng.DateBetween(from, to), from, to);
        var time = WorkTime();
        var elapsed = (day.DayNumber - project.Start.DayNumber) / (double)Math.Max(1, project.End.DayNumber - project.Start.DayNumber);
        var phase = ProjectPhases[Math.Min(ProjectPhases.Length - 1, (int)(elapsed * ProjectPhases.Length))];
        var progress = Math.Min(100, (int)Math.Round(elapsed * 100 / 5) * 5 + _rng.Between(0, 10));
        var hours = _rng.Between(2, 16);

        var sb = new StringBuilder();
        sb.AppendLine($"Project: {project.Name} ({org.Name})");
        sb.AppendLine($"Phase: {phase} · Progress: {progress} %");
        sb.AppendLine();
        sb.AppendLine("Done:");
        foreach (var line in _rng.PickDistinct(project.Type.Scope, _rng.Between(1, 2)))
        {
            sb.AppendLine($"- {line}: {_rng.Pick(new[] { "finished", "mostly done", "started", "tested", "documented" })}");
        }
        sb.AppendLine();
        sb.AppendLine("Open points:");
        foreach (var point in _rng.PickDistinct(OpenPoints, _rng.Between(1, 2)))
        {
            sb.AppendLine($"- {point}");
        }
        sb.AppendLine();
        sb.Append($"Hours this week: {hours} h (budget {project.Hours} h, {project.Pricing})");

        var note = AddNote(Fit(TitleLength.Note,
            $"{project.Code}: {phase}", $"{project.Code} {project.Type.Name} – {phase}", $"{org.Short}: {project.Type.Name} ({phase})"),
            sb.ToString(), day, time);
        LinkNote(note, project.Record);
        LinkNote(note, org);
        if (project.Lead != null)
        {
            LinkNote(note, project.Lead, 0.7);
        }
        if (org.Items.Count > 0)
        {
            LinkNote(note, _rng.Pick(org.Items), 0.3);
        }
        LinkNote(note, TopicTag(project.Type.Area, project.Type.Topic), 0.3);
        return true;
    }

    private bool QuoteNote(ProjectInfo project)
    {
        var org = project.Org;
        var day = WeekdayWithin(project.Start.AddDays(-_rng.Between(5, 45)), _windowStart, _today);
        if (day < _windowStart || day > _today || day < org.Since.AddDays(-60))
        {
            return false;
        }
        var time = WorkTime();
        var lines = new List<(string Text, int Amount)>();
        var planning = Math.Max(2, project.Hours / 5);
        var docs = Math.Max(1, project.Hours / 10);
        var implementation = Math.Max(2, project.Hours - planning - docs);
        lines.Add(($"Analysis and planning: {planning} h × {Money(org, project.Rate)}", planning * project.Rate));
        lines.Add(($"Implementation: {implementation} h × {Money(org, project.Rate)}", implementation * project.Rate));
        lines.Add(($"Documentation and handover: {docs} h × {Money(org, project.Rate)}", docs * project.Rate));
        if (_rng.Chance(0.4))
        {
            var kind = _rng.Pick(ItemKinds.Where(k => k.Hardware).ToList());
            var qty = _rng.Between(1, kind.Prefix is "NB" or "PC" or "AP" ? 12 : 2);
            lines.Add(($"{qty} × {_rng.Pick(kind.Models)}", qty * PriceOf(kind)));
        }
        var status = project.Status switch
        {
            "Cancelled" => "Declined",
            "Planned" when _rng.Chance(0.5) => "Sent, waiting for answer",
            _ => $"Accepted on {Day(Min(project.Start.AddDays(-_rng.Between(1, 4)), _today))}",
        };

        var sb = new StringBuilder();
        sb.AppendLine($"Quote Q-{day.Year}-{NumberToken} for {org.Name}");
        if (project.Lead != null)
        {
            sb.AppendLine($"Attn: {project.Lead.WithTitle}");
        }
        sb.AppendLine($"Subject: {project.Type.Name} ({project.Code})");
        sb.AppendLine();
        for (var i = 0; i < lines.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {lines[i].Text} = {Money(org, lines[i].Amount)}");
        }
        sb.AppendLine($"Total (net): {Money(org, lines.Sum(l => l.Amount))}");
        sb.AppendLine();
        sb.AppendLine($"Valid for 30 days. {_rng.Pick(new[] { "50 % on order, 50 % on completion.", "Invoiced monthly by effort.", "Payable on completion." })}");
        sb.Append($"Status: {status}");

        var note = AddNote(Fit(TitleLength.Note,
            $"Quote Q-{day.Year}-{NumberToken}: {project.Type.Name}", $"Quote Q-{day.Year}-{NumberToken} – {org.Short}"),
            sb.ToString(), day, time);
        _numberedNotes[note.Id] = "Q";
        LinkNote(note, org);
        LinkNote(note, project.Record);
        if (project.Lead != null)
        {
            LinkNote(note, project.Lead);
        }
        LinkNote(note, Tag("Business: Quotes"), 0.5);
        return true;
    }

    private bool InvoiceNote(Org org)
    {
        var day = RecentDay(org);
        var time = WorkTime();
        var period = day.AddMonths(-1);
        var rate = _rng.Between(19, 30) * 5;
        var extraHours = _rng.Between(0, 14);
        var retainer = _rng.Between(20, 200) * 10;
        var total = retainer + extraHours * rate;
        var due = day.AddDays(_rng.Pick(new[] { 14, 14, 30 }));
        var finance = org.Contacts.FirstOrDefault(c => c.Record.JobTitle is { } t &&
            (t.Contains("Finance") || t.Contains("Account") || t.Contains("CFO") || t.Contains("Controller") || t.Contains("Office")));

        var sb = new StringBuilder();
        sb.AppendLine($"Invoice INV-{day.Year}-{NumberToken} to {org.Name}");
        sb.AppendLine($"Period: {period.ToString("MMMM yyyy", Inv)}");
        sb.AppendLine();
        sb.AppendLine($"- Managed services / retainer: {Money(org, retainer)}");
        if (extraHours > 0)
        {
            sb.AppendLine($"- Additional support: {extraHours} h × {Money(org, rate)} = {Money(org, extraHours * rate)}");
        }
        if (_rng.Chance(0.25) && org.Items.Count > 0)
        {
            var item = _rng.Pick(org.Items);
            var price = PriceOf(item.Kind);
            total += price;
            sb.AppendLine($"- Hardware / licences: {item.Model} = {Money(org, price)}");
        }
        sb.AppendLine($"Total (net): {Money(org, total)}, plus VAT");
        sb.AppendLine($"Due: {Day(due)}");
        sb.Append($"Status: {(due < _today ? _rng.Double() switch
        {
            < 0.88 => $"Paid on {Day(Min(due.AddDays(_rng.Between(-10, 12)), _today))}",
            < 0.97 => $"Reminder sent on {Day(Min(due.AddDays(7), _today))}, paid later",
            _ => "Overdue — second reminder sent",
        } : "Open")}");

        var note = AddNote(Fit(TitleLength.Note,
            $"Invoice INV-{day.Year}-{NumberToken} – {org.Short}", $"INV-{day.Year}-{NumberToken}: {org.Short}"),
            sb.ToString(), day, time);
        _numberedNotes[note.Id] = "INV";
        LinkNote(note, org);
        LinkNote(note, finance, 0.6);
        LinkNote(note, Tag("Business: Invoicing"), 0.5);
        return true;
    }

    private bool KnowledgeNote()
    {
        _kbPool ??= BuildKnowledgePool();
        if (_kbPool.Count == 0)
        {
            return false;
        }
        var (title, text, tag) = _kbPool.Dequeue();
        var day = RecentDay(_windowStart, _today);
        var note = AddNote(title, text, day, WorkTime());
        LinkNote(note, _tags.GetValueOrDefault(tag));
        var area = tag.Split(':')[0];
        LinkNote(note, Tag(area), 0.5);
        return true;
    }

    private Queue<(string, string, string)> BuildKnowledgePool()
    {
        var pool = new List<(string, string, string)>();
        foreach (var article in KbArticles)
        {
            foreach (var product in article.Products)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Applies to: {product}");
                sb.AppendLine();
                for (var i = 0; i < article.Steps.Length; i++)
                {
                    sb.AppendLine($"{i + 1}. {article.Steps[i]}");
                }
                sb.AppendLine();
                sb.Append($"Tip: {_rng.Pick(Tips)}");
                pool.Add((Clip("How-to: " + article.Title.Replace("{p}", product), TitleLength.Note), sb.ToString(),
                    $"{article.Area}: {article.Topic}"));
            }
        }
        foreach (var issue in Issues)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Seen at several clients. Causes so far:");
            foreach (var finding in issue.Findings)
            {
                sb.AppendLine($"- {finding}");
            }
            sb.AppendLine();
            sb.AppendLine("What worked:");
            foreach (var fix in issue.Fixes)
            {
                sb.AppendLine($"- {fix}");
            }
            pool.Add((Clip($"Lessons learned: {issue.Title}", TitleLength.Note), sb.ToString().TrimEnd(), $"{issue.Area}: {issue.Topic}"));
        }
        foreach (var type in ProjectTypes)
        {
            var sb = new StringBuilder();
            foreach (var line in type.Scope.Concat(ChecklistBasics))
            {
                sb.AppendLine($"- [ ] {line}");
            }
            pool.Add((Clip($"Checklist: {type.Name}", TitleLength.Note), sb.ToString().TrimEnd(), $"{type.Area}: {type.Topic}"));
        }
        return new Queue<(string, string, string)>(pool.OrderBy(_ => _rng.Double()));
    }

    private bool ProspectNote(Org org)
    {
        var contact = _rng.Pick(org.Contacts);
        var day = RecentDay(org);
        var time = WorkTime();
        var wants = _rng.PickDistinct(ProjectTypes, 2);

        var sb = new StringBuilder();
        sb.AppendLine($"Lead from {_rng.Pick(Referrals)}.");
        sb.AppendLine($"Contact: {contact.WithTitle}");
        sb.AppendLine($"{org.Name} · {org.Sector?.Name} · about {org.Employees} employees · {org.City.Name}");
        sb.AppendLine();
        sb.AppendLine("Current situation:");
        sb.AppendLine($"- IT is looked after by {_rng.Pick(new[] { "an employee in their spare time", "another IT provider", "a family member", "nobody in particular", "the software vendor" })}.");
        if (org.Sector != null)
        {
            sb.AppendLine($"- Uses {_rng.Pick(org.Sector.Systems)}.");
        }
        foreach (var pain in _rng.PickDistinct(PainPoints, 2))
        {
            sb.AppendLine($"- {pain}");
        }
        sb.AppendLine();
        sb.AppendLine($"Interested in: {wants[0].Name}, {wants[1].Name}");
        sb.Append($"Next step: {_rng.Pick(new[] { $"send a quote by {Day(day.AddDays(5))}", $"onsite assessment on {Day(day.AddDays(_rng.Between(3, 20)))}", "call again next quarter", "send references and price list" })}");

        var note = AddNote(Fit(TitleLength.Note,
            $"Intro call – {org.Short}", $"First meeting with {org.Short}", $"Lead: {org.Short}"), sb.ToString(), day, time);
        LinkNote(note, org, contact);
        LinkNote(note, Tag("Business: Leads"), 0.5);
        if (org.LeadTag != null)
        {
            LinkNote(note, Tag(org.LeadTag), 0.4);
        }
        return true;
    }

    private bool VendorNote(Org org)
    {
        var contact = _rng.Pick(org.Contacts);
        var subject = _rng.Pick(VendorSubjects);
        var day = RecentDay(org);
        var item = org.SuppliedItems.Count > 0 ? _rng.Pick(org.SuppliedItems) : null;
        var product = item?.Model ?? _rng.Pick(_rng.Pick(ItemKinds.Where(k => k.Models.Length > 0).ToList()).Models);

        var body = subject switch
        {
            "RMA request" => $"RMA for {item?.Record.ItemName ?? product} approved. Replacement ships in {_rng.Between(1, 7)} days; send the faulty unit back with the RMA label.",
            "Licence renewal" => $"Renewal quote for {product} received: {Money(org, _rng.Between(2, 80) * 100)} per year. Price up {_rng.Between(3, 15)} % from last year.",
            "Price list update" => $"New price list from {Day(day.AddDays(_rng.Between(5, 30)))}. {product} goes up by {_rng.Between(2, 12)} %.",
            "Delivery delay" => $"{product} is back-ordered, new ETA {Day(day.AddDays(_rng.Between(7, 45)))}. Informed the affected client.",
            "Partner webinar" => $"Webinar about the new {product} features. Worth offering to clients with older models.",
            "Support case escalation" => $"Vendor case {_rng.Digits(8)} for {product} escalated to second level. Waiting for a firmware fix.",
            "Demo unit request" => $"Requested a demo unit of {product} for a client test. Loan period 30 days.",
            "Certification exam voucher" => $"Received an exam voucher via the partner programme, valid until {Day(day.AddMonths(6))}.",
            _ => $"{subject}: talked about volumes for {product} and payment terms. Discount stays at {_rng.Between(5, 25)} %.",
        };

        var note = AddNote(Fit(TitleLength.Note, $"{org.Short}: {subject}", $"{subject} – {org.Short}"),
            $"{subject} with {contact.FullName} ({org.Name}), {day.ToString("d MMM", Inv)}.{Environment.NewLine}{Environment.NewLine}{body}",
            day, WorkTime());
        LinkNote(note, org, contact);
        LinkNote(note, item, 0.7);
        LinkNote(note, Tag("Business: Suppliers"), 0.4);
        return true;
    }

    private bool PartnerNote(Org partner, ProjectInfo project)
    {
        var contact = _rng.Pick(partner.Contacts);
        var from = Max(project.Start.AddDays(-20), _windowStart);
        var day = WeekdayWithin(_rng.DateBetween(from, Min(project.End, _today)), from, _today);
        var days = _rng.Between(1, 6);

        var sb = new StringBuilder();
        sb.AppendLine($"Asked {contact.FullName} ({partner.Name}) to support {project.Name} at {Sentence(project.Org.Name)}");
        sb.AppendLine($"Effort: {days} day(s) at {Money(partner, _rng.Between(55, 110) * 10)} per day.");
        sb.AppendLine($"Dates: {Day(day.AddDays(_rng.Between(3, 10)))} onwards.");
        sb.Append($"Needs: {_rng.Pick(new[] { "VPN access and a guest account", "a site contact and parking", "the network plan", "admin access for the duration of the job" })}. NDA on file.");

        var note = AddNote(Fit(TitleLength.Note,
            $"Subcontract: {partner.Short} for {project.Code}", $"{partner.Short} – {project.Type.Name}", $"Subcontract {project.Code}"),
            sb.ToString(), day, WorkTime());
        LinkNote(note, partner, contact);
        LinkNote(note, project.Record);
        LinkNote(note, project.Org);
        LinkNote(note, Tag("Business: Partners"), 0.3);
        return true;
    }

    private bool QuickNote(Org org)
    {
        var contact = _rng.Pick(org.Contacts);
        var item = org.Items.Count > 0 ? _rng.Pick(org.Items) : null;
        var text = _rng.Pick(QuickNotes)
            .Replace("{org}", org.Short)
            .Replace("{first}", contact.First)
            .Replace("{item}", item?.Record.ItemName ?? "the server")
            .Replace("{model}", item?.Model ?? _rng.Pick(Printers.Models))
            .Replace("{domain}", org.Domain)
            .Replace("{product}", _rng.Pick(Licences.Models));

        var note = AddNote(null, text, RecentDay(org), WorkTime());
        LinkNote(note, org, 0.8);
        if (text.Contains(contact.First))
        {
            LinkNote(note, contact, 0.6);
        }
        LinkNote(note, item, text.Contains(item?.Record.ItemName ?? "\0") ? 1 : 0);
        return true;
    }

    private bool BusinessNote()
    {
        var day = RecentDay(_windowStart, _today);
        var quarter = (day.Month - 1) / 3 + 1;
        var (title, text, tag) = _rng.Between(0, 7) switch
        {
            0 => ($"VAT return Q{quarter} {day.Year}", $"VAT return for Q{quarter} filed. Sent the invoices and receipts to the accountant. Next deadline: {Day(day.AddMonths(3))}.", "Business: Tax"),
            1 => ($"Training: {_rng.Pick(Certifications)}", $"Exam prep for {_rng.Pick(Certifications)}. Weak areas: {_rng.Pick(Taxonomy).Topics[0]}, licensing. Exam booked for {Day(day.AddDays(_rng.Between(14, 60)))}.", "Business: Certifications"),
            2 => ($"Newsletter {day.ToString("MMMM yyyy", Inv)}", $"Topics: {_rng.Pick(ProjectTypes).Name}, \"{_rng.Pick(Issues).Title}\", a client story. Send on the first Tuesday.", "Business: Marketing"),
            3 => ($"Liability insurance {day.Year}", $"Professional liability and cyber insurance renewed. Premium {_rng.Between(900, 2400):N0} per year. Questionnaire answered.", "Business: Insurance"),
            4 => ($"Time tracking {day.ToString("MMMM yyyy", Inv)}", $"Booked {_rng.Between(110, 170)} h, of which {_rng.Between(70, 90)} % billable. Biggest clients: {string.Join(", ", _rng.PickDistinct(_orgs.Where(o => o.Role == OrgRole.Client).Select(o => o.Short).ToList(), 3))}.", "Business: Time tracking"),
            5 => ($"Hourly rates {day.Year + 1}", $"Plan to raise the standard rate by {_rng.Between(3, 8)} % from January. Inform retainer clients {_rng.Between(6, 10)} weeks in advance.", "Business: Pricing"),
            6 => ($"Idea: {_rng.Pick(ServiceIdeas)}", $"Could be offered as a fixed monthly package. Check demand with three clients first.", "Business: Marketing"),
            _ => ($"Tools review {day.Year}", $"Reviewed my own tools: RMM, password manager, documentation, backup. Keep all; cancel the unused screen recorder.", "Business: Suppliers"),
        };
        var note = AddNote(Clip(title, TitleLength.Note), text, day, WorkTime());
        LinkNote(note, Tag(tag));
        return true;
    }

    // ------------------------------------------------------------------ note helpers

    private Note AddNote(string? title, string text, DateOnly day, TimeSpan time)
    {
        var (createDate, createDateTime) = NoteDates.ForNewNote(day, time);
        var note = new Note
        {
            Id = _rng.Guid(),
            OwnerDbId = OwnerDbId,
            Title = title,
            TextContents = text,
            CreateDate = createDate,
            CreateDateTime = createDateTime,
        };
        _notes.Add(note);
        return note;
    }

    private ItemInfo? FindItem(Org org, string prefix)
    {
        var matching = org.Items.Where(i => i.Kind.Prefix == prefix).ToList();
        return matching.Count > 0 ? _rng.Pick(matching) : null;
    }

    private (DateOnly From, DateOnly To) Window(Org org)
    {
        var from = Max(org.Since, _windowStart);
        var to = Min(org.Until ?? _today, _today);
        return from > to ? (to, to) : (from, to);
    }

    private DateOnly RecentDay(Org org)
    {
        var (from, to) = Window(org);
        return RecentDay(from, to);
    }

    /// <summary>A working day in the range, more often recent than long ago.</summary>
    private DateOnly RecentDay(DateOnly from, DateOnly to)
    {
        var span = to.DayNumber - from.DayNumber;
        var day = to.AddDays(-(int)(span * Math.Pow(_rng.Double(), 1.4)));
        return WeekdayWithin(day, from, to);
    }

    private DateOnly WeekdayWithin(DateOnly day, DateOnly from, DateOnly to)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday && _rng.Chance(0.9))
        {
            var friday = day.AddDays(day.DayOfWeek == DayOfWeek.Saturday ? -1 : -2);
            var monday = day.AddDays(day.DayOfWeek == DayOfWeek.Saturday ? 2 : 1);
            day = friday >= from ? friday : monday;
        }
        return Min(Max(day, from), to);
    }

    private TimeSpan WorkTime() => TimeSpan.FromMinutes(_rng.Between(7 * 60 + 30, 18 * 60 + 30));

    private string Billing(Org org) => org.Role == OrgRole.FormerClient
        ? "billable"
        : _rng.Pick(new[] { "included in retainer", "included in retainer", "billable", "billable", "not billable (goodwill)" });

    // ------------------------------------------------------------------ links

    private void Link(string id1, RecordType type1, string id2, RecordType type2)
    {
        if (id1 == id2)
        {
            return;
        }
        var key = string.CompareOrdinal(id1, id2) < 0 ? id1 + id2 : id2 + id1;
        if (!_linkKeys.Add(key))
        {
            return;
        }
        _links.Add(new LinkedRecord
        {
            Id = _rng.Guid(),
            OwnerDbId = OwnerDbId,
            Record1Id = id1,
            Record2Id = id2,
            Record1TypeId = (int)type1,
            Record2TypeId = (int)type2,
        });
    }

    private void LinkTo(Org org, Category? tag)
    {
        if (tag != null)
        {
            Link(org.Id, RecordType.Organization, tag.Id, RecordType.Category);
        }
    }

    private void LinkTo(Item item, Category? tag)
    {
        if (tag != null)
        {
            Link(item.Id, RecordType.Item, tag.Id, RecordType.Category);
        }
    }

    private void LinkTo(Category category, Category? tag)
    {
        if (tag != null)
        {
            Link(category.Id, RecordType.Category, tag.Id, RecordType.Category);
        }
    }

    private void LinkNote(Note note, Org org, ContactInfo? contact = null)
    {
        Link(note.Id, RecordType.Note, org.Id, RecordType.Organization);
        if (contact != null)
        {
            Link(note.Id, RecordType.Note, contact.Record.Id, RecordType.Contact);
        }
    }

    private void LinkNote(Note note, Org org, double chance)
    {
        if (_rng.Chance(chance))
        {
            LinkNote(note, org);
        }
    }

    private void LinkNote(Note note, ContactInfo? contact, double chance = 1)
    {
        if (contact != null && _rng.Chance(chance))
        {
            Link(note.Id, RecordType.Note, contact.Record.Id, RecordType.Contact);
        }
    }

    private void LinkNote(Note note, ItemInfo? item, double chance = 1)
    {
        if (item != null && _rng.Chance(chance))
        {
            Link(note.Id, RecordType.Note, item.Record.Id, RecordType.Item);
        }
    }

    private void LinkNote(Note note, Category? category, double chance = 1)
    {
        if (category != null && _rng.Chance(chance))
        {
            Link(note.Id, RecordType.Note, category.Id, RecordType.Category);
        }
    }

    // ------------------------------------------------------------------ formatting

    private string Landline(Country country, City city) => country.Code switch
    {
        "US" => $"+1 ({city.AreaCode}) 555-{_rng.Digits(4)}",
        "UK" => city.AreaCode.Length == 2 ? $"+44 {city.AreaCode} 7946 {_rng.Digits(4)}" : $"+44 {city.AreaCode} 496 {_rng.Digits(4)}",
        "DE" => $"+49 {city.AreaCode} {_rng.Between(1, 9)}{_rng.Digits(10 - city.AreaCode.Length)}",
        "AT" => $"+43 {city.AreaCode} {_rng.Between(1, 9)}{_rng.Digits(5)}",
        "CH" => $"+41 {city.AreaCode} {_rng.Between(2, 9)}{_rng.Digits(2)} {_rng.Digits(2)} {_rng.Digits(2)}",
        "NL" => $"+31 {city.AreaCode} {_rng.Between(2, 9)}{_rng.Digits(2)} {_rng.Digits(4)}",
        "PL" => $"+48 {city.AreaCode} {_rng.Between(2, 9)}{_rng.Digits(2)} {_rng.Digits(2)} {_rng.Digits(2)}",
        "IE" => $"+353 {city.AreaCode} {_rng.Between(2, 9)}{_rng.Digits(2)} {_rng.Digits(4)}",
        _ => $"+{_rng.Digits(2)} {_rng.Digits(8)}",
    };

    private string Mobile(Country country) => country.Code switch
    {
        "US" => $"+1 ({_rng.Pick(new[] { "201", "646", "773", "857", "720", "425", "470" })}) 555-{_rng.Digits(4)}",
        "UK" => $"+44 7700 900{_rng.Digits(3)}",
        "DE" => $"+49 {_rng.Pick(new[] { "151", "160", "170", "176", "157" })} {_rng.Digits(8)}",
        "AT" => $"+43 {_rng.Pick(new[] { "664", "676", "699", "650" })} {_rng.Digits(7)}",
        "CH" => $"+41 {_rng.Pick(new[] { "76", "78", "79" })} {_rng.Digits(3)} {_rng.Digits(2)} {_rng.Digits(2)}",
        "NL" => $"+31 6 {_rng.Digits(8)}",
        "PL" => $"+48 {_rng.Pick(new[] { "501", "601", "691", "721", "790" })} {_rng.Digits(3)} {_rng.Digits(3)}",
        "IE" => $"+353 {_rng.Pick(new[] { "83", "85", "86", "87" })} {_rng.Digits(3)} {_rng.Digits(4)}",
        _ => $"+{_rng.Digits(2)} {_rng.Digits(9)}",
    };

    private string DirectLine(Org org) =>
        org.Country.Code is "DE" or "AT" or "CH" ? $"{org.Phone}-{_rng.Between(10, 99)}" : Landline(org.Country, org.City);

    private static string Money(Org org, int amountInEuro)
    {
        var amount = (int)(Math.Round(amountInEuro * org.Country.CurrencyFactor / 5.0) * 5);
        return $"{org.Country.CurrencySymbol}{amount.ToString("N0", Inv)}";
    }

    private static int RoundTo(int value, int step) => (int)Math.Round(value / (double)step) * step;

    private static string Sentence(string text) => text.EndsWith('.') ? text : text + ".";

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", Inv);

    private static string Day(DateOnly date) => date.ToString("d MMM yyyy", Inv);

    private static string Clock(TimeSpan time) => $"{(int)time.TotalHours % 24:D2}:{time.Minutes:D2}";

    private DateOnly NextAnniversary(DateOnly start, int months)
    {
        var next = start;
        while (next <= _today)
        {
            next = next.AddMonths(months);
        }
        return next;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    /// <summary>Picks one of the titles that fits the maximum length, or shortens the first one.</summary>
    private string Fit(int max, params string[] candidates)
    {
        var fitting = candidates.Where(c => c.Length <= max).ToList();
        return fitting.Count > 0 ? _rng.Pick(fitting) : Clip(candidates[^1], max);
    }

    private static string Clip(string s, int max)
    {
        if (s.Length <= max)
        {
            return s;
        }
        var cut = s.LastIndexOf(' ', max);
        return (cut > max / 2 ? s[..cut] : s[..max]).TrimEnd(' ', '-', '–', ':', ',', '(');
    }

    private static string MailPart(string name) =>
        new(Ascii(name).ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    private static string Ascii(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'Ä' => "Ae", 'Ö' => "Oe", 'Ü' => "Ue", 'ß' => "ss",
                'ł' => "l", 'Ł' => "L", 'ó' => "o", 'Ó' => "O", 'ś' => "s", 'Ś' => "S", 'ż' => "z", 'Ż' => "Z",
                'ź' => "z", 'Ź' => "Z", 'ć' => "c", 'Ć' => "C", 'ń' => "n", 'ę' => "e", 'ą' => "a", 'é' => "e",
                _ => ch.ToString(),
            });
        }
        return sb.ToString();
    }

    private static readonly string[] FollowUps =
    [
        "Check that the fix held", "Send the user a short how-to", "Order a replacement part", "Update the documentation",
        "Schedule an onsite visit", "Ask the vendor for a firmware fix", "Add a monitoring check",
    ];

    private static readonly string[] OpenPoints =
    [
        "Waiting for the client to confirm the maintenance window.", "Vendor delivery delayed by a week.",
        "Two users still need training.", "Need admin credentials for the old system.", "Licence order pending approval.",
        "Change request for extra scope — quote needed.", "Old provider has not handed over the documentation yet.",
        "Internet line installation date not confirmed.", "Client wants to postpone go-live by two weeks.",
        "Test user found an issue with the line-of-business app.",
    ];

    private static readonly string[] PainPoints =
    [
        "Backups have never been tested.", "Internet line drops several times a week.", "No one knows the admin passwords.",
        "Server is from 2015 and out of warranty.", "Staff use personal devices for work e-mail.",
        "Cyber insurance now requires MFA.", "Had a phishing incident last month.", "Wi-Fi is slow in half of the office.",
        "Printing problems every week.", "The current provider takes days to respond.",
    ];

    private static readonly string[] Tips =
    [
        "Take a snapshot or backup first.", "Tell the users before you start.", "Write the change into the asset record.",
        "Do it in the maintenance window, not during office hours.", "Keep the old configuration for a week.",
    ];

    private static readonly string[] ChecklistBasics =
    [
        "Backup / snapshot before changes", "Inform users and agree on a date", "Update IT documentation",
        "Handover and sign-off by the client", "Invoice and close the project",
    ];

    private static readonly string[] Certifications =
    [
        "AZ-104 Azure Administrator", "MS-102 Microsoft 365 Administrator", "SC-300 Identity Administrator",
        "Fortinet NSE 4", "Veeam VMCE", "CompTIA Security+", "CCNA", "ITIL 4 Foundation",
    ];

    private static readonly string[] ServiceIdeas =
    [
        "backup check as a service", "NIS2 quick check package", "phishing simulation subscription",
        "onboarding/offboarding flat fee", "Microsoft 365 security baseline", "hardware as a service", "annual IT health check",
    ];
}
