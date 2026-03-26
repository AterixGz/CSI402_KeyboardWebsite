namespace KeyboardWebsiteProject.Models;

public class Product
{
    public int ProductId { get; set; }
    public string? Name { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string? BrandName { get; set; } // ดึงมาจากตาราง Brands
    public string? ImageUrl { get; set; }
    public string? CategoryName { get; set; } // ดึงมาจากตาราง Categories
    public string? Description { get; set; } // รายละเอียดสินค้า
    public List<Specification> Specifications { get; set; } = new List<Specification>(); // ข้อมูล specs
}

public class Specification
{
    public int SpecId { get; set; }
    public int ProductId { get; set; }
    public string? SpecKey { get; set; }
    public string? SpecValue { get; set; }
}

// Models สำหรับ API request
public class CreateProductRequest
{
    public string? Name { get; set; }
    public string? BrandName { get; set; }
    public string? CategoryName { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }
    public List<SpecificationRequest>? Specifications { get; set; }
}

public class SpecificationRequest
{
    public string? SpecKey { get; set; }
    public string? SpecValue { get; set; }
}

public class UpdateProductRequest
{
    public int ProductId { get; set; }
    public string? Name { get; set; }
    public string? BrandName { get; set; }
    public string? CategoryName { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }
    public List<SpecificationRequest>? Specifications { get; set; }
}