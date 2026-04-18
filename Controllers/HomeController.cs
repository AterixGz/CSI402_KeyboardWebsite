using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using KeyboardWebsiteProject.Models;
using Stripe.Checkout;

namespace KeyboardWebsiteProject.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly MySqlConnection _connection;
    private readonly IConfiguration _configuration;

    public HomeController(ILogger<HomeController> logger, MySqlConnection connection, IConfiguration configuration)
    {
        _logger = logger;
        _connection = connection;
        _configuration = configuration;
    }

    public IActionResult Index()
    {
        var products = new List<Product>();

        try
        {
            // เปิดการเชื่อมต่อกับฐานข้อมูล
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // สร้าง SQL query ดึงข้อมูล Products กับ Product_Images, Categories และ Brands (ได้ 4 รายการ)
            string query = @"SELECT p.product_id, p.name, p.price, p.stock_quantity,
                                   COALESCE(pi.image_url, '~/image/default.png') AS image_url,
                                   p.description,
                                   COALESCE(c.category_name, 'Uncategorized') as category_name,
                                   COALESCE(b.brand_name, '') as brand_name,
                                   COALESCE(r.review_count, 0) AS review_count,
                                   COALESCE(r.avg_rating, 0) AS average_rating
                            FROM Products p 
                            LEFT JOIN Product_Images pi ON p.product_id = pi.product_id AND pi.is_main = 1
                            LEFT JOIN Categories c ON p.category_id = c.category_id 
                            LEFT JOIN Brands b ON p.brand_id = b.brand_id
                            LEFT JOIN (
                                SELECT product_id, COUNT(*) AS review_count, AVG(rating) AS avg_rating
                                FROM Reviews
                                GROUP BY product_id
                            ) r ON p.product_id = r.product_id
                            ORDER BY p.product_id DESC
                            LIMIT 4";
            
            using (MySqlCommand cmd = new MySqlCommand(query, _connection))
            {
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        products.Add(new Product
                        {
                            ProductId = reader.GetInt32("product_id"),
                            Name = reader.GetString("name"),
                            Price = reader.GetDecimal("price"),
                            StockQuantity = reader.GetInt32("stock_quantity"),
                            ImageUrl = reader.GetString("image_url") ?? "~/image/default.png",
                            CategoryName = reader.GetString("category_name"),
                            BrandName = reader.GetString("brand_name"),
                            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? "" : reader.GetString("description"),
                            ReviewCount = reader.IsDBNull(reader.GetOrdinal("review_count")) ? 0 : reader.GetInt32("review_count"),
                            AverageRating = reader.IsDBNull(reader.GetOrdinal("average_rating")) ? 0m : Convert.ToDecimal(reader["average_rating"])
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ViewBag.ErrorMessage = $"เกิดข้อผิดพลาด: {ex.Message}";
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }

        return View(products);
    }

    public IActionResult Shop(string? category = null)
    {
        var products = new List<Product>();

        try
        {
            // เปิดการเชื่อมต่อกับฐานข้อมูล
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // สร้าง SQL query ดึงข้อมูล Products กับ Product_Images, Categories และ Brands
            string query = @"SELECT p.product_id, p.name, p.price, p.stock_quantity,
                                   COALESCE(pi.image_url, '~/image/default.png') AS image_url,
                                   p.description,
                                   COALESCE(c.category_name, 'Uncategorized') as category_name,
                                   COALESCE(b.brand_name, '') as brand_name
                            FROM Products p 
                            LEFT JOIN Product_Images pi ON p.product_id = pi.product_id AND pi.is_main = 1
                            LEFT JOIN Categories c ON p.category_id = c.category_id 
                            LEFT JOIN Brands b ON p.brand_id = b.brand_id
                            ORDER BY p.product_id DESC";
            
            using (MySqlCommand cmd = new MySqlCommand(query, _connection))
            {
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        products.Add(new Product
                        {
                            ProductId = reader.GetInt32("product_id"),
                            Name = reader.GetString("name"),
                            Price = reader.GetDecimal("price"),
                            StockQuantity = reader.GetInt32("stock_quantity"),
                            ImageUrl = reader.GetString("image_url") ?? "~/image/default.png",
                            CategoryName = reader.GetString("category_name"),
                            BrandName = reader.GetString("brand_name"),
                            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? "" : reader.GetString("description")
                        });
                    }
                }
            }

            // โหลด specifications และ attributes สำหรับแต่ละ product
            string specQuery = "SELECT spec_id, product_id, spec_key, spec_value FROM Product_Specifications WHERE product_id = @productId";
            string attrQuery = @"SELECT pav.product_id, at.at_name AS attribute_type, av.av_value AS attribute_value
                                 FROM Product_Attribute_Mapping pav
                                 JOIN Attribute_Values av ON pav.av_id = av.av_id
                                 JOIN Attribute_Types at ON av.at_id = at.at_id
                                 WHERE pav.product_id = @productId";

            foreach (var product in products)
            {
                using (MySqlCommand specCmd = new MySqlCommand(specQuery, _connection))
                {
                    specCmd.Parameters.AddWithValue("@productId", product.ProductId);
                    using (MySqlDataReader reader = specCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            product.Specifications.Add(new Specification
                            {
                                SpecId = reader.GetInt32("spec_id"),
                                ProductId = reader.GetInt32("product_id"),
                                SpecKey = reader.GetString("spec_key"),
                                SpecValue = reader.GetString("spec_value")
                            });
                        }
                    }
                }

                using (MySqlCommand attrCmd = new MySqlCommand(attrQuery, _connection))
                {
                    attrCmd.Parameters.AddWithValue("@productId", product.ProductId);
                    using (MySqlDataReader reader = attrCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            product.Attributes.Add(new ProductAttribute
                            {
                                ProductId = reader.GetInt32("product_id"),
                                AttributeType = reader.IsDBNull(reader.GetOrdinal("attribute_type")) ? null : reader.GetString("attribute_type"),
                                AttributeValue = reader.IsDBNull(reader.GetOrdinal("attribute_value")) ? null : reader.GetString("attribute_value")
                            });
                        }
                    }
                }

                const string imageQuery = "SELECT image_url FROM Product_Images WHERE product_id = @productId AND image_url IS NOT NULL AND image_url <> '' ORDER BY is_main DESC, image_id ASC";
                using (MySqlCommand imageCmd = new MySqlCommand(imageQuery, _connection))
                {
                    imageCmd.Parameters.AddWithValue("@productId", product.ProductId);
                    using (MySqlDataReader reader = imageCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            product.ImageUrls.Add(reader.GetString("image_url"));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ViewBag.ErrorMessage = $"เกิดข้อผิดพลาด: {ex.Message}";
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }

        ViewBag.SelectedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        ViewBag.Products = products;
        return View();
    }

    [HttpPost]
    public IActionResult AddToCart([FromBody] AddToCartRequest request)
    {
        if (request == null || request.ProductId <= 0)
        {
            if (Request.Headers.ContainsKey("X-Requested-With"))
            {
                return Json(new { success = false, message = "สินค้าไม่ถูกต้อง" });
            }
            return BadRequest("Invalid product");
        }

        int productId = request.ProductId;
        int quantity = request.Quantity;
        bool isBuyNow = request.IsBuyNow;

        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            if (Request.Headers.ContainsKey("X-Requested-With"))
            {
                var loginUrl = Url.Action("Login", "Account");
                return new JsonResult(new { success = false, message = "กรุณาเข้าสู่ระบบก่อนเพิ่มสินค้าลงในตะกร้า", redirectUrl = loginUrl })
                {
                    StatusCode = 401
                };
            }
            TempData["CartMessage"] = "กรุณาเข้าสู่ระบบก่อนเพิ่มสินค้าลงในตะกร้า";
            return RedirectToAction("Login", "Account");
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            // ตรวจสอบว่าสินค้ามีอยู่และเช็คสต็อก
            const string checkProductQuery = "SELECT stock_quantity FROM Products WHERE product_id = @productId";
            int stockQuantity = 0;

            using (var cmd = new MySqlCommand(checkProductQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", productId);
                _logger.LogInformation($"Checking product {productId} in database");
                var result = cmd.ExecuteScalar();
                if (result == null)
                {
                    _logger.LogWarning($"Product {productId} not found in database");
                    if (Request.Headers.ContainsKey("X-Requested-With"))
                    {
                        return Json(new { success = false, message = $"ไม่พบสินค้า ID {productId} ในระบบ กรุณาลองใหม่อีกครั้ง" });
                    }
                    TempData["CartMessage"] = "สินค้าไม่ถูกต้อง";
                    return RedirectToAction("Productdetails", new { id = productId });
                }

                stockQuantity = Convert.ToInt32(result);
            }

            if (stockQuantity <= 0)
            {
                if (Request.Headers.ContainsKey("X-Requested-With"))
                {
                    return Json(new { success = false, message = "สินค้าหมด" });
                }
                TempData["CartMessage"] = "สินค้าหมด";
                return RedirectToAction("Productdetails", new { id = productId });
            }

            // ใช้ quantity ที่ถูกต้อง
            quantity = Math.Max(1, quantity);
            quantity = Math.Min(quantity, stockQuantity);

            // ตรวจสอบว่ามีสินค้าชุดนี้ในตะกร้าแล้วหรือยัง
            const string selectCartItemQuery = "SELECT cart_id, quantity FROM Cart WHERE user_id = @userId AND product_id = @productId LIMIT 1";
            int existingCartId = 0;
            int existingQuantity = 0;

            // Close and reopen connection to ensure fresh state
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            using (var cmd = new MySqlCommand(selectCartItemQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                cmd.Parameters.AddWithValue("@productId", productId);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        existingCartId = reader.GetInt32("cart_id");
                        existingQuantity = reader.GetInt32("quantity");
                    }
                }
            }

            if (existingCartId > 0)
            {
                int newQuantity = Math.Min(stockQuantity, existingQuantity + quantity);
                const string updateCartQuery = "UPDATE Cart SET quantity = @quantity WHERE cart_id = @cartId";
                using (var cmd = new MySqlCommand(updateCartQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@quantity", newQuantity);
                    cmd.Parameters.AddWithValue("@cartId", existingCartId);
                    cmd.ExecuteNonQuery();
                }
            }
            else
            {
                const string insertCartQuery = "INSERT INTO Cart (user_id, product_id, quantity, added_at) VALUES (@userId, @productId, @quantity, NOW())";
                using (var cmd = new MySqlCommand(insertCartQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId.Value);
                    cmd.Parameters.AddWithValue("@productId", productId);
                    cmd.Parameters.AddWithValue("@quantity", quantity);
                    cmd.ExecuteNonQuery();
                }
            }

            // ถ้าเป็น AJAX request จะ return JSON
            if (Request.Headers.ContainsKey("X-Requested-With"))
            {
                return Json(new { success = true, message = "เพิ่มสินค้าลงตะกร้าเรียบร้อยแล้ว" });
            }

            TempData["CartMessage"] = "เพิ่มสินค้าลงตะกร้าเรียบร้อยแล้ว";
            
            // ถ้ากด Buy Now ให้ redirect ไป Cart
            if (isBuyNow)
            {
                return RedirectToAction("Cart");
            }

            // ถ้ากด Add to Cart จาก form submit ให้ redirect ไป Cart (แต่ปกติถ้า AJAX ส่วนนี้ไม่ใช้)
            return RedirectToAction("Cart");
        }
        catch (Exception ex)
        {
            if (Request.Headers.ContainsKey("X-Requested-With"))
            {
                return Json(new { success = false, message = $"เกิดข้อผิดพลาดขณะเพิ่มสินค้าลงตะกร้า: {ex.Message}" });
            }
            TempData["CartMessage"] = $"เกิดข้อผิดพลาดขณะเพิ่มสินค้าลงตะกร้า: {ex.Message}";
            return RedirectToAction("Productdetails", new { id = productId });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    public IActionResult Cart()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            TempData["CartMessage"] = "กรุณาเข้าสู่ระบบเพื่อดูตะกร้า";
            return RedirectToAction("Login", "Account");
        }

        var cartItems = new List<CartItem>();

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            string query = @"SELECT c.cart_id, c.product_id, c.quantity, c.is_selected,
                                   p.name, p.price, p.stock_quantity,
                                   COALESCE(pi.image_url, '~/image/default.png') AS image_url,
                                   p.category_id AS category_id,
                                   COALESCE(cat.category_name, 'Uncategorized') AS category_name,
                                   COALESCE(b.brand_name, '') AS brand_name
                             FROM Cart c
                             JOIN Products p ON c.product_id = p.product_id
                             LEFT JOIN Product_Images pi ON p.product_id = pi.product_id AND pi.is_main = 1
                             LEFT JOIN Categories cat ON p.category_id = cat.category_id
                             LEFT JOIN Brands b ON p.brand_id = b.brand_id
                             WHERE c.user_id = @userId
                             ORDER BY c.added_at DESC";

            using (var cmd = new MySqlCommand(query, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cartItems.Add(new CartItem
                        {
                            CartId = reader.GetInt32("cart_id"),
                            ProductId = reader.GetInt32("product_id"),
                            Quantity = reader.GetInt32("quantity"),
                            IsSelected = reader.GetBoolean("is_selected"),
                            Name = reader.GetString("name"),
                            Price = reader.GetDecimal("price"),
                            StockQuantity = reader.GetInt32("stock_quantity"),
                            ImageUrl = reader.IsDBNull(reader.GetOrdinal("image_url")) ? "~/image/default.png" : reader.GetString("image_url"),
                            CategoryId = reader.IsDBNull(reader.GetOrdinal("category_id")) ? (int?)null : reader.GetInt32("category_id"),
                            CategoryName = reader.GetString("category_name"),
                            BrandName = reader.GetString("brand_name")
                        });
                    }
                }
            }

            var userAddresses = new List<UserAddress>();
            UserAddress? selectedAddress = null;
            string addressQuery = @"SELECT address_id, user_id, receiver_name, phone_number, address_line1,
                                           sub_district, district, province, postal_code, is_default
                                    FROM User_Addresses
                                    WHERE user_id = @userId
                                    ORDER BY is_default DESC, created_at DESC";
            using (var addressCmd = new MySqlCommand(addressQuery, _connection))
            {
                addressCmd.Parameters.AddWithValue("@userId", userId.Value);
                using (var reader = addressCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var address = new UserAddress
                        {
                            AddressId = reader.GetInt32("address_id"),
                            UserId = reader.GetInt32("user_id"),
                            ReceiverName = reader.IsDBNull(reader.GetOrdinal("receiver_name")) ? null : reader.GetString("receiver_name"),
                            PhoneNumber = reader.IsDBNull(reader.GetOrdinal("phone_number")) ? null : reader.GetString("phone_number"),
                            AddressLine1 = reader.IsDBNull(reader.GetOrdinal("address_line1")) ? null : reader.GetString("address_line1"),
                            SubDistrict = reader.IsDBNull(reader.GetOrdinal("sub_district")) ? null : reader.GetString("sub_district"),
                            District = reader.IsDBNull(reader.GetOrdinal("district")) ? null : reader.GetString("district"),
                            Province = reader.IsDBNull(reader.GetOrdinal("province")) ? null : reader.GetString("province"),
                            PostalCode = reader.IsDBNull(reader.GetOrdinal("postal_code")) ? null : reader.GetString("postal_code"),
                            IsDefault = reader.GetBoolean("is_default")
                        };
                        userAddresses.Add(address);
                    }
                }
            }

            if (userAddresses.Any())
            {
                selectedAddress = userAddresses.First();
            }

            ViewBag.UserAddresses = userAddresses;
            ViewBag.SelectedUserAddress = selectedAddress;
            ViewBag.SystemPromotions = GetActiveSystemPromotions();
            ViewBag.CartMessage = TempData["CartMessage"];
            return View(cartItems);
        }
        catch (Exception ex)
        {
            ViewBag.ErrorMessage = $"เกิดข้อผิดพลาดขณะโหลดตะกร้า: {ex.Message}";
            return View(cartItems);
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    [HttpGet]
    public IActionResult DebugProducts()
    {
        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            var products = new List<object>();
            string query = "SELECT product_id, name, stock_quantity FROM Products ORDER BY product_id DESC LIMIT 10";
            
            using (var cmd = new MySqlCommand(query, _connection))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        products.Add(new
                        {
                            product_id = reader.GetInt32("product_id"),
                            name = reader.GetString("name"),
                            stock_quantity = reader.GetInt32("stock_quantity")
                        });
                    }
                }
            }

            return Json(new { success = true, products = products });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Database error: {ex.Message}");
            return Json(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    [HttpGet]
    public IActionResult GetCoupon(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Json(new { success = false, message = "กรุณากรอกรหัสคูปอง" });
        }

        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบก่อนใช้คูปอง" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            const string query = @"SELECT coupon_id, code, discount_value, discount_type, expiry_date, usage_limit, used_count
                                   FROM Coupons
                                   WHERE code = @code
                                   LIMIT 1";

            using var cmd = new MySqlCommand(query, _connection);
            cmd.Parameters.AddWithValue("@code", code.Trim());

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return Json(new { success = false, message = "คูปองไม่ถูกต้อง" });
            }

            var couponId = reader.GetInt32("coupon_id");
            var expiryDate = reader.GetDateTime("expiry_date");
            if (DateTime.UtcNow > expiryDate.ToUniversalTime())
            {
                return Json(new { success = false, message = "คูปองนี้หมดอายุแล้ว" });
            }

            var usageLimit = reader.IsDBNull(reader.GetOrdinal("usage_limit")) ? (int?)null : reader.GetInt32("usage_limit");
            var usedCount = reader.IsDBNull(reader.GetOrdinal("used_count")) ? 0 : reader.GetInt32("used_count");
            if (usageLimit.HasValue && usedCount >= usageLimit.Value)
            {
                return Json(new { success = false, message = "คูปองนี้ถูกใช้ครบโควต้าแล้ว" });
            }

            var discountValue = reader.GetDecimal("discount_value");
            var discountType = reader.GetString("discount_type");
            reader.Close();

            const string usageQuery = @"SELECT COUNT(*) FROM Coupon_Usage WHERE coupon_id = @couponId AND user_id = @userId";
            using (var usageCmd = new MySqlCommand(usageQuery, _connection))
            {
                usageCmd.Parameters.AddWithValue("@couponId", couponId);
                usageCmd.Parameters.AddWithValue("@userId", userId.Value);
                var alreadyUsed = Convert.ToInt32(usageCmd.ExecuteScalar());
                if (alreadyUsed > 0)
                {
                    return Json(new { success = false, message = "คุณใช้คูปองนี้ไปแล้ว" });
                }
            }

            return Json(new
            {
                success = true,
                coupon = new
                {
                    code = code.Trim().ToUpperInvariant(),
                    discountValue,
                    discountType = discountType?.ToLowerInvariant(),
                    expiryDate = expiryDate.ToString("o"),
                    usageLimit,
                    usedCount
                }
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาดในการตรวจสอบคูปอง: {ex.Message}" });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    private List<CartItem> GetCartItems(int userId)
    {
        var cartItems = new List<CartItem>();
        string query = @"SELECT c.cart_id, c.product_id, c.quantity, c.is_selected,
                                   p.name, p.price, p.stock_quantity,
                                   COALESCE(pi.image_url, '~/image/default.png') AS image_url,
                                   p.category_id AS category_id,
                                   COALESCE(cat.category_name, 'Uncategorized') AS category_name,
                                   COALESCE(b.brand_name, '') AS brand_name
                             FROM Cart c
                             JOIN Products p ON c.product_id = p.product_id
                             LEFT JOIN Product_Images pi ON p.product_id = pi.product_id AND pi.is_main = 1
                             LEFT JOIN Categories cat ON p.category_id = cat.category_id
                             LEFT JOIN Brands b ON p.brand_id = b.brand_id
                             WHERE c.user_id = @userId
                             ORDER BY c.added_at DESC";
        using (var cmd = new MySqlCommand(query, _connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    cartItems.Add(new CartItem
                    {
                        CartId = reader.GetInt32("cart_id"),
                        ProductId = reader.GetInt32("product_id"),
                        Quantity = reader.GetInt32("quantity"),
                        IsSelected = reader.GetBoolean("is_selected"),
                        Name = reader.GetString("name"),
                        Price = reader.GetDecimal("price"),
                        StockQuantity = reader.GetInt32("stock_quantity"),
                        ImageUrl = reader.IsDBNull(reader.GetOrdinal("image_url")) ? "~/image/default.png" : reader.GetString("image_url"),
                        CategoryId = reader.IsDBNull(reader.GetOrdinal("category_id")) ? (int?)null : reader.GetInt32("category_id"),
                        CategoryName = reader.GetString("category_name"),
                        BrandName = reader.GetString("brand_name")
                    });
                }
            }
        }

        return cartItems;
    }

    private List<CartItem> GetSelectedCartItems(int userId)
    {
        return GetCartItems(userId).Where(item => item.IsSelected).ToList();
    }

    private List<CartPromotion> GetActiveSystemPromotions()
    {
        var promotionsById = new Dictionary<int, CartPromotion>();
        bool openedHere = false;

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
                openedHere = true;
            }

            const string promoSql = @"SELECT promo_id, promo_name, COALESCE(min_spend, 0) AS min_spend,
                                           COALESCE(discount_amount, 0) AS discount_amount,
                                           COALESCE(is_free_shipping, 0) AS is_free_shipping,
                                           COALESCE(is_active, 0) AS is_active
                                      FROM Promotions
                                     WHERE is_active = 1
                                       AND (start_date IS NULL OR start_date <= NOW())
                                       AND (end_date IS NULL OR end_date >= NOW())";

            using (var promoCmd = new MySqlCommand(promoSql, _connection))
            using (var reader = promoCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var promotion = new CartPromotion
                    {
                        PromotionId = reader.GetInt32("promo_id"),
                        Name = reader.IsDBNull(reader.GetOrdinal("promo_name")) ? string.Empty : reader.GetString("promo_name"),
                        MinSpend = reader.IsDBNull(reader.GetOrdinal("min_spend")) ? 0m : reader.GetDecimal("min_spend"),
                        DiscountAmount = reader.IsDBNull(reader.GetOrdinal("discount_amount")) ? 0m : reader.GetDecimal("discount_amount"),
                        IsFreeShipping = reader.IsDBNull(reader.GetOrdinal("is_free_shipping")) ? false : reader.GetBoolean("is_free_shipping"),
                        IsActive = reader.IsDBNull(reader.GetOrdinal("is_active")) ? false : reader.GetBoolean("is_active")
                    };

                    promotionsById[promotion.PromotionId] = promotion;
                }
            }

            const string reqSql = @"SELECT pr.promo_id, pr.category_id, pr.product_id, pr.min_quantity,
                                           COALESCE(c.category_name, pc.category_name, '') AS category_name
                                      FROM Promotion_Requirements pr
                                      LEFT JOIN Categories c ON pr.category_id = c.category_id
                                      LEFT JOIN Products p ON pr.product_id = p.product_id
                                      LEFT JOIN Categories pc ON p.category_id = pc.category_id";

            using (var reqCmd = new MySqlCommand(reqSql, _connection))
            using (var reqReader = reqCmd.ExecuteReader())
            {
                while (reqReader.Read())
                {
                    var promoId = reqReader.GetInt32("promo_id");
                    if (!promotionsById.TryGetValue(promoId, out var promotion))
                    {
                        continue;
                    }

                    var requirement = new CartPromotionRequirement
                    {
                        ProductId = reqReader.IsDBNull(reqReader.GetOrdinal("product_id"))
                            ? (int?)null
                            : reqReader.GetInt32("product_id"),
                        CategoryId = reqReader.IsDBNull(reqReader.GetOrdinal("category_id"))
                            ? (int?)null
                            : reqReader.GetInt32("category_id"),
                        CategoryName = reqReader.IsDBNull(reqReader.GetOrdinal("category_name"))
                            ? string.Empty
                            : reqReader.GetString("category_name"),
                        MinQuantity = reqReader.IsDBNull(reqReader.GetOrdinal("min_quantity"))
                            ? 0
                            : reqReader.GetInt32("min_quantity")
                    };

                    promotion.Requirements.Add(requirement);
                }
            }
        }
        finally
        {
            if (openedHere && _connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }

        return promotionsById.Values.ToList();
    }

    private bool IsPromotionEligible(CartPromotion promotion, List<CartItem> selectedItems, decimal subtotal)
    {
        if (promotion == null || !promotion.IsActive) return false;
        if (subtotal < promotion.MinSpend) return false;
        if (promotion.Requirements == null || promotion.Requirements.Count == 0) return true;

        return promotion.Requirements.All(req =>
        {
            var requiredQty = req.MinQuantity;

            if (req.ProductId.HasValue)
            {
                var item = selectedItems.FirstOrDefault(i => i.ProductId == req.ProductId.Value);
                return item != null && item.Quantity >= requiredQty;
            }

            if (req.CategoryId.HasValue)
            {
                var totalQty = selectedItems
                    .Where(i => i.CategoryId == req.CategoryId.Value)
                    .Sum(i => i.Quantity);
                return totalQty >= requiredQty;
            }

            var categoryName = (req.CategoryName ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(categoryName) && !string.Equals(categoryName, "all categories", StringComparison.OrdinalIgnoreCase))
            {
                var normalizedCategory = categoryName.ToLowerInvariant();
                var totalQty = selectedItems
                    .Where(i => (i.CategoryName ?? string.Empty).Trim().ToLowerInvariant() == normalizedCategory)
                    .Sum(i => i.Quantity);
                return totalQty >= requiredQty;
            }

            var totalSelectedQty = selectedItems.Sum(i => i.Quantity);
            return totalSelectedQty >= requiredQty;
        });
    }

    private (CartPromotion? selectedDiscountPromotion, CartPromotion? selectedFreeShippingPromotion, decimal promotionDiscount, decimal shippingFee) GetSelectedCheckoutPromotions(List<CartItem> selectedItems, decimal subtotal, int? discountPromotionId, int? freeShippingPromotionId)
    {
        var activePromotions = GetActiveSystemPromotions();
        var eligiblePromotions = activePromotions.Where(p => IsPromotionEligible(p, selectedItems, subtotal)).ToList();

        CartPromotion? selectedDiscountPromotion = null;
        CartPromotion? selectedFreeShippingPromotion = null;

        if (discountPromotionId.HasValue)
        {
            selectedDiscountPromotion = eligiblePromotions.FirstOrDefault(p => p.PromotionId == discountPromotionId.Value && !p.IsFreeShipping && p.DiscountAmount > 0);
        }

        if (selectedDiscountPromotion == null)
        {
            selectedDiscountPromotion = eligiblePromotions
                .Where(p => !p.IsFreeShipping && p.DiscountAmount > 0)
                .OrderByDescending(p => p.DiscountAmount)
                .FirstOrDefault();
        }

        if (freeShippingPromotionId.HasValue)
        {
            selectedFreeShippingPromotion = eligiblePromotions.FirstOrDefault(p => p.PromotionId == freeShippingPromotionId.Value && p.IsFreeShipping);
        }

        if (selectedFreeShippingPromotion == null)
        {
            selectedFreeShippingPromotion = eligiblePromotions.FirstOrDefault(p => p.IsFreeShipping);
        }

        var promotionDiscount = selectedDiscountPromotion != null
            ? Math.Min(subtotal, selectedDiscountPromotion.DiscountAmount)
            : 0m;

        var shippingFee = selectedFreeShippingPromotion != null ? 0m : 150m;

        return (selectedDiscountPromotion, selectedFreeShippingPromotion, promotionDiscount, shippingFee);
    }

    private bool TryGetCouponByCode(string code, int userId, out int couponId, out decimal discountValue, out string discountType, out string errorMessage)
    {
        couponId = 0;
        discountValue = 0m;
        discountType = string.Empty;
        errorMessage = string.Empty;

        const string query = @"SELECT coupon_id, discount_value, discount_type, expiry_date, usage_limit, used_count
                               FROM Coupons
                               WHERE code = @code
                               LIMIT 1";

        using var cmd = new MySqlCommand(query, _connection);
        cmd.Parameters.AddWithValue("@code", code.Trim());

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            errorMessage = "คูปองไม่ถูกต้อง";
            return false;
        }

        var couponIdValue = reader.GetInt32("coupon_id");
        var expiryDate = reader.GetDateTime("expiry_date");
        if (DateTime.UtcNow > expiryDate.ToUniversalTime())
        {
            errorMessage = "คูปองนี้หมดอายุแล้ว";
            return false;
        }

        var usageLimit = reader.IsDBNull(reader.GetOrdinal("usage_limit")) ? (int?)null : reader.GetInt32("usage_limit");
        var usedCount = reader.IsDBNull(reader.GetOrdinal("used_count")) ? 0 : reader.GetInt32("used_count");
        if (usageLimit.HasValue && usedCount >= usageLimit.Value)
        {
            errorMessage = "คูปองนี้ถูกใช้ครบโควต้าแล้ว";
            return false;
        }

        var discountValueValue = reader.GetDecimal("discount_value");
        var discountTypeValue = reader.GetString("discount_type")?.ToLowerInvariant() ?? string.Empty;
        reader.Close();

        const string usageQuery = @"SELECT COUNT(*) FROM Coupon_Usage WHERE coupon_id = @couponId AND user_id = @userId";
        using (var usageCmd = new MySqlCommand(usageQuery, _connection))
        {
            usageCmd.Parameters.AddWithValue("@couponId", couponIdValue);
            usageCmd.Parameters.AddWithValue("@userId", userId);
            var alreadyUsed = Convert.ToInt32(usageCmd.ExecuteScalar());
            if (alreadyUsed > 0)
            {
                errorMessage = "คุณใช้คูปองนี้ไปแล้ว";
                return false;
            }
        }

        couponId = couponIdValue;
        discountValue = discountValueValue;
        discountType = discountTypeValue;

        return true;
    }

    private decimal CalculateCouponAmount(decimal subtotal, string discountType, decimal discountValue)
    {
        if (discountType == "percentage")
        {
            return Math.Min(subtotal * discountValue / 100m, subtotal);
        }

        if (discountType == "fixed")
        {
            return Math.Min(discountValue, subtotal);
        }

        return 0m;
    }

    private int InsertOrder(int userId, int shippingAddressId, int? couponId, decimal subtotal, decimal discountAmount, decimal totalAmount, string status)
    {
        const string sql = @"INSERT INTO Orders (user_id, shipping_address_id, coupon_id, order_date, subtotal, discount_amount, total_amount, status)
                             VALUES (@userId, @shippingAddressId, @couponId, NOW(), @subtotal, @discountAmount, @totalAmount, @status)";
        using var cmd = new MySqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@shippingAddressId", shippingAddressId);
        cmd.Parameters.AddWithValue("@couponId", couponId.HasValue ? (object)couponId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@subtotal", subtotal);
        cmd.Parameters.AddWithValue("@discountAmount", discountAmount);
        cmd.Parameters.AddWithValue("@totalAmount", totalAmount);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.ExecuteNonQuery();
        return (int)cmd.LastInsertedId;
    }

    private void InsertOrderDetails(int orderId, List<CartItem> items)
    {
        const string sql = @"INSERT INTO OrderDetails (order_id, product_id, quantity, unit_price)
                             VALUES (@orderId, @productId, @quantity, @unitPrice)";
        using var cmd = new MySqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@orderId", orderId);
        cmd.Parameters.Add("@productId", MySqlDbType.Int32);
        cmd.Parameters.Add("@quantity", MySqlDbType.Int32);
        cmd.Parameters.Add("@unitPrice", MySqlDbType.Decimal);

        foreach (var item in items)
        {
            cmd.Parameters["@productId"].Value = item.ProductId;
            cmd.Parameters["@quantity"].Value = item.Quantity;
            cmd.Parameters["@unitPrice"].Value = item.Price;
            cmd.ExecuteNonQuery();
        }
    }

    private void RecordCouponUsage(int couponId, int userId, int orderId)
    {
        const string sql = "INSERT INTO Coupon_Usage (coupon_id, user_id, order_id, used_at) VALUES (@couponId, @userId, @orderId, NOW())";
        using var cmd = new MySqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@couponId", couponId);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@orderId", orderId);
        cmd.ExecuteNonQuery();
    }

    private void IncrementCouponUsedCount(int couponId)
    {
        const string sql = "UPDATE Coupons SET used_count = used_count + 1 WHERE coupon_id = @couponId";
        using var cmd = new MySqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@couponId", couponId);
        cmd.ExecuteNonQuery();
    }

    private void DeleteSelectedCartItems(int userId)
    {
        const string sql = "DELETE FROM Cart WHERE user_id = @userId AND is_selected = 1";
        using var cmd = new MySqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.ExecuteNonQuery();
    }

    [HttpPost]
    public IActionResult Checkout(int? addressId, string couponCode, int? discountPromotionId, int? freeShippingPromotionId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            if (addressId == null || addressId <= 0)
            {
                TempData["CartMessage"] = "กรุณาเลือกที่อยู่จัดส่งก่อนชำระเงิน";
                return RedirectToAction("Cart");
            }

            const string addressQuery = @"SELECT COUNT(*) FROM User_Addresses WHERE address_id = @addressId AND user_id = @userId";
            using (var addressCmd = new MySqlCommand(addressQuery, _connection))
            {
                addressCmd.Parameters.AddWithValue("@addressId", addressId.Value);
                addressCmd.Parameters.AddWithValue("@userId", userId.Value);
                var count = Convert.ToInt32(addressCmd.ExecuteScalar());
                if (count == 0)
                {
                    TempData["CartMessage"] = "ที่อยู่จัดส่งไม่ถูกต้อง";
                    return RedirectToAction("Cart");
                }
            }

            var cartItems = GetSelectedCartItems(userId.Value);
            if (!cartItems.Any())
            {
                TempData["CartMessage"] = "กรุณาเลือกสินค้าอย่างน้อย 1 รายการก่อนชำระเงิน";
                return RedirectToAction("Cart");
            }

            int? couponId = null;
            string couponCodeStored = string.Empty;
            decimal couponDiscount = 0m;
            if (!string.IsNullOrWhiteSpace(couponCode))
            {
                if (!TryGetCouponByCode(couponCode, userId.Value, out int availableCouponId, out decimal discountValue, out string discountType, out string couponError))
                {
                    TempData["CartMessage"] = couponError;
                    return RedirectToAction("Cart");
                }

                couponId = availableCouponId;
                couponCodeStored = couponCode.Trim();
            }

            var subtotal = cartItems.Sum(item => item.Price * item.Quantity);

            HttpContext.Session.SetInt32("CheckoutAddressId", addressId.Value);
            if (couponId.HasValue)
            {
                HttpContext.Session.SetInt32("CheckoutCouponId", couponId.Value);
                HttpContext.Session.SetString("CheckoutCouponCode", couponCodeStored);
            }
            else
            {
                HttpContext.Session.Remove("CheckoutCouponId");
                HttpContext.Session.Remove("CheckoutCouponCode");
            }

            if (discountPromotionId.HasValue)
            {
                HttpContext.Session.SetInt32("CheckoutDiscountPromotionId", discountPromotionId.Value);
            }
            else
            {
                HttpContext.Session.Remove("CheckoutDiscountPromotionId");
            }

            if (freeShippingPromotionId.HasValue)
            {
                HttpContext.Session.SetInt32("CheckoutFreeShippingPromotionId", freeShippingPromotionId.Value);
            }
            else
            {
                HttpContext.Session.Remove("CheckoutFreeShippingPromotionId");
            }

            var promotionResult = GetSelectedCheckoutPromotions(cartItems, subtotal, discountPromotionId, freeShippingPromotionId);
            var promotionDiscount = promotionResult.promotionDiscount;
            var shippingFee = promotionResult.shippingFee;
            var subtotalAfterPromotion = Math.Max(0, subtotal - promotionDiscount);
            if (couponId.HasValue)
            {
                if (!TryGetCouponByCode(couponCodeStored, userId.Value, out _, out decimal discountValue, out string discountType, out _))
                {
                    TempData["CartMessage"] = "คูปองไม่สามารถใช้งานได้ในขณะนี้";
                    return RedirectToAction("Cart");
                }
                couponDiscount = CalculateCouponAmount(subtotalAfterPromotion, discountType, discountValue);
            }
            var totalAmount = Math.Max(0, subtotalAfterPromotion - couponDiscount + shippingFee);

            if (totalAmount <= 0)
            {
                var totalDiscount = couponDiscount + promotionDiscount;
                var orderId = InsertOrder(userId.Value, addressId.Value, couponId, subtotal, totalDiscount, totalAmount, "paid");
                InsertOrderDetails(orderId, cartItems);
                if (couponId.HasValue)
                {
                    RecordCouponUsage(couponId.Value, userId.Value, orderId);
                    IncrementCouponUsedCount(couponId.Value);
                }
                DeleteSelectedCartItems(userId.Value);
                HttpContext.Session.Remove("CheckoutAddressId");
                HttpContext.Session.Remove("CheckoutCouponId");
                HttpContext.Session.Remove("CheckoutCouponCode");
                HttpContext.Session.Remove("CheckoutDiscountPromotionId");
                HttpContext.Session.Remove("CheckoutFreeShippingPromotionId");
                TempData["CartMessage"] = "ชำระเงินเรียบร้อยแล้ว";
                return RedirectToAction("Cart");
            }

            UserAddress? selectedAddress = null;
            const string selectedAddressQuery = @"SELECT address_id, user_id, receiver_name, phone_number, address_line1,
                                           sub_district, district, province, postal_code, is_default
                                    FROM User_Addresses
                                    WHERE address_id = @addressId AND user_id = @userId
                                    LIMIT 1";
            using (var addressCmd = new MySqlCommand(selectedAddressQuery, _connection))
            {
                addressCmd.Parameters.AddWithValue("@addressId", addressId.Value);
                addressCmd.Parameters.AddWithValue("@userId", userId.Value);
                using (var reader = addressCmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        selectedAddress = new UserAddress
                        {
                            AddressId = reader.GetInt32("address_id"),
                            UserId = reader.GetInt32("user_id"),
                            ReceiverName = reader.IsDBNull(reader.GetOrdinal("receiver_name")) ? null : reader.GetString("receiver_name"),
                            PhoneNumber = reader.IsDBNull(reader.GetOrdinal("phone_number")) ? null : reader.GetString("phone_number"),
                            AddressLine1 = reader.IsDBNull(reader.GetOrdinal("address_line1")) ? null : reader.GetString("address_line1"),
                            SubDistrict = reader.IsDBNull(reader.GetOrdinal("sub_district")) ? null : reader.GetString("sub_district"),
                            District = reader.IsDBNull(reader.GetOrdinal("district")) ? null : reader.GetString("district"),
                            Province = reader.IsDBNull(reader.GetOrdinal("province")) ? null : reader.GetString("province"),
                            PostalCode = reader.IsDBNull(reader.GetOrdinal("postal_code")) ? null : reader.GetString("postal_code"),
                            IsDefault = reader.GetBoolean("is_default")
                        };
                    }
                }
            }

            // Return checkout view instead of redirecting to Stripe
            ViewBag.StripePublishableKey = _configuration["Stripe:PublishableKey"] ?? string.Empty;
            ViewBag.SubTotal = subtotal;
            ViewBag.Discount = couponDiscount;
            ViewBag.PromotionDiscount = promotionDiscount;
            ViewBag.ShippingFee = shippingFee;
            ViewBag.TotalAmount = totalAmount;
            ViewBag.SelectedPromotionName = promotionResult.selectedDiscountPromotion?.Name ?? string.Empty;
            ViewBag.SelectedFreeShippingPromotionName = promotionResult.selectedFreeShippingPromotion?.Name ?? string.Empty;
            ViewBag.CartItems = cartItems;
            ViewBag.CouponCode = couponCodeStored;
            ViewBag.SelectedAddress = selectedAddress;

            return View("Checkout");
        }
        catch (Exception ex)
        {
            TempData["CartMessage"] = $"เกิดข้อผิดพลาดขณะเชื่อมต่อ Stripe: {ex.Message}";
            return RedirectToAction("Cart");
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    public IActionResult CheckoutSuccess(string session_id)
    {
        if (string.IsNullOrEmpty(session_id))
        {
            TempData["CartMessage"] = "ตรวจสอบการชำระเงินไม่สำเร็จ";
            return RedirectToAction("Cart");
        }

        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            TempData["CartMessage"] = "กรุณาเข้าสู่ระบบก่อนทำรายการ";
            return RedirectToAction("Login", "Account");
        }

        try
        {
            Stripe.StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"] ?? string.Empty;
            var service = new SessionService();
            var session = service.Get(session_id);

            if (session.PaymentStatus != "paid")
            {
                TempData["CartMessage"] = $"สถานะการชำระเงิน: {session.PaymentStatus}";
                return RedirectToAction("Cart");
            }

            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            var addressId = HttpContext.Session.GetInt32("CheckoutAddressId");
            if (addressId == null || addressId <= 0)
            {
                TempData["CartMessage"] = "ที่อยู่จัดส่งไม่ถูกต้อง";
                return RedirectToAction("Cart");
            }

            var cartItems = GetSelectedCartItems(userId.Value);
            if (!cartItems.Any())
            {
                TempData["CartMessage"] = "ไม่พบสินค้าที่เลือกสำหรับชำระเงิน";
                return RedirectToAction("Cart");
            }

            int? couponId = null;
            string couponCodeStored = HttpContext.Session.GetString("CheckoutCouponCode") ?? string.Empty;
            if (HttpContext.Session.GetInt32("CheckoutCouponId") is int savedCouponId)
            {
                couponId = savedCouponId;
            }

            var subtotal = cartItems.Sum(item => item.Price * item.Quantity);
            decimal couponDiscount = 0m;
            if (couponId.HasValue)
            {
                if (!TryGetCouponByCode(couponCodeStored, userId.Value, out _, out decimal discountValue, out string discountType, out _))
                {
                    couponId = null;
                }
                else
                {
                    couponDiscount = CalculateCouponAmount(subtotal, discountType, discountValue);
                }
            }

            var totalAmount = Math.Max(0, subtotal - couponDiscount);
            var orderId = InsertOrder(userId.Value, addressId.Value, couponId, subtotal, couponDiscount, totalAmount, "paid");
            InsertOrderDetails(orderId, cartItems);

            if (couponId.HasValue)
            {
                RecordCouponUsage(couponId.Value, userId.Value, orderId);
                IncrementCouponUsedCount(couponId.Value);
            }

            DeleteSelectedCartItems(userId.Value);
            HttpContext.Session.Remove("CheckoutAddressId");
            HttpContext.Session.Remove("CheckoutCouponId");
            HttpContext.Session.Remove("CheckoutCouponCode");

            TempData["CartMessage"] = "ชำระเงินเรียบร้อยแล้ว";
        }
        catch (Exception ex)
        {
            TempData["CartMessage"] = $"ตรวจสอบการชำระเงินไม่สำเร็จ: {ex.Message}";
        }

        return RedirectToAction("Cart");
    }

    [HttpPost]
    public IActionResult RemoveFromCart(int cartId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null) return RedirectToAction("Login", "Account");
        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();
            string sql = "DELETE FROM Cart WHERE cart_id = @cartId AND user_id = @userId";
            using var cmd = new MySqlCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@cartId", cartId);
            cmd.Parameters.AddWithValue("@userId", userId.Value);
            cmd.ExecuteNonQuery();
            TempData["CartMessage"] = "ลบรายการจากตะกร้าเรียบร้อยแล้ว";
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
        return RedirectToAction("Cart");
    }

    [HttpPost]
    public IActionResult ClearCart()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null) return RedirectToAction("Login", "Account");
        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();
            string sql = "DELETE FROM Cart WHERE user_id = @userId";
            using var cmd = new MySqlCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@userId", userId.Value);
            cmd.ExecuteNonQuery();
            TempData["CartMessage"] = "ล้างตะกร้าเรียบร้อยแล้ว";
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
        return RedirectToAction("Cart");
    }

    [HttpPost]
    public IActionResult UpdateCartSelection([FromBody] UpdateCartSelectionRequest request)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบ" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            string updateSql = "UPDATE Cart SET is_selected = @isSelected WHERE cart_id = @cartId AND user_id = @userId";
            using var cmd = new MySqlCommand(updateSql, _connection);
            cmd.Parameters.AddWithValue("@isSelected", request.IsSelected ? 1 : 0);
            cmd.Parameters.AddWithValue("@cartId", request.CartId);
            cmd.Parameters.AddWithValue("@userId", userId.Value);
            cmd.ExecuteNonQuery();

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    public IActionResult Productdetails(int id)
    {
        Product? product = null;

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // Fetch product details
            string query = @"SELECT p.product_id, p.name, p.price, p.stock_quantity,
                                   COALESCE(pi.image_url, '~/image/default.png') AS image_url,
                                   p.description,
                                   COALESCE(c.category_name, 'Uncategorized') as category_name,
                                   COALESCE(b.brand_name, '') as brand_name
                            FROM Products p 
                            LEFT JOIN Product_Images pi ON p.product_id = pi.product_id AND pi.is_main = 1
                            LEFT JOIN Categories c ON p.category_id = c.category_id 
                            LEFT JOIN Brands b ON p.brand_id = b.brand_id
                            WHERE p.product_id = @productId";
            
            using (MySqlCommand cmd = new MySqlCommand(query, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", id);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        product = new Product
                        {
                            ProductId = reader.GetInt32("product_id"),
                            Name = reader.GetString("name"),
                            Price = reader.GetDecimal("price"),
                            StockQuantity = reader.GetInt32("stock_quantity"),
                            ImageUrl = reader.GetString("image_url") ?? "~/image/default.png",
                            CategoryName = reader.GetString("category_name"),
                            BrandName = reader.GetString("brand_name"),
                            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? "" : reader.GetString("description")
                        };
                    }
                }
            }

            if (product != null)
            {
                string imageQuery = @"SELECT image_url FROM Product_Images WHERE product_id = @productId ORDER BY is_main DESC, image_id ASC";
                using (MySqlCommand imageCmd = new MySqlCommand(imageQuery, _connection))
                {
                    imageCmd.Parameters.AddWithValue("@productId", product.ProductId);
                    using (MySqlDataReader reader = imageCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            product.ImageUrls.Add(reader.IsDBNull(reader.GetOrdinal("image_url")) ? "~/image/default.png" : reader.GetString("image_url"));
                        }
                    }
                }

                if (product.ImageUrls.Count == 0 && !string.IsNullOrEmpty(product.ImageUrl))
                {
                    product.ImageUrls.Add(product.ImageUrl);
                }
            }

            // Load specifications
            if (product != null)
            {
                string specQuery = "SELECT spec_id, product_id, spec_key, spec_value FROM Product_Specifications WHERE product_id = @productId";
                using (MySqlCommand specCmd = new MySqlCommand(specQuery, _connection))
                {
                    specCmd.Parameters.AddWithValue("@productId", product.ProductId);
                    using (MySqlDataReader reader = specCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            product.Specifications.Add(new Specification
                            {
                                SpecId = reader.GetInt32("spec_id"),
                                ProductId = reader.GetInt32("product_id"),
                                SpecKey = reader.GetString("spec_key"),
                                SpecValue = reader.GetString("spec_value")
                            });
                        }
                    }
                }

                string reviewQuery = @"SELECT r.review_id, r.product_id, r.user_id, r.rating, r.comment, r.review_date,
                                               COALESCE(u.username, 'Customer') AS reviewer_name
                                        FROM Reviews r
                                        LEFT JOIN Users u ON r.user_id = u.user_id
                                        WHERE r.product_id = @productId
                                        ORDER BY r.review_date DESC";
                using (MySqlCommand reviewCmd = new MySqlCommand(reviewQuery, _connection))
                {
                    reviewCmd.Parameters.AddWithValue("@productId", product.ProductId);
                    using (MySqlDataReader reader = reviewCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            product.Reviews.Add(new Review
                            {
                                ReviewId = reader.GetInt32("review_id"),
                                ProductId = reader.GetInt32("product_id"),
                                UserId = reader.GetInt32("user_id"),
                                Rating = reader.GetInt32("rating"),
                                Comment = reader.IsDBNull(reader.GetOrdinal("comment")) ? string.Empty : reader.GetString("comment"),
                                ReviewerName = reader.GetString("reviewer_name"),
                                ReviewDate = reader.GetDateTime("review_date")
                            });
                        }
                    }
                }

                product.ReviewCount = product.Reviews.Count;
                product.AverageRating = product.ReviewCount > 0 ? product.Reviews.Average(r => (decimal)r.Rating) : 0m;
                product.Star5Count = product.Reviews.Count(r => r.Rating == 5);
                product.Star4Count = product.Reviews.Count(r => r.Rating == 4);
                product.Star3Count = product.Reviews.Count(r => r.Rating == 3);
                product.Star2Count = product.Reviews.Count(r => r.Rating == 2);
                product.Star1Count = product.Reviews.Count(r => r.Rating == 1);
            }

            // Load wishlist state for the current user
            var currentUserId = HttpContext.Session.GetInt32("UserId");
            ViewBag.IsAuthenticated = currentUserId != null;
            if (currentUserId != null && product != null)
            {
                string wishlistQuery = "SELECT COUNT(*) FROM Wishlist WHERE user_id = @userId AND product_id = @productId";
                using (var wishlistCmd = new MySqlCommand(wishlistQuery, _connection))
                {
                    wishlistCmd.Parameters.AddWithValue("@userId", currentUserId.Value);
                    wishlistCmd.Parameters.AddWithValue("@productId", product.ProductId);
                    var count = Convert.ToInt32(wishlistCmd.ExecuteScalar());
                    ViewBag.IsFavorite = count > 0;
                }

                product.UserReview = product.Reviews.FirstOrDefault(r => r.UserId == currentUserId.Value);
            }
            else
            {
                ViewBag.IsFavorite = false;
            }
        }
        catch (Exception ex)
        {
            ViewBag.ErrorMessage = $"เกิดข้อผิดพลาด: {ex.Message}";
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }

        return View(product);
    }

    [HttpPost]
    public IActionResult AddReview(int productId, int rating, string comment)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            TempData["ReviewMessage"] = "Please log in to submit a review.";
            return RedirectToAction("Login", "Account");
        }

        if (rating < 1 || rating > 5 || string.IsNullOrWhiteSpace(comment))
        {
            TempData["ReviewMessage"] = "Please select a rating and write a review before submitting.";
            return RedirectToAction("Productdetails", new { id = productId });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            const string checkExistingQuery = "SELECT review_id FROM Reviews WHERE product_id = @productId AND user_id = @userId LIMIT 1";
            using (var checkCmd = new MySqlCommand(checkExistingQuery, _connection))
            {
                checkCmd.Parameters.AddWithValue("@productId", productId);
                checkCmd.Parameters.AddWithValue("@userId", userId.Value);
                var existingReview = checkCmd.ExecuteScalar();
                if (existingReview != null)
                {
                    TempData["ReviewMessage"] = "You have already reviewed this product. Delete your existing review to submit a new one.";
                    return RedirectToAction("Productdetails", new { id = productId });
                }
            }

            const string insertQuery = "INSERT INTO Reviews (product_id, user_id, rating, comment, review_date) VALUES (@productId, @userId, @rating, @comment, NOW())";
            using (var insertCmd = new MySqlCommand(insertQuery, _connection))
            {
                insertCmd.Parameters.AddWithValue("@productId", productId);
                insertCmd.Parameters.AddWithValue("@userId", userId.Value);
                insertCmd.Parameters.AddWithValue("@rating", rating);
                insertCmd.Parameters.AddWithValue("@comment", comment.Trim());
                insertCmd.ExecuteNonQuery();
            }

            TempData["ReviewMessage"] = "Review submitted successfully.";
        }
        catch (Exception ex)
        {
            TempData["ReviewMessage"] = $"Unable to save review: {ex.Message}";
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }

        return RedirectToAction("Productdetails", new { id = productId });
    }

    [HttpPost]
    public IActionResult DeleteReview(int productId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            TempData["ReviewMessage"] = "Please log in to delete your review.";
            return RedirectToAction("Login", "Account");
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            const string deleteQuery = "DELETE FROM Reviews WHERE product_id = @productId AND user_id = @userId LIMIT 1";
            using (var deleteCmd = new MySqlCommand(deleteQuery, _connection))
            {
                deleteCmd.Parameters.AddWithValue("@productId", productId);
                deleteCmd.Parameters.AddWithValue("@userId", userId.Value);
                deleteCmd.ExecuteNonQuery();
            }

            TempData["ReviewMessage"] = "Your review has been deleted.";
        }
        catch (Exception ex)
        {
            TempData["ReviewMessage"] = $"Unable to delete review: {ex.Message}";
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }

        return RedirectToAction("Productdetails", new { id = productId });
    }

    [HttpPost]
    public IActionResult AddToWishlist(int productId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบก่อนกดรายการโปรด" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            const string checkQuery = "SELECT wishlist_id FROM Wishlist WHERE user_id = @userId AND product_id = @productId LIMIT 1";
            using (var checkCmd = new MySqlCommand(checkQuery, _connection))
            {
                checkCmd.Parameters.AddWithValue("@userId", userId.Value);
                checkCmd.Parameters.AddWithValue("@productId", productId);
                var existing = checkCmd.ExecuteScalar();
                if (existing == null)
                {
                    const string insertQuery = "INSERT INTO Wishlist (user_id, product_id, added_at) VALUES (@userId, @productId, NOW())";
                    using (var insertCmd = new MySqlCommand(insertQuery, _connection))
                    {
                        insertCmd.Parameters.AddWithValue("@userId", userId.Value);
                        insertCmd.Parameters.AddWithValue("@productId", productId);
                        insertCmd.ExecuteNonQuery();
                    }
                }
            }

            return Json(new { success = true, message = "เพิ่มรายการโปรดเรียบร้อยแล้ว" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาด: {ex.Message}" });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    [HttpPost]
    public IActionResult RemoveFromWishlist(int productId)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบก่อนกดรายการโปรด" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            const string deleteQuery = "DELETE FROM Wishlist WHERE user_id = @userId AND product_id = @productId";
            using (var deleteCmd = new MySqlCommand(deleteQuery, _connection))
            {
                deleteCmd.Parameters.AddWithValue("@userId", userId.Value);
                deleteCmd.Parameters.AddWithValue("@productId", productId);
                deleteCmd.ExecuteNonQuery();
            }

            return Json(new { success = true, message = "ลบออกจากรายการโปรดแล้ว" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาด: {ex.Message}" });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    [HttpPost]
    public IActionResult UpdateQuantity(int cartId, int newQuantity)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null) return RedirectToAction("Login", "Account");

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            // ตรวจสอบ stock และ limit quantity
            const string checkStockQuery = @"SELECT p.stock_quantity FROM Cart c
                                            JOIN Products p ON c.product_id = p.product_id
                                            WHERE c.cart_id = @cartId AND c.user_id = @userId";
            int maxStock = 0;

            using (var cmd = new MySqlCommand(checkStockQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@cartId", cartId);
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                var result = cmd.ExecuteScalar();
                if (result == null)
                {
                    TempData["CartMessage"] = "ไม่พบสินค้าในตะกร้า";
                    return RedirectToAction("Cart");
                }
                maxStock = Convert.ToInt32(result);
            }

            // Validate quantity
            newQuantity = Math.Max(1, Math.Min(newQuantity, maxStock));

            // Update cart
            const string updateQuery = "UPDATE Cart SET quantity = @quantity WHERE cart_id = @cartId AND user_id = @userId";
            using (var cmd = new MySqlCommand(updateQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@quantity", newQuantity);
                cmd.Parameters.AddWithValue("@cartId", cartId);
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                cmd.ExecuteNonQuery();
            }

            TempData["CartMessage"] = "แก้ไขจำนวนสินค้าเรียบร้อยแล้ว";
        }
        catch (Exception ex)
        {
            TempData["CartMessage"] = $"เกิดข้อผิดพลาด: {ex.Message}";
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }

        return RedirectToAction("Cart");
    }

    [HttpPost]
    public IActionResult CreatePaymentIntent([FromBody] CreatePaymentIntentRequest request)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบก่อน" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            var addressId = HttpContext.Session.GetInt32("CheckoutAddressId");
            if (addressId == null || addressId <= 0)
            {
                return Json(new { success = false, message = "ที่อยู่จัดส่งไม่ถูกต้อง" });
            }

            var cartItems = GetSelectedCartItems(userId.Value);
            if (!cartItems.Any())
            {
                return Json(new { success = false, message = "ไม่พบสินค้าที่เลือก" });
            }

            int? couponId = null;
            string couponCodeStored = HttpContext.Session.GetString("CheckoutCouponCode") ?? string.Empty;
            if (HttpContext.Session.GetInt32("CheckoutCouponId") is int savedCouponId)
            {
                couponId = savedCouponId;
            }

            var subtotal = cartItems.Sum(item => item.Price * item.Quantity);
            decimal couponDiscount = 0m;
            var discountPromotionId = HttpContext.Session.GetInt32("CheckoutDiscountPromotionId");
            var freeShippingPromotionId = HttpContext.Session.GetInt32("CheckoutFreeShippingPromotionId");
            var promotionResult = GetSelectedCheckoutPromotions(cartItems, subtotal, discountPromotionId, freeShippingPromotionId);
            var promotionDiscount = promotionResult.promotionDiscount;
            var shippingFee = promotionResult.shippingFee;
            var subtotalAfterPromotion = Math.Max(0, subtotal - promotionDiscount);
            if (couponId.HasValue)
            {
                if (!TryGetCouponByCode(couponCodeStored, userId.Value, out _, out decimal discountValue, out string discountType, out _))
                {
                    couponId = null;
                }
                else
                {
                    couponDiscount = CalculateCouponAmount(subtotalAfterPromotion, discountType, discountValue);
                }
            }
            var totalAmount = Math.Max(0, subtotalAfterPromotion - couponDiscount + shippingFee);

            Stripe.StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"] ?? string.Empty;

            var options = new Stripe.PaymentIntentCreateOptions
            {
                Amount = (long)(totalAmount * 100m),
                Currency = "thb",
                PaymentMethodTypes = new List<string> { "card" },
                Metadata = new Dictionary<string, string>
                {
                    { "userId", userId.Value.ToString() },
                    { "addressId", addressId.Value.ToString() },
                    { "couponId", couponId?.ToString() ?? string.Empty },
                    { "discountPromotionId", discountPromotionId?.ToString() ?? string.Empty },
                    { "freeShippingPromotionId", freeShippingPromotionId?.ToString() ?? string.Empty }
                }
            };

            var service = new Stripe.PaymentIntentService();
            var paymentIntent = service.Create(options);

            return Json(new
            {
                success = true,
                clientSecret = paymentIntent.ClientSecret,
                totalAmount = totalAmount,
                subtotal = subtotal,
                discount = couponDiscount,
                promotionDiscount = promotionDiscount,
                shippingFee = shippingFee
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาด: {ex.Message}" });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    [HttpPost]
    public IActionResult ConfirmPayment([FromBody] ConfirmPaymentRequest request)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบก่อน" });
        }

        try
        {
            Stripe.StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"] ?? string.Empty;
            var service = new Stripe.PaymentIntentService();
            var paymentIntent = service.Get(request.PaymentIntentId);

            if (paymentIntent.Status != "succeeded")
            {
                return Json(new { success = false, message = "การชำระเงินไม่สำเร็จ" });
            }

            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            var addressId = HttpContext.Session.GetInt32("CheckoutAddressId");
            if (addressId == null || addressId <= 0)
            {
                return Json(new { success = false, message = "ที่อยู่จัดส่งไม่ถูกต้อง" });
            }

            var cartItems = GetSelectedCartItems(userId.Value);
            if (!cartItems.Any())
            {
                return Json(new { success = false, message = "ไม่พบสินค้าที่เลือก" });
            }

            int? couponId = null;
            string couponCodeStored = HttpContext.Session.GetString("CheckoutCouponCode") ?? string.Empty;
            if (HttpContext.Session.GetInt32("CheckoutCouponId") is int savedCouponId)
            {
                couponId = savedCouponId;
            }

            var subtotal = cartItems.Sum(item => item.Price * item.Quantity);
            decimal couponDiscount = 0m;
            var discountPromotionId = HttpContext.Session.GetInt32("CheckoutDiscountPromotionId");
            var freeShippingPromotionId = HttpContext.Session.GetInt32("CheckoutFreeShippingPromotionId");
            var promotionResult = GetSelectedCheckoutPromotions(cartItems, subtotal, discountPromotionId, freeShippingPromotionId);
            var promotionDiscount = promotionResult.promotionDiscount;
            var shippingFee = promotionResult.shippingFee;
            var subtotalAfterPromotion = Math.Max(0, subtotal - promotionDiscount);
            if (couponId.HasValue)
            {
                if (!TryGetCouponByCode(couponCodeStored, userId.Value, out _, out decimal discountValue, out string discountType, out _))
                {
                    couponId = null;
                }
                else
                {
                    couponDiscount = CalculateCouponAmount(subtotalAfterPromotion, discountType, discountValue);
                }
            }
            var totalAmount = Math.Max(0, subtotalAfterPromotion - couponDiscount + shippingFee);
            var totalDiscount = couponDiscount + promotionDiscount;
            var orderId = InsertOrder(userId.Value, addressId.Value, couponId, subtotal, totalDiscount, totalAmount, "paid");
            InsertOrderDetails(orderId, cartItems);

            if (couponId.HasValue)
            {
                RecordCouponUsage(couponId.Value, userId.Value, orderId);
                IncrementCouponUsedCount(couponId.Value);
            }

            DeleteSelectedCartItems(userId.Value);
            HttpContext.Session.Remove("CheckoutAddressId");
            HttpContext.Session.Remove("CheckoutCouponId");
            HttpContext.Session.Remove("CheckoutCouponCode");
            HttpContext.Session.Remove("CheckoutDiscountPromotionId");
            HttpContext.Session.Remove("CheckoutFreeShippingPromotionId");

            return Json(new { success = true, orderId = orderId, message = "ชำระเงินเรียบร้อยแล้ว" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาด: {ex.Message}" });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    public IActionResult ThankYou()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
