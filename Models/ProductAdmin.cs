namespace KeyboardWebsiteProject.Models;

public class Product
{
    public int ProductId { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string BrandName { get; set; } // ดึงมาจากตาราง Brands
    public string ImageUrl { get; set; }
    public string CategoryName { get; set; } // ดึงมาจากตาราง Categories
}