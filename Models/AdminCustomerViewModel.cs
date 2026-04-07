using System;
using System.Collections.Generic;

namespace KeyboardWebsiteProject.Models
{
    public class AdminCustomerViewModel
    {
        public int TotalCustomers { get; set; }
        public int NewThisMonth { get; set; }
        public int ActiveCustomers { get; set; }
        public int VipMembers { get; set; }
        public List<AdminCustomerItem> Customers { get; set; } = new();
    }

    public class AdminCustomerItem
    {
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public int Orders { get; set; }
        public decimal Spent { get; set; }
        public string Created { get; set; } = "-";
        public string LastOrder { get; set; } = "-";
        public string Color { get; set; } = "blue";
        public string Initials { get; set; } = "";
    }
}
