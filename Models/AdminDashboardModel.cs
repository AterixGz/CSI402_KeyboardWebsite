using System.Collections.Generic;

namespace KeyboardWebsiteProject.Models
{
    public class AdminDashboardModel
    {
        public List<StatCard> Stats { get; set; } = new();
        public List<RecentOrder> RecentOrders { get; set; } = new();
        public List<TopCustomer> TopCustomers { get; set; } = new();
        public string LastUpdated { get; set; } = "Just now";
    }

    public class StatCard
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string Change { get; set; } = "";
        public bool IsPositive { get; set; } = true;
        public string IconClass { get; set; } = "";
    }

    public class RecentOrder
    {
        public int OrderId { get; set; }
        public string Initials { get; set; } = "";
        public string Name { get; set; } = "";
        public string Product { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string Amount { get; set; } = "";
        public string Status { get; set; } = "";
        public string BadgeClass { get; set; } = "";
    }

    public class TopCustomer
    {
        public string Rank { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Amount { get; set; } = "";
        public string Orders { get; set; } = "";
    }
}
