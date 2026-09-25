/*
 *   Gnoseis is a CRM application and general knowledge manager.
 *   Copyright (C) 2024 Gnoseis.org
 *
 *   Dual-licensed under the GNU General Public License v3.0 (see LICENSE)
 *   or a commercial license (see COMMERCIAL_LICENSE).
 */

namespace Gnoseis.DemoData;

// Word lists for the demo knowledge base of an independent IT consultant. All companies and
// people are made up. Phone numbers use fictional ranges where a country has one (US 555, UK 7700 900).

internal sealed record NameGroup(string[] First, string[] Last);

internal sealed record City(string Name, string AreaCode);

internal sealed record Country(
    string Code, double Weight, string Tld, string[] LegalSuffixes, string CurrencySymbol, double CurrencyFactor,
    NameGroup Names, City[] Cities);

internal sealed record Sector(string Name, string[] Nouns, string[] Titles, string[] Systems);

internal sealed record TaxonomyArea(string Area, string Description, string[] Topics);

internal sealed record ItemKind(string Prefix, string Kind, double Weight, string Area, string Topic, string[] Models,
    bool Hardware = true, bool Assignable = false, bool OncePerOrg = false);

internal sealed record Issue(string Title, string Area, string Topic, string ItemPrefix, string[] Findings, string[] Fixes);

internal sealed record MeetingTopic(string Title, string Area, string[] Points, string[] Actions);

internal sealed record ProjectType(string Name, string Area, string Topic, string[] Scope);

internal sealed record KbArticle(string Title, string Area, string Topic, string[] Products, string[] Steps);

internal static class Vocabulary
{
    private static readonly NameGroup German = new(
        ["Anna", "Lukas", "Julia", "Felix", "Laura", "Jonas", "Sarah", "Maximilian", "Lena", "Tobias", "Katharina", "Stefan",
         "Sabine", "Michael", "Andrea", "Thomas", "Claudia", "Markus", "Nicole", "Christian", "Petra", "Florian", "Miriam",
         "Daniel", "Sophie", "Matthias", "Franziska", "Alexander", "Johanna", "Sebastian", "Elena", "Jan", "Birgit", "Ralf",
         "Heike", "Uwe", "Monika", "Dieter", "Carina", "Philipp", "Simone", "Bernd", "Ursula", "Jürgen", "Martina", "Oliver"],
        ["Müller", "Schmidt", "Schneider", "Fischer", "Weber", "Meyer", "Wagner", "Becker", "Schulz", "Hoffmann", "Koch",
         "Richter", "Klein", "Wolf", "Schröder", "Neumann", "Schwarz", "Zimmermann", "Braun", "Krüger", "Hofmann", "Hartmann",
         "Lange", "Werner", "Krause", "Lehmann", "Köhler", "Maier", "Huber", "Keller", "Brandt", "Vogel", "Frank", "Berger",
         "Graf", "Winkler", "Lorenz", "Baumann", "Pichler", "Gruber", "Steiner", "Moser", "Egger", "Brunner", "Frei", "Meier",
         "Kessler", "Roth", "Ziegler", "Seidel", "Engel", "Sommer", "Haas", "Fuchs", "Scholz", "Jäger"]);

    private static readonly NameGroup English = new(
        ["James", "Emma", "Oliver", "Olivia", "William", "Sophie", "Jack", "Charlotte", "Harry", "Amelia", "George", "Emily",
         "Thomas", "Grace", "Daniel", "Chloe", "Michael", "Hannah", "David", "Lucy", "Robert", "Jessica", "Sarah", "Matthew",
         "Rachel", "Andrew", "Laura", "Christopher", "Megan", "Ryan", "Aisling", "Ciaran", "Siobhan", "Declan", "Niamh", "Sean",
         "Ashley", "Tyler", "Madison", "Brandon", "Jennifer", "Kevin", "Priya", "Arjun", "Fatima", "Omar", "Mei", "Wei"],
        ["Smith", "Jones", "Taylor", "Brown", "Williams", "Wilson", "Johnson", "Davies", "Robinson", "Wright", "Thompson",
         "Evans", "Walker", "White", "Roberts", "Green", "Hall", "Wood", "Jackson", "Clarke", "Murphy", "Kelly", "O'Brien",
         "Byrne", "Ryan", "Walsh", "O'Connor", "Doyle", "Miller", "Davis", "Garcia", "Martinez", "Anderson", "Thomas", "Moore",
         "Martin", "Lee", "Harris", "Clark", "Lewis", "Young", "King", "Scott", "Baker", "Nelson", "Carter", "Mitchell",
         "Turner", "Patel", "Shah", "Khan", "Chen", "Nguyen", "Hughes", "Fletcher", "Barnes"]);

    private static readonly NameGroup Dutch = new(
        ["Daan", "Emma", "Sem", "Julia", "Lucas", "Sophie", "Milan", "Tess", "Bram", "Lotte", "Thijs", "Anouk", "Ruben",
         "Fleur", "Jeroen", "Marieke", "Pieter", "Inge", "Joost", "Sanne", "Wouter", "Esther", "Koen", "Femke", "Bas", "Linda",
         "Martijn", "Noor", "Stijn", "Eva"],
        ["de Jong", "Jansen", "de Vries", "van den Berg", "van Dijk", "Bakker", "Janssen", "Visser", "Smit", "Meijer",
         "de Boer", "Mulder", "de Groot", "Bos", "Vos", "Peters", "Hendriks", "van Leeuwen", "Dekker", "Brouwer", "de Wit",
         "Dijkstra", "Smits", "de Graaf", "van der Meer", "Kok", "Jacobs", "van der Linden", "Vermeulen", "van Beek"]);

    // Polish surnames are listed in the male form; the female form (-ska / -cka) is derived for female first names.
    private static readonly NameGroup Polish = new(
        ["Anna", "Piotr", "Katarzyna", "Krzysztof", "Małgorzata", "Tomasz", "Agnieszka", "Paweł", "Magdalena", "Michał",
         "Joanna", "Marcin", "Aleksandra", "Łukasz", "Monika", "Jakub", "Ewa", "Grzegorz", "Karolina", "Marek", "Natalia",
         "Kamil", "Zofia", "Adam", "Barbara", "Wojciech", "Justyna", "Mateusz", "Dorota", "Bartosz"],
        ["Nowak", "Kowalski", "Wiśniewski", "Wójcik", "Kowalczyk", "Kamiński", "Lewandowski", "Zieliński", "Szymański",
         "Woźniak", "Dąbrowski", "Kozłowski", "Jankowski", "Mazur", "Wojciechowski", "Kwiatkowski", "Krawczyk", "Kaczmarek",
         "Piotrowski", "Grabowski", "Zając", "Pawłowski", "Michalski", "Król", "Wieczorek", "Jabłoński", "Wróbel",
         "Nowakowski", "Majewski", "Olszewski", "Sikora", "Baran", "Rutkowski", "Ostrowski"]);

    public static readonly Country[] Countries =
    [
        new("DE", 28, "de", ["GmbH", "GmbH", "GmbH", "GmbH & Co. KG", "AG", "e.K.", "UG"], "€", 1.0, German,
            [new("Berlin", "30"), new("Hamburg", "40"), new("München", "89"), new("Köln", "221"), new("Frankfurt am Main", "69"),
             new("Stuttgart", "711"), new("Düsseldorf", "211"), new("Leipzig", "341"), new("Nürnberg", "911"), new("Hannover", "511")]),
        new("AT", 8, "at", ["GmbH", "GmbH", "KG", "OG"], "€", 1.0, German,
            [new("Wien", "1"), new("Graz", "316"), new("Linz", "732"), new("Salzburg", "662"), new("Innsbruck", "512")]),
        new("CH", 8, "ch", ["AG", "GmbH", "GmbH"], "CHF ", 0.95, German,
            [new("Zürich", "44"), new("Basel", "61"), new("Bern", "31"), new("Luzern", "41"), new("St. Gallen", "71")]),
        new("UK", 16, "co.uk", ["Ltd", "Ltd", "Ltd", "LLP", "PLC"], "£", 0.85, English,
            [new("London", "20"), new("Manchester", "161"), new("Birmingham", "121"), new("Leeds", "113"), new("Bristol", "117"),
             new("Edinburgh", "131"), new("Glasgow", "141")]),
        new("IE", 5, "ie", ["Ltd", "Ltd", "DAC"], "€", 1.0, English,
            [new("Dublin", "1"), new("Cork", "21"), new("Galway", "91"), new("Limerick", "61")]),
        new("NL", 8, "nl", ["B.V.", "B.V.", "V.O.F."], "€", 1.0, Dutch,
            [new("Amsterdam", "20"), new("Rotterdam", "10"), new("Utrecht", "30"), new("Den Haag", "70"), new("Eindhoven", "40")]),
        new("PL", 10, "pl", ["sp. z o.o.", "sp. z o.o.", "S.A.", "sp.j."], "PLN ", 4.3, Polish,
            [new("Warszawa", "22"), new("Kraków", "12"), new("Wrocław", "71"), new("Gdańsk", "58"), new("Poznań", "61")]),
        new("US", 17, "com", ["Inc.", "LLC", "LLC", "Corp."], "$", 1.1, English,
            [new("New York", "212"), new("Chicago", "312"), new("Boston", "617"), new("Austin", "512"), new("Denver", "303"),
             new("Seattle", "206"), new("Atlanta", "404")]),
    ];

    public static readonly string[] Prefixes =
    [
        "Alpine", "Northwind", "Bluewater", "Silverline", "Riverside", "Oakridge", "Summit", "Harbor", "Evergreen", "Lakeside",
        "Brightstone", "Redwood", "Westgate", "Eastfield", "Northstar", "Sunrise", "Greenleaf", "Ironbridge", "Clearwater",
        "Highland", "Maplewood", "Stonebridge", "Goldcrest", "Blackrock", "Whitehall", "Fairview", "Crescent", "Pinecrest",
        "Horizon", "Meridian", "Keystone", "Cornerstone", "Beacon", "Pioneer", "Heritage", "Liberty", "Sterling", "Crown",
        "Unity", "Vista", "Nova", "Apex", "Zenith", "Orbit", "Vertex", "Quantum", "Atlas", "Aurora", "Polaris", "Cobalt",
        "Amber", "Jade", "Onyx", "Ruby", "Sapphire", "Emerald", "Granite", "Marble", "Cedar", "Willow", "Birch", "Aspen",
        "Linden", "Elm", "Hawthorn", "Falcon", "Eagle", "Heron", "Kestrel", "Lynx", "Fox", "Otter", "Bison", "Stag", "Swan",
        "Raven", "Orca", "Delta", "Sigma", "Omega", "Alpha", "Prime", "Core", "Bright", "Swift", "Bold", "True", "Pure",
        "Clear", "Rapid", "Smart", "Blue", "Green", "Red", "Silver", "Golden", "Royal", "Grand", "Urban", "Metro", "Central",
        "Coastal", "Valley", "Hillside", "Bridge", "Castle", "Tower", "Market", "Park", "Garden", "Mill", "Forge", "Anchor",
        "Compass", "Harvest", "Orchard", "Meadow", "Brook", "Seaside", "Sunset", "Starlight", "Kingfisher", "Lighthouse",
        "Trident", "Pinnacle", "Solstice", "Everest", "Nordic", "Baltic", "Danube", "Rhine", "Thames", "Hudson",
    ];

    public static readonly string[] GenericClientTitles =
    [
        "Managing Director", "CEO", "Owner", "Office Manager", "Head of IT", "IT Administrator", "CFO", "Head of Finance",
        "Operations Manager", "HR Manager", "Assistant to the Management", "Head of Sales", "Receptionist", "Accountant",
        "Team Lead", "Marketing Manager", "Project Manager", "Purchasing Manager", "IT Coordinator", "COO", "Controller",
        "Sales Representative", "Customer Service Lead", "Quality Manager",
    ];

    public static readonly Sector[] Sectors =
    [
        new("Healthcare", ["Dental Practice", "Medical Centre", "Physiotherapy", "Veterinary Clinic", "Pharmacy", "Orthodontics",
                "Eye Clinic", "Care Home", "Radiology", "Dermatology Clinic", "Family Practice", "Hearing Care"],
            ["Practice Manager", "Head Physician", "Dentist", "Nursing Director", "Medical Assistant"],
            ["practice management software", "DICOM viewer", "e-prescription connector", "patient kiosk", "X-ray workstation"]),
        new("Legal", ["Law Firm", "Legal Partners", "Attorneys", "Notary Office", "Solicitors", "Patent Attorneys"],
            ["Partner", "Senior Associate", "Paralegal", "Office Manager", "Legal Secretary"],
            ["document management system", "legal case management", "dictation software", "e-filing client"]),
        new("Accounting & Finance", ["Tax Advisors", "Accounting", "Wealth Management", "Insurance Brokers", "Audit Partners",
                "Financial Planning", "Payroll Services", "Asset Management"],
            ["Partner", "Tax Advisor", "Senior Accountant", "Payroll Specialist", "Head of Compliance"],
            ["DATEV", "Sage", "Xero", "payroll software", "document scanning workflow"]),
        new("Manufacturing", ["Precision Engineering", "Metalworks", "Plastics", "Tooling", "Packaging", "Machine Works",
                "Components", "Fabrication", "Woodworks", "Textiles"],
            ["Plant Manager", "Production Manager", "Head of Engineering", "Logistics Lead", "CAD Designer"],
            ["ERP system", "MES terminals", "CAD workstations", "machine network (OT)", "label printers"]),
        new("Logistics", ["Logistics", "Freight", "Transport", "Warehousing", "Couriers", "Removals", "Shipping"],
            ["Dispatcher", "Warehouse Manager", "Fleet Manager", "Head of Operations"],
            ["transport management system", "handheld scanners", "telematics portal", "warehouse Wi-Fi"]),
        new("Retail", ["Fashion", "Bookshop", "Bike Shop", "Home & Garden", "Electronics", "Delicatessen", "Opticians",
                "Jewellers", "Toy Store", "Outdoor Store"],
            ["Store Manager", "E-commerce Manager", "Area Manager", "Buyer"],
            ["POS terminals", "webshop", "inventory management", "payment terminals"]),
        new("Hospitality", ["Hotel", "Restaurant Group", "Brewery", "Café", "Guest House", "Catering", "Spa Resort"],
            ["General Manager", "Front Office Manager", "Chef de Cuisine", "Events Manager"],
            ["property management system", "guest Wi-Fi", "restaurant POS", "key card system"]),
        new("Education", ["Language School", "Montessori School", "Driving School", "Academy", "Training Centre",
                "Music School", "Tutoring Centre"],
            ["Head Teacher", "School Administrator", "Course Coordinator", "Trainer"],
            ["Moodle", "student Wi-Fi", "classroom displays", "student laptops"]),
        new("Architecture & Real Estate", ["Architects", "Property Management", "Real Estate", "Interior Design", "Surveyors",
                "Facility Management"],
            ["Senior Architect", "Property Manager", "Estate Agent", "Draughtsperson"],
            ["AutoCAD", "large-format plotter", "property management software", "BIM workstations"]),
        new("Construction", ["Construction", "Builders", "Roofing", "Plumbing & Heating", "Electrical", "Landscaping",
                "Scaffolding"],
            ["Site Manager", "Estimator", "Foreman", "Project Engineer"],
            ["construction software", "field tablets", "time-tracking terminals"]),
        new("Non-profit", ["Foundation", "Association", "Charity", "Sports Club", "Community Centre", "Youth Services"],
            ["Chairperson", "Treasurer", "Volunteer Coordinator", "Fundraising Manager"],
            ["donor management", "volunteer laptops", "shared calendars"]),
        new("Marketing & Media", ["Marketing", "Creative Studio", "Publishing", "Photography", "Media", "Events",
                "Advertising", "Video Production"],
            ["Creative Director", "Art Director", "Account Director", "Producer"],
            ["Adobe Creative Cloud", "Mac workstations", "media NAS", "colour-calibrated displays"]),
        new("Automotive", ["Car Dealership", "Auto Repair", "Tyres", "Car Rental", "Body Shop", "Motorcycles"],
            ["Workshop Manager", "Service Advisor", "Sales Manager", "Parts Manager"],
            ["dealer management system", "diagnostic laptops", "workshop tablets"]),
        new("Food & Agriculture", ["Farm", "Winery", "Bakery", "Dairy", "Nursery", "Butchers", "Food Wholesale"],
            ["Production Lead", "Farm Manager", "Sales Manager"],
            ["scales and label printers", "cold-store monitoring", "order system"]),
        new("Engineering Services", ["Engineering", "Consulting Engineers", "Energy", "Solar", "Surveying", "Environmental"],
            ["Principal Engineer", "Project Engineer", "Technical Director"],
            ["CAD/CAE workstations", "GIS software", "large-file transfer", "simulation server"]),
    ];

    public static readonly string[] VendorNouns =
    [
        "IT Distribution", "Networks", "Telecom", "Hosting", "Data Centre", "Cloud Services", "Office Solutions",
        "Security Systems", "Software", "Print Services", "Cabling", "Power Systems", "Computer Wholesale", "Licensing",
        "Hardware Supply", "Internet Services",
    ];

    public static readonly string[] VendorTitles =
    [
        "Account Manager", "Sales Engineer", "Support Engineer", "Customer Success Manager", "Billing Department",
        "Partner Manager", "Inside Sales", "Technical Account Manager", "Presales Consultant",
    ];

    public static readonly string[] PartnerNouns =
    [
        "IT Consulting", "Web Studio", "Network Services", "Cabling & Electrical", "Security Consulting", "SAP Consulting",
        "Data Recovery", "Software Development", "IT Training", "Audio Visual",
    ];

    public static readonly string[] PartnerTitles =
    [
        "Freelance Network Engineer", "Web Developer", "Cabling Technician", "Security Consultant", "Developer",
        "Trainer", "Managing Director", "Linux Specialist", "Database Consultant",
    ];

    public static readonly string[] OtherNouns =
    [
        "Chamber of Commerce", "Business Network", "Chamber of Crafts", "Coworking", "Rotary Club", "IT Meetup",
        "Trade Fair", "Startup Hub", "Tax Office", "Employers Association", "Innovation Lab",
    ];

    public static readonly string[] OtherTitles =
    [
        "Event Coordinator", "Organiser", "Member Services", "Advisor", "Board Member", "Community Manager",
    ];

    public static readonly TaxonomyArea[] Taxonomy =
    [
        new("Networking", "Local network, internet access and remote connectivity.",
            ["LAN", "VLANs", "Wi-Fi", "VPN", "Firewalls", "DNS", "DHCP", "Routing", "Switching", "Internet uplinks", "IPv6", "Monitoring"]),
        new("Security", "Protecting clients against attacks and data loss.",
            ["Endpoint protection", "MFA", "Phishing", "Patch management", "Vulnerability scans", "Incident response",
             "Password managers", "Email security", "Awareness training", "Zero Trust", "Encryption", "SIEM"]),
        new("Microsoft 365", "Tenant administration, collaboration and device management.",
            ["Exchange Online", "Teams", "SharePoint", "OneDrive", "Intune", "Entra ID", "Licensing", "Conditional Access",
             "Defender", "Purview", "Tenant migration", "Power Automate"]),
        new("Servers", "On-premises servers and virtualisation.",
            ["Windows Server", "Linux", "Active Directory", "Group Policy", "File server", "Print server", "Hyper-V", "VMware",
             "Proxmox", "Remote Desktop", "Certificates", "Hardware"]),
        new("Backup & DR", "Backups, restore tests and disaster recovery.",
            ["Veeam", "Offsite backup", "Restore tests", "Immutable storage", "Disaster recovery", "NAS", "Retention", "Cloud backup"]),
        new("Cloud", "Public cloud, hosting and SaaS.",
            ["Azure", "AWS", "Hosting", "Domains", "Web hosting", "SaaS", "Cloud migration", "Cost optimisation"]),
        new("Devices", "Client hardware and its lifecycle.",
            ["Laptops", "Desktops", "Printers", "Smartphones", "Tablets", "Peripherals", "Monitors", "Docking stations",
             "Imaging", "Lifecycle", "UPS"]),
        new("Telephony", "Phone systems and mobile contracts.",
            ["VoIP", "SIP trunks", "PBX", "Teams Phone", "Call queues", "Mobile contracts"]),
        new("Software", "Business applications used by clients.",
            ["ERP", "CRM", "Accounting software", "Line-of-business apps", "Updates", "Licensing", "Browsers", "PDF tools"]),
        new("Data", "Databases, reporting and data handling.",
            ["SQL Server", "Reporting", "Power BI", "Data migration", "Archiving", "Data quality"]),
        new("Compliance", "Regulations, audits and IT policies.",
            ["GDPR", "NIS2", "ISO 27001", "Data processing agreements", "Audits", "Policies", "Documentation", "Cyber insurance"]),
        new("Business", "Running the consultancy itself.",
            ["Contracts", "Invoicing", "Quotes", "Pricing", "Leads", "Marketing", "Partners", "Suppliers", "Time tracking",
             "Tax", "Insurance", "Training", "Certifications"]),
        new("Support", "Day-to-day support work.",
            ["Helpdesk", "Remote support", "Onsite visits", "Onboarding", "Offboarding", "Escalations", "SLA", "Maintenance windows"]),
    ];

    public static readonly string[] StatusTags =
    [
        "Status: Open", "Status: In progress", "Status: Waiting on client", "Status: Waiting on vendor", "Status: Done",
        "Status: Follow-up", "Billing: Billable", "Billing: Not billable", "Billing: Included in retainer",
        "Priority: Low", "Priority: Normal", "Priority: High", "Priority: Critical",
        "Client tier: A", "Client tier: B", "Client tier: C", "Lead: Hot", "Lead: Warm", "Lead: Cold",
    ];

    public static readonly ItemKind[] ItemKinds =
    [
        new("SRV", "Server", 6, "Servers", "Hardware",
            ["Dell PowerEdge R650xs", "Dell PowerEdge T350", "HPE ProLiant DL380 Gen11", "HPE ProLiant ML350 Gen10",
             "Lenovo ThinkSystem SR630 V3", "Fujitsu PRIMERGY RX2540 M7", "Lenovo ThinkSystem ST250 V2"]),
        new("NB", "Laptop", 20, "Devices", "Laptops",
            ["Lenovo ThinkPad T14 Gen 4", "Lenovo ThinkPad X1 Carbon Gen 11", "Dell Latitude 5440", "Dell Latitude 7440",
             "HP EliteBook 840 G10", "HP ProBook 450 G10", "MacBook Air M3", "MacBook Pro 14 M3", "Surface Laptop 5",
             "Fujitsu LIFEBOOK U7413"], Assignable: true),
        new("PC", "Desktop", 9, "Devices", "Desktops",
            ["Dell OptiPlex 7010", "HP EliteDesk 800 G9", "Lenovo ThinkCentre M70q", "iMac 24 M3", "Fujitsu ESPRIMO D7012",
             "Lenovo ThinkStation P360"], Assignable: true),
        new("FW", "Firewall", 5, "Networking", "Firewalls",
            ["Fortinet FortiGate 60F", "Fortinet FortiGate 100F", "Sophos XGS 2100", "Sophos XGS 116", "WatchGuard Firebox T45",
             "Netgate 6100", "Palo Alto PA-440", "LANCOM 1900EF"]),
        new("SW", "Switch", 6, "Networking", "Switching",
            ["HPE Aruba 2930F 24G", "Cisco Catalyst 9200L-24P", "Ubiquiti USW-Pro-48-PoE", "Netgear M4250-26G4F",
             "Juniper EX2300-24P", "LANCOM GS-3652X"]),
        new("AP", "Access point", 7, "Networking", "Wi-Fi",
            ["Ubiquiti U6 Pro", "Aruba Instant On AP22", "Cisco Meraki MR36", "LANCOM LW-600", "TP-Link Omada EAP670"]),
        new("RT", "Router", 3, "Networking", "Internet uplinks",
            ["FRITZ!Box 7590 AX", "DrayTek Vigor 2865", "Cisco ISR 1111", "LANCOM 1793VA"]),
        new("NAS", "NAS", 5, "Backup & DR", "NAS",
            ["Synology DS923+", "Synology RS1221+", "QNAP TS-873A", "QNAP TS-h1290FX", "Synology DS1522+"]),
        new("PRN", "Printer", 7, "Devices", "Printers",
            ["HP Color LaserJet Pro MFP 4301", "Brother MFC-L8900CDW", "Kyocera ECOSYS M5526cdw", "Canon imageRUNNER C3326i",
             "Ricoh IM C3000", "HP DesignJet T650"]),
        new("UPS", "UPS", 3, "Devices", "UPS", ["APC Smart-UPS 1500", "Eaton 5PX 2200", "APC Back-UPS Pro 900"]),
        new("MOB", "Smartphone", 7, "Devices", "Smartphones",
            ["iPhone 15", "iPhone 14", "Samsung Galaxy S24", "Samsung Galaxy A55", "Google Pixel 8"], Assignable: true),
        new("TEL", "Desk phone", 3, "Telephony", "VoIP", ["Yealink T54W", "Snom D785", "Yealink T46U", "Poly Edge E350"], Assignable: true),
        new("LIC", "Licence", 12, "Software", "Licensing",
            ["Microsoft 365 Business Premium", "Microsoft 365 Business Standard", "Microsoft 365 E3", "Exchange Online Plan 1",
             "Windows Server 2022 Standard", "Windows Server 2025 Standard", "SQL Server 2022 Standard",
             "Veeam Data Platform Foundation", "Veeam Backup for M365", "Bitdefender GravityZone", "ESET PROTECT Advanced",
             "Defender for Business", "Sophos Intercept X", "1Password Business", "Bitwarden Teams", "Adobe Acrobat Pro",
             "Adobe Creative Cloud", "AutoCAD LT", "TeamViewer Business", "NinjaOne RMM", "Zoom Workplace Pro",
             "Hornetsecurity 365 Total Protection"], Hardware: false),
        new("SVC", "Service contract", 6, "Support", "SLA",
            ["Managed backup", "Managed firewall", "Monitoring & patching", "Helpdesk retainer 10 h/month",
             "Helpdesk retainer 20 h/month", "Web hosting", "Email security", "Offsite backup 5 TB", "Awareness training",
             "Microsoft 365 management", "Server maintenance", "Mobile device management", "IT documentation",
             "SIP trunk 10 channels"], Hardware: false),
        new("DOM", "Domain", 3, "Cloud", "Domains", [], Hardware: false, OncePerOrg: true),
        new("CRT", "Certificate", 2, "Servers", "Certificates", [], Hardware: false, OncePerOrg: true),
    ];

    public static readonly string[] Registrars = ["INWX", "Gandi", "IONOS", "Cloudflare Registrar", "Namecheap", "OVHcloud", "united-domains"];

    public static readonly string[] Locations =
    [
        "Server room, basement", "Server cabinet, 1st floor", "Comms room, 2nd floor", "Reception", "Back office",
        "Open-plan office", "Warehouse", "Meeting room", "Home office", "Branch office", "Workshop", "Storage room",
    ];

    public static readonly string[] OperatingSystems =
    [
        "Windows 11 Pro 24H2", "Windows 11 Enterprise 23H2", "Windows 10 Pro 22H2", "macOS 15 Sequoia", "macOS 14 Sonoma",
        "Ubuntu 24.04 LTS", "Windows Server 2022", "Windows Server 2025", "Windows Server 2019", "Debian 12",
    ];

    public static readonly Issue[] Issues =
    [
        new("Outlook keeps asking for password", "Microsoft 365", "Exchange Online", "NB",
            ["A stale token was cached after the password change.", "An old GPO disabled modern authentication.",
             "The user had two Outlook profiles pointing at the same mailbox."],
            ["Cleared cached credentials and rebuilt the Outlook profile.", "Removed the legacy registry setting, signed in again with MFA.",
             "Deleted the duplicate profile and set the remaining one as default."]),
        new("VPN drops every few minutes", "Networking", "VPN", "FW",
            ["DPD timeout on the firewall was set to 10 s.", "The user's home router has SIP ALG enabled and resets UDP sessions.",
             "MTU mismatch caused fragmented packets to be dropped."],
            ["Raised DPD timeout to 60 s and enabled keepalives.", "Switched the tunnel to TCP 443 as a workaround.",
             "Set the tunnel MTU to 1400 and confirmed the connection held for an hour."]),
        new("Printer offline for the whole office", "Devices", "Printers", "PRN",
            ["The printer took a new DHCP address after a power cut.", "The print spooler on the print server had hung.",
             "The toner-low warning had turned into a hard stop."],
            ["Created a DHCP reservation and updated the port on the print server.", "Restarted the spooler and cleared stuck jobs.",
             "Replaced the toner and ordered two spares."]),
        new("File server running out of disk space", "Servers", "File server", "SRV",
            ["The scan folder contained 180 GB of old scans.", "Shadow copies were set to use 30 % of the volume.",
             "A user copied a full camera archive to the shared drive."],
            ["Moved old scans to the archive share and set a quota.", "Reduced shadow copy storage to 10 %.",
             "Extended the volume by 500 GB and set up a disk space alert at 85 %."]),
        new("Backup job failed overnight", "Backup & DR", "Veeam", "NAS",
            ["The repository NAS was in the middle of a DSM update.", "The VSS writer for SQL Server was in a failed state.",
             "The backup user's password had expired."],
            ["Re-ran the job manually, finished successfully.", "Restarted the SQL VSS writer and scheduled a restart of the host.",
             "Changed the service account to a non-expiring managed account."]),
        new("Wi-Fi slow in meeting room", "Networking", "Wi-Fi", "AP",
            ["The AP was on a 2.4 GHz channel shared with the neighbours.", "The AP negotiated only 100 Mbit on its uplink.",
             "Too many clients on a single AP during all-hands meetings."],
            ["Moved clients to 5 GHz with band steering and a fixed channel plan.", "Replaced the patch cable; uplink is 1 Gbit again.",
             "Proposed a second access point for the room."]),
        new("Phishing email reported", "Security", "Phishing", "LIC",
            ["Fake invoice with a link to a credential-harvesting page.", "Spoofed sender using a look-alike domain.",
             "One user entered their password on the fake page."],
            ["Purged the message from all mailboxes and blocked the sender domain.", "Reset the user's password and revoked sessions.",
             "Checked sign-in logs: no successful logins from unknown locations."]),
        new("New employee needs setup", "Support", "Onboarding", "NB",
            ["Start date is next Monday.", "Needs access to the finance share and the ERP.", "Laptop from stock can be used."],
            ["Created the account, assigned licences and added to groups.", "Enrolled the laptop in Intune and installed line-of-business apps.",
             "Sent the welcome letter with MFA instructions."]),
        new("Employee leaving – offboarding", "Support", "Offboarding", "NB",
            ["Last working day is Friday.", "Mailbox must be kept for the manager for 3 months.", "Company phone has to be wiped."],
            ["Converted the mailbox to a shared mailbox and gave the manager access.", "Blocked sign-in and removed licences.",
             "Wiped the laptop and phone and put them back in stock."]),
        new("Server not responding", "Servers", "Hardware", "SRV",
            ["A failed disk in the RAID 5 array caused the array to degrade.", "Windows update got stuck at 100 % after a reboot.",
             "The host ran out of memory because of a runaway process."],
            ["Replaced the disk under warranty; rebuild completed overnight.", "Booted into safe mode, rolled back the update.",
             "Limited the process memory and scheduled a RAM upgrade."]),
        new("Teams calls choppy", "Microsoft 365", "Teams", "RT",
            ["Upload bandwidth is saturated by the offsite backup during the day.", "No QoS on the firewall.",
             "Users are on the guest Wi-Fi."],
            ["Moved the backup window to the night.", "Created QoS rules for Teams media ports.",
             "Explained which network to use and hid the guest SSID in the office."]),
        new("Laptop won't boot", "Devices", "Laptops", "NB",
            ["BitLocker recovery screen after a BIOS update.", "SSD failure reported by the diagnostics.",
             "Windows boot loader corrupted after a power loss."],
            ["Looked up the recovery key in Entra ID and unlocked the drive.", "Ordered a warranty replacement SSD and reimaged.",
             "Repaired the boot loader from the recovery environment."]),
        new("Mailbox full", "Microsoft 365", "Exchange Online", "LIC",
            ["Mailbox at 49.8 GB of 50 GB.", "Large attachments in Sent Items.", "Archive mailbox not enabled."],
            ["Enabled the online archive and a 2-year retention tag.", "Showed the user how to share files as links.",
             "Upgraded the licence to include a larger mailbox."]),
        new("Firewall firmware update", "Networking", "Firewalls", "FW",
            ["Vendor advisory for a critical SSL-VPN vulnerability.", "Firmware is two major versions behind.",
             "Configuration backup was older than 3 months."],
            ["Took a config backup and updated firmware in the evening window.", "Verified VPN and site-to-site tunnels afterwards.",
             "Scheduled quarterly firmware reviews."]),
        new("Switch port errors", "Networking", "Switching", "SW",
            ["CRC errors on the uplink port to the server.", "A loop caused by a desk switch under a desk.",
             "PoE budget exceeded after adding cameras."],
            ["Replaced the SFP module and patch cable.", "Enabled loop protection and removed the desk switch.",
             "Moved two cameras to the second switch."]),
        new("Certificate expired", "Servers", "Certificates", "CRT",
            ["The web shop showed a certificate warning.", "The Remote Desktop gateway certificate expired over the weekend.",
             "Auto-renewal failed because DNS validation was moved to another provider."],
            ["Renewed the certificate and installed it on all bindings.", "Set up an expiry monitor 30 days in advance.",
             "Updated the DNS API credentials for automatic renewal."]),
        new("Domain renewal due", "Cloud", "Domains", "DOM",
            ["The renewal invoice went to a former employee's mailbox.", "Auto-renew was disabled at the registrar.",
             "Transfer lock was off."],
            ["Renewed for two years and changed the billing contact.", "Enabled auto-renew and transfer lock.",
             "Added the domain to the expiry calendar."]),
        new("Phone system not ringing", "Telephony", "VoIP", "TEL",
            ["SIP registration lost after the ISP changed the public IP.", "The call queue had no active agents after a holiday.",
             "Desk phones lost PoE after a switch reboot."],
            ["Re-registered the trunk and enabled dynamic DNS.", "Fixed the queue schedule and added a fallback number.",
             "Checked PoE on all ports; phones registered again."]),
        new("NAS reports degraded volume", "Backup & DR", "NAS", "NAS",
            ["One drive shows reallocated sectors.", "A drive was removed during cleaning.", "Temperature in the cabinet above 40 °C."],
            ["Replaced the drive and let the volume repair.", "Reseated the drive, SMART is fine, scrubbing started.",
             "Asked the landlord to fix the air conditioning in the cabinet."]),
        new("UPS on battery", "Devices", "UPS", "UPS",
            ["Short power cut in the building.", "Battery self-test failed.", "UPS load at 95 % after adding a second server."],
            ["Confirmed servers stayed up; logged the event.", "Ordered a replacement battery pack.",
             "Proposed a larger UPS with an extra battery module."]),
        new("Suspicious sign-in alert", "Microsoft 365", "Entra ID", "LIC",
            ["Impossible-travel alert for a user account.", "Several failed MFA prompts in a row (MFA fatigue).",
             "Legacy authentication attempts from abroad."],
            ["Revoked sessions, reset password, re-registered MFA.", "Enabled number matching for the authenticator app.",
             "Blocked legacy authentication with Conditional Access."]),
        new("Slow ERP", "Software", "ERP", "SRV",
            ["The database server's disk queue is high during month-end.", "The ERP client is two versions behind.",
             "Antivirus scans the ERP database files in real time."],
            ["Added the ERP folders to the antivirus exclusions per vendor guidance.", "Updated all clients to the current version.",
             "Planned to move the database to SSD storage."]),
        new("Scanner can't send to email", "Devices", "Printers", "PRN",
            ["Basic SMTP auth was disabled in Microsoft 365.", "The scan-to-email account's password expired.",
             "The relay connector IP no longer matched."],
            ["Configured SMTP relay with a connector for the office IP.", "Switched the device to scan-to-SharePoint.",
             "Updated the connector with the new public IP."]),
        new("Ransomware warning from endpoint protection", "Security", "Endpoint protection", "PC",
            ["Endpoint protection blocked a macro from an email attachment.", "Suspicious PowerShell activity on one PC.",
             "Detection was a false positive for a vendor tool."],
            ["Isolated the PC, ran a full scan, no further findings.", "Restored two files from backup to be safe.",
             "Added an exclusion for the vendor tool after checking its signature."]),
        new("Smartphone lost", "Devices", "Smartphones", "MOB",
            ["User left the phone in a taxi.", "Phone stolen at a trade fair."],
            ["Located the phone via MDM, it was offline; issued a remote wipe.", "Blocked the SIM with the carrier.",
             "Set up a replacement phone from stock."]),
    ];

    public static readonly MeetingTopic[] MeetingTopics =
    [
        new("Quarterly IT review", "Support",
            ["Went through the ticket statistics of the last quarter.", "Backup success rate was 99 %; one failed job fixed.",
             "Three laptops are older than five years.", "Patch compliance is at 96 %."],
            ["Send hardware refresh quote", "Schedule restore test", "Update the asset list"]),
        new("IT budget planning", "Business",
            ["Discussed the IT budget for next year.", "Management wants predictable monthly costs.",
             "Server hardware warranty ends next year.", "Licence costs increased after the Microsoft price change."],
            ["Prepare a budget proposal with options", "List all renewals for next year", "Compare leasing vs. buying"]),
        new("Security roadmap", "Security",
            ["Reviewed the results of the last vulnerability scan.", "MFA is enabled for 80 % of the users.",
             "Cyber insurance questionnaire needs answers about backups and MFA.", "Staff would benefit from phishing training."],
            ["Enable MFA for the remaining users", "Offer awareness training", "Fill in the insurance questionnaire together"]),
        new("Backup strategy", "Backup & DR",
            ["Current backups are on a single NAS in the same room.", "No offsite copy of the file server.",
             "Discussed the 3-2-1 rule and immutable storage.", "Restore time objective: one business day."],
            ["Quote an offsite backup", "Run a documented restore test", "Write down the recovery plan"]),
        new("Office move planning", "Networking",
            ["Move date is planned for the end of next quarter.", "New office needs structured cabling and a comms cabinet.",
             "Internet line has to be ordered at least 8 weeks in advance.", "Phones should keep the same numbers."],
            ["Order the internet line", "Get a cabling quote", "Plan the moving weekend"]),
        new("NIS2 obligations", "Compliance",
            ["Checked whether the company falls under NIS2.", "Went through risk management and reporting duties.",
             "Management has personal liability for cyber security.", "Supplier security needs to be documented."],
            ["Send the NIS2 checklist", "Draft an incident response plan", "Book management training"]),
        new("Microsoft 365 adoption", "Microsoft 365",
            ["Staff still email attachments instead of sharing links.", "Teams channels are not used consistently.",
             "SharePoint permissions grew over time.", "Discussed Copilot licences."],
            ["Run a short Teams workshop", "Clean up SharePoint permissions", "Test Copilot with two users"]),
        new("Contract renewal", "Business",
            ["The managed services contract ends in two months.", "Client is happy with response times.",
             "Wants onsite days included.", "Discussed the new hourly rate."],
            ["Send the updated contract", "Include two onsite days per month", "Confirm new SLA times"]),
        new("Hardware refresh", "Devices",
            ["Twelve devices are out of warranty.", "Users prefer lighter laptops with docking stations.",
             "Old devices must be wiped with a certificate.", "Windows 10 end of support affects four PCs."],
            ["Quote laptops and docks", "Plan the rollout per department", "Arrange certified disposal"]),
        new("Cloud strategy", "Cloud",
            ["Discussed moving the file server to SharePoint.", "The ERP must stay on-premises for now.",
             "Internet uplink would need an upgrade.", "Compared costs for three years."],
            ["Prepare a migration plan", "Check the ERP vendor's cloud options", "Quote a faster internet line"]),
        new("Kick-off meeting", "Business",
            ["Agreed on project scope and timeline.", "Named contacts on both sides.", "Weekly status call on Thursdays.",
             "Change requests will be quoted separately."],
            ["Send the project plan", "Create the shared project folder", "Book the first status call"]),
        new("Incident post-mortem", "Security",
            ["Reviewed the timeline of the incident.", "Detection took longer than it should have.",
             "The backup restore worked as planned.", "Communication to staff was unclear."],
            ["Write the incident report", "Add alerting for the missing log source", "Update the communication plan"]),
        new("Onboarding as new client", "Support",
            ["Collected admin credentials into the password manager.", "Documented the network and all servers.",
             "Installed the monitoring agent on all devices.", "Found several accounts of former employees still active."],
            ["Finish the IT documentation", "Disable stale accounts", "Present findings in two weeks"]),
    ];

    public static readonly ProjectType[] ProjectTypes =
    [
        new("M365 tenant migration", "Microsoft 365", "Tenant migration",
            ["Migrate mailboxes, OneDrive and Teams", "Update DNS records and autodiscover", "Re-configure Outlook and mobile devices"]),
        new("Server replacement", "Servers", "Hardware",
            ["Replace the host server", "Migrate VMs and roles", "Decommission and wipe the old server"]),
        new("Firewall upgrade", "Networking", "Firewalls",
            ["Replace the firewall", "Migrate rules and VPN tunnels", "Enable IPS and web filtering"]),
        new("Wi-Fi rollout", "Networking", "Wi-Fi",
            ["Site survey", "Install access points", "Separate guest and staff networks"]),
        new("Backup concept", "Backup & DR", "Offsite backup",
            ["Design a 3-2-1 backup", "Set up immutable offsite copies", "Document and test restores"]),
        new("NIS2 readiness assessment", "Compliance", "NIS2",
            ["Gap analysis against NIS2 measures", "Risk register", "Roadmap with priorities"]),
        new("Laptop rollout", "Devices", "Laptops",
            ["Procure laptops and docks", "Autopilot enrolment", "Hand over to users and collect old devices"]),
        new("Office move", "Networking", "LAN",
            ["Cabling and comms cabinet", "Internet and phone lines", "Weekend move of all IT equipment"]),
        new("VoIP migration", "Telephony", "VoIP",
            ["Port phone numbers", "Configure hunt groups and IVR", "Install desk phones"]),
        new("Intune enrolment", "Microsoft 365", "Intune",
            ["Compliance and configuration policies", "Enrol Windows and mobile devices", "App deployment"]),
        new("Security audit", "Security", "Vulnerability scans",
            ["External and internal scan", "Review of accounts and permissions", "Report with recommendations"]),
        new("ERP upgrade", "Software", "ERP",
            ["Test upgrade in a copy", "Coordinate with ERP vendor", "Upgrade clients"]),
        new("Network redesign", "Networking", "VLANs",
            ["Segment network into VLANs", "Replace unmanaged switches", "Document the new design"]),
        new("MFA rollout", "Security", "MFA",
            ["Communication to staff", "Register authenticator apps", "Conditional Access policies"]),
        new("Windows 11 upgrade", "Devices", "Imaging",
            ["Compatibility check", "In-place upgrade or replacement", "Application testing"]),
        new("Disaster recovery test", "Backup & DR", "Disaster recovery",
            ["Restore critical systems to isolated network", "Measure recovery times", "Write DR test report"]),
        new("Azure migration", "Cloud", "Azure",
            ["Landing zone setup", "Migrate application servers", "Cost monitoring"]),
        new("File server to SharePoint", "Microsoft 365", "SharePoint",
            ["Map shares to sites and libraries", "Migrate data with permissions", "Train users"]),
        new("Printer consolidation", "Devices", "Printers",
            ["Inventory of printers", "Replace with managed MFPs", "Follow-me printing"]),
        new("Password manager rollout", "Security", "Password managers",
            ["Choose product", "Import shared credentials", "Train staff"]),
        new("Managed services onboarding", "Support", "Helpdesk",
            ["Documentation", "Monitoring agents", "Handover from previous provider"]),
        new("Endpoint protection switch", "Security", "Endpoint protection",
            ["Uninstall old product", "Deploy new agent", "Tune policies and exclusions"]),
        new("Email security gateway", "Security", "Email security",
            ["MX change", "SPF, DKIM and DMARC", "Quarantine reports for users"]),
        new("VPN replacement", "Networking", "VPN",
            ["Replace legacy VPN client", "MFA for remote access", "Rollout to home workers"]),
        new("Hyper-V cluster", "Servers", "Hyper-V",
            ["Two hosts with shared storage", "Failover testing", "Migration of VMs"]),
        new("NAS replacement", "Backup & DR", "NAS",
            ["Procure new NAS", "Data migration", "Snapshots and replication"]),
        new("IT documentation", "Compliance", "Documentation",
            ["Network plan", "System inventory", "Emergency manual"]),
        new("GDPR data audit", "Compliance", "GDPR",
            ["Record of processing activities", "Data processing agreements", "Deletion concept"]),
        new("Power BI dashboards", "Data", "Power BI",
            ["Connect ERP data", "Sales and finance dashboards", "Scheduled refresh"]),
        new("CRM implementation", "Software", "CRM",
            ["Requirements", "Data import from spreadsheets", "User training"]),
        new("Teams Phone rollout", "Telephony", "Teams Phone",
            ["Number porting", "Call queues and auto attendants", "Headsets for staff"]),
        new("Awareness training", "Security", "Awareness training",
            ["Baseline phishing simulation", "Training sessions", "Follow-up simulation"]),
        new("Server room cleanup", "Servers", "Hardware",
            ["Cable management", "Label all devices", "Remove unused equipment"]),
        new("UPS replacement", "Devices", "UPS",
            ["Size new UPS", "Install during maintenance window", "Configure shutdown agents"]),
        new("Guest Wi-Fi", "Networking", "Wi-Fi",
            ["Separate VLAN", "Captive portal", "Bandwidth limits"]),
        new("Asset inventory", "Devices", "Lifecycle",
            ["Scan network", "Label devices", "Import into asset list"]),
        new("Licence audit", "Software", "Licensing",
            ["Compare assigned vs. purchased licences", "Remove unused licences", "Savings report"]),
        new("Remote work setup", "Networking", "VPN",
            ["Laptops and VPN", "Softphones", "Home office guidelines"]),
        new("Branch office connection", "Networking", "Routing",
            ["Site-to-site VPN", "Shared printers and files", "Local Wi-Fi"]),
        new("Exchange decommission", "Microsoft 365", "Exchange Online",
            ["Move remaining recipients", "Remove hybrid configuration", "Shut down on-prem Exchange"]),
        new("Active Directory cleanup", "Servers", "Active Directory",
            ["Remove stale accounts and computers", "Tier model for admins", "Review GPOs"]),
        new("Website hosting move", "Cloud", "Web hosting",
            ["New hosting package", "Migrate website and database", "DNS switch"]),
        new("Patch management rollout", "Security", "Patch management",
            ["RMM agent on all devices", "Patch rings", "Monthly reports"]),
    ];

    public static readonly string[] ProjectPhases =
        ["Planning", "Preparation", "Implementation", "Testing", "Rollout", "Handover", "Documentation", "Follow-up"];

    public static readonly KbArticle[] KbArticles =
    [
        new("Update firmware on {p}", "Networking", "Firewalls",
            ["Fortinet FortiGate", "Sophos XGS", "WatchGuard Firebox", "LANCOM routers"],
            ["Download a configuration backup.", "Check the upgrade path in the release notes.",
             "Upload the firmware image and reboot in the maintenance window.", "Verify VPN tunnels and internet access.",
             "Note the new version in the asset record."]),
        new("Restore a single file with {p}", "Backup & DR", "Restore tests",
            ["Veeam Backup & Replication", "Synology Hyper Backup", "Veeam Backup for M365", "Windows Server Backup"],
            ["Open the console and find the latest restore point.", "Start the file-level restore wizard.",
             "Restore to the original location with a new name.", "Ask the user to check the file.", "Log the restore in the ticket."]),
        new("Enrol a device in Intune ({p})", "Microsoft 365", "Intune",
            ["Windows Autopilot", "iOS", "Android Enterprise", "macOS"],
            ["Check the user has an Intune licence.", "Register the device hash or start enrolment from the device.",
             "Wait for the compliance policy to apply.", "Check the required apps were installed."]),
        new("Reset MFA for a user in {p}", "Microsoft 365", "Entra ID",
            ["Entra ID", "the Microsoft 365 admin center", "Duo", "Okta"],
            ["Verify the user's identity by calling back the known number.", "Revoke existing MFA registrations.",
             "Issue a temporary access pass.", "Let the user register again and confirm sign-in works."]),
        new("Set up a VLAN on {p}", "Networking", "VLANs",
            ["HPE Aruba switches", "Cisco Catalyst", "UniFi switches", "Netgear managed switches"],
            ["Create the VLAN and give it a name.", "Tag the VLAN on the uplink ports.", "Set access ports for the devices.",
             "Add the interface and DHCP scope on the firewall.", "Test and update the network plan."]),
        new("Add a shared mailbox in {p}", "Microsoft 365", "Exchange Online",
            ["Exchange Online", "Exchange 2019"],
            ["Create the shared mailbox.", "Grant Full Access and Send As.", "Wait for it to appear in Outlook (up to an hour).",
             "Set up automatic replies if needed."]),
        new("Check SPF, DKIM and DMARC with {p}", "Security", "Email security",
            ["MXToolbox", "dmarcian", "PowerShell", "Google Admin Toolbox"],
            ["Look up the SPF record and count DNS lookups.", "Confirm DKIM is signing outgoing mail.",
             "Start DMARC with p=none and a report address.", "Review reports after two weeks, then move to quarantine."]),
        new("Replace a failed disk in {p}", "Servers", "Hardware",
            ["Dell PERC controllers", "HPE Smart Array", "Synology NAS", "QNAP NAS"],
            ["Identify the failed disk by its bay number and LED.", "Check the replacement has the same size or larger.",
             "Hot-swap the disk.", "Monitor the rebuild and confirm the array is healthy."]),
        new("Onboard a new user ({p})", "Support", "Onboarding",
            ["hybrid AD", "cloud-only", "Google Workspace"],
            ["Create the account and set the manager.", "Assign licences and groups.", "Prepare the device.",
             "Send the welcome letter with first sign-in steps."]),
        new("Offboard a leaving user ({p})", "Support", "Offboarding",
            ["hybrid AD", "cloud-only", "Google Workspace"],
            ["Block sign-in and revoke sessions.", "Convert the mailbox to shared and delegate access.",
             "Transfer OneDrive files to the manager.", "Remove licences and wipe devices."]),
        new("Renew a TLS certificate with {p}", "Servers", "Certificates",
            ["Let's Encrypt (win-acme)", "Let's Encrypt (certbot)", "a commercial CA", "Azure Key Vault"],
            ["Check where the certificate is used.", "Renew or re-issue.", "Bind the new certificate on all services.",
             "Verify with an SSL checker and update the expiry date in the asset record."]),
        new("Speed up a slow {p}", "Devices", "Laptops",
            ["Windows 11 laptop", "Windows 10 PC", "MacBook"],
            ["Check startup apps and disk usage.", "Run the vendor's hardware diagnostics.", "Install pending updates and drivers.",
             "Consider an SSD or RAM upgrade."]),
        new("Configure {p} for a new office", "Telephony", "VoIP",
            ["a SIP trunk", "Teams Phone", "3CX", "a cloud PBX"],
            ["Order numbers or port existing ones.", "Create users and extensions.", "Set business hours and call routing.",
             "Test incoming and outgoing calls."]),
        new("Clean up disk space on {p}", "Servers", "File server",
            ["a file server", "a Remote Desktop host", "an Exchange server"],
            ["Find large folders with TreeSize.", "Clear temp and update caches.", "Move archives to cold storage.",
             "Set quotas and an alert threshold."]),
        new("Set up offsite replication to {p}", "Backup & DR", "Offsite backup",
            ["Wasabi", "Backblaze B2", "Azure Blob Storage", "a second NAS"],
            ["Create the bucket with object lock.", "Add the repository with encryption.", "Configure the copy job.",
             "Test a restore from the offsite copy."]),
    ];

    public static readonly string[] Weekdays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];

    public static readonly string[] PersonalMailDomains = ["gmail.com", "outlook.com", "gmx.net", "proton.me", "web.de", "icloud.com"];

    public static readonly string[] ContactRemarks =
    [
        "Prefers phone over e-mail.", "Decision maker for the IT budget.", "Best reached in the morning.",
        "Works part-time (Mon–Wed).", "Very technical, likes details.", "Not technical — explain in plain language.",
        "Signs off quotes above 5,000.", "Main contact for day-to-day support.", "Introduced us to two other clients.",
        "Prefers Teams calls.", "Responds quickly on WhatsApp for urgent issues.", "Handles invoices and payments.",
        "Keyholder for the office — call before onsite visits.", "Met at a local business breakfast.",
        "Has admin rights for the Microsoft 365 tenant.", "Wants a monthly summary report.", "On parental leave until next quarter.",
        "Deputy: see linked contacts.", "Speaks German and English.", "Former colleague from an earlier project.",
    ];

    public static readonly string[] QuickNotes =
    [
        "Order toner for {org} ({model}).", "Ask {first} whether the new starter needs a laptop or a desktop.",
        "Check warranty of {item} before quoting a repair.", "Renew {domain} before the end of the month.",
        "Send {first} the offboarding checklist.", "Remind {org} about MFA for the remaining users.",
        "Look into alternatives to {product} for {org}.", "Call {first} back about the printer.",
        "{first} asked about a second monitor for the reception.", "Invoice the extra hours for {org} this month.",
        "Test the new RMM script on my lab PC first.", "Idea: offer {org} a fixed-price onboarding package.",
        "Password manager entry for {org} is missing the firewall admin account.", "Book the onsite day at {org} for next week.",
        "Check whether {product} renews automatically.", "Follow up on the quote with {first}.",
    ];

    public static readonly string[] VendorSubjects =
    [
        "Price list update", "RMA request", "Licence renewal", "Partner webinar", "Account review", "Delivery delay",
        "Support case escalation", "New distributor terms", "Demo unit request", "Certification exam voucher",
    ];

    public static readonly string[] Referrals =
    [
        "a recommendation from an existing client", "the website contact form", "a LinkedIn message",
        "a local business network meeting", "a trade fair", "a Google search", "their accountant", "a former colleague",
    ];
}
