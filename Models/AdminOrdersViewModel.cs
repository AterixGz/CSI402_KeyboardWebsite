using System.Collections.Generic;

namespace KeyboardWebsiteProject.Models
{
    public class AdminOrdersViewModel
    {
        public List<AdminOrderItem> Orders { get; set; } = new();
    }

    public class AdminOrderItem
    {
        public string Id { get; set; } = "";
        public string First { get; set; } = "";
        public string Last { get; set; } = "";
        public string Email { get; set; } = "";
        public string Products { get; set; } = "";
        public int Items { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "";
        public string Date { get; set; } = "";
        public string Time { get; set; } = "";
        public string Color { get; set; } = "av-blue";
    }
}
