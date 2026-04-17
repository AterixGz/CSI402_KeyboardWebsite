namespace KeyboardWebsiteProject.Models;

public class PromotionViewModel
{
    public int Id { get; set; }
    public string Type { get; set; } = "coupon";
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Discount { get; set; } = string.Empty;
    public string DiscountType { get; set; } = "percent";
    public string MinSpend { get; set; } = "฿0";
    public bool IsFreeShipping { get; set; }
    public string Category { get; set; } = "All Categories";
    public string DateStart { get; set; } = string.Empty;
    public string DateEnd { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public int Used { get; set; }
    public int Limit { get; set; }
    public List<PromotionRequirementViewModel> Items { get; set; } = new();
    public string ItemJson { get; set; } = "[]";
}

public class PromotionRequirementViewModel
{
    public string Category { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int MinQty { get; set; }
    public int MaxQty { get; set; }
}
