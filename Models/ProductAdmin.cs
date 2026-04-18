namespace KeyboardWebsiteProject.Models;

public class Product
{
    public int ProductId { get; set; }
    public string? Name { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string? BrandName { get; set; } // ดึงมาจากตาราง Brands
    public string? ImageUrl { get; set; }
    public List<string> ImageUrls { get; set; } = new List<string>();
    public string? CategoryName { get; set; } // ดึงมาจากตาราง Categories
    public string? Description { get; set; } // รายละเอียดสินค้า
    public List<Specification> Specifications { get; set; } = new List<Specification>(); // ข้อมูล specs
    public List<Review> Reviews { get; set; } = new List<Review>();
    public List<ProductAttribute> Attributes { get; set; } = new List<ProductAttribute>();
    public Review? UserReview { get; set; }
    public int ReviewCount { get; set; }
    public decimal AverageRating { get; set; }
    public int Star5Count { get; set; }
    public int Star4Count { get; set; }
    public int Star3Count { get; set; }
    public int Star2Count { get; set; }
    public int Star1Count { get; set; }
}

public class Specification
{
    public int SpecId { get; set; }
    public int ProductId { get; set; }
    public string? SpecKey { get; set; }
    public string? SpecValue { get; set; }
}

public class ProductAttribute
{
    public int ProductId { get; set; }
    public string? AttributeType { get; set; }
    public string? AttributeValue { get; set; }
}

public class CartItem
{
    public int CartId { get; set; }
    public int ProductId { get; set; }
    public string? Name { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public int StockQuantity { get; set; }
    public bool IsSelected { get; set; }
    public string? ImageUrl { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? BrandName { get; set; }
    public decimal Total => Price * Quantity;
}

public class CartPromotion
{
    public int PromotionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DiscountAmount { get; set; }
    public decimal MinSpend { get; set; }
    public bool IsFreeShipping { get; set; }
    public bool IsActive { get; set; }
    public List<CartPromotionRequirement> Requirements { get; set; } = new();
}

public class CartPromotionRequirement
{
    public int? ProductId { get; set; }
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int MinQuantity { get; set; }
}

public class AddToCartRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; } = 1;
    public bool IsBuyNow { get; set; }
}

public class UserAddress
{
    public int AddressId { get; set; }
    public int UserId { get; set; }
    public string? ReceiverName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? AddressLine1 { get; set; }
    public string? SubDistrict { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? PostalCode { get; set; }
    public bool IsDefault { get; set; }
}

public class UpdateCartSelectionRequest
{
    public int CartId { get; set; }
    public bool IsSelected { get; set; }
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

public class Review
{
    public int ReviewId { get; set; }
    public int ProductId { get; set; }
    public int UserId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string? ReviewerName { get; set; }
    public DateTime ReviewDate { get; set; }
}