using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KeyboardWebsiteProject.Pages
{
    // ─── Sub-models ───────────────────────────────────────────────

    public class StatCard
    {
        public string Label       { get; set; } = "";
        public string Value       { get; set; } = "";
        public string Change      { get; set; } = "";   // e.g. "+12.5%"
        public bool   IsPositive  { get; set; } = true;
        public string IconClass   { get; set; } = "";   // e.g. "icon-green"
    }

    public class RecentOrder
    {
        public string Initials   { get; set; } = "";
        public string Name       { get; set; } = "";
        public string Product    { get; set; } = "";
        public string Amount     { get; set; } = "";
        public string Status     { get; set; } = "";    // shipped / processing / delivered / pending
        public string BadgeClass { get; set; } = "";    // e.g. "badge-shipped"
    }

    public class TopCustomer
    {
        public string Rank    { get; set; } = "";   // "#1", "#2", ...
        public string Name    { get; set; } = "";
        public string Email   { get; set; } = "";
        public string Amount  { get; set; } = "";
        public string Orders  { get; set; } = "";   // e.g. "12 orders"
    }

    // ─── Page Model ───────────────────────────────────────────────

    public class DashboardModel : PageModel
    {
        public List<StatCard>     Stats         { get; private set; } = new();
        public List<RecentOrder>  RecentOrders  { get; private set; } = new();
        public List<TopCustomer>  TopCustomers  { get; private set; } = new();
        public string             LastUpdated   { get; private set; } = "Just now";

        public void OnGet()
        {
            // ── Stat Cards ──────────────────────────────────────
            Stats = new List<StatCard>
            {
                new StatCard
                {
                    Label      = "Total Customers",
                    Value      = "2,847",
                    Change     = "+12.5% vs last month",
                    IsPositive = true,
                    IconClass  = "icon-green"
                },
                new StatCard
                {
                    Label      = "Total Orders",
                    Value      = "1,234",
                    Change     = "+8.2% vs last month",
                    IsPositive = true,
                    IconClass  = "icon-purple"
                },
                new StatCard
                {
                    Label      = "Products",
                    Value      = "156",
                    Change     = "+3 vs last month",
                    IsPositive = true,
                    IconClass  = "icon-blue"
                },
                new StatCard
                {
                    Label      = "Revenue",
                    Value      = "$48,560",
                    Change     = "-2.4% vs last month",
                    IsPositive = false,
                    IconClass  = "icon-orange"
                },
            };

            // ── Recent Orders ───────────────────────────────────
            RecentOrders = new List<RecentOrder>
            {
                new RecentOrder { Initials = "JD", Name = "John Doe",     Product = "GMK Olivia",            Amount = "$185", Status = "shipped",    BadgeClass = "badge-shipped" },
                new RecentOrder { Initials = "JS", Name = "Jane Smith",   Product = "Zoom75",                Amount = "$420", Status = "processing", BadgeClass = "badge-processing" },
                new RecentOrder { Initials = "MJ", Name = "Mike Johnson", Product = "Gateron Oil Kings x70", Amount = "$42",  Status = "delivered",  BadgeClass = "badge-delivered" },
                new RecentOrder { Initials = "SW", Name = "Sarah Wilson", Product = "Custom Coiled Cable",   Amount = "$65",  Status = "pending",    BadgeClass = "badge-pending" },
                new RecentOrder { Initials = "TB", Name = "Tom Brown",    Product = "GMMK Pro",              Amount = "$350", Status = "shipped",    BadgeClass = "badge-shipped" },
            };

            // ── Top Customers ───────────────────────────────────
            TopCustomers = new List<TopCustomer>
            {
                new TopCustomer { Rank = "#1", Name = "Alex Chen",      Email = "alex@example.com",  Amount = "$2,450", Orders = "12 orders" },
                new TopCustomer { Rank = "#2", Name = "Maria Garcia",   Email = "maria@example.com", Amount = "$1,890", Orders = "8 orders"  },
                new TopCustomer { Rank = "#3", Name = "James Wilson",   Email = "james@example.com", Amount = "$1,650", Orders = "7 orders"  },
                new TopCustomer { Rank = "#4", Name = "Emma Thompson",  Email = "emma@example.com",  Amount = "$1,420", Orders = "6 orders"  },
                new TopCustomer { Rank = "#5", Name = "David Kim",      Email = "david@example.com", Amount = "$1,280", Orders = "5 orders"  },
            };

            LastUpdated = "Just now";
        }
    }
}
