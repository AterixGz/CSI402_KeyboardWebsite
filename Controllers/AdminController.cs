using System.Net;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using KeyboardWebsiteProject.Models;
using KeyboardWebsiteProject.Views.Admin;

public class AdminController : Controller
{
    private readonly MySqlConnection _connection;
    private readonly Cloudinary _cloudinary;

    public AdminController(MySqlConnection connection, Cloudinary cloudinary)
    {
        _connection = connection;
        _cloudinary = cloudinary;
    }

    private static string NormalizeStatus(string status)
    {
        return status?.Trim().ToLower() switch
        {
            "delivered" => "Delivered",
            "shipping" => "Shipping",
            "pending" => "Pending",
            "cancelled" => "Cancelled",
            "processing" => "Processing",
            _ => string.IsNullOrWhiteSpace(status) ? "Pending" : status
        };
    }

    private static string GetBadgeClass(string status)
    {
        return status?.Trim().ToLower() switch
        {
            "delivered" => "badge-delivered",
            "shipping" => "badge-shipped",
            "pending" => "badge-pending",
            "cancelled" => "badge-pending",
            "processing" => "badge-processing",
            _ => "badge-pending"
        };
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "--";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0].Length > 1 ? parts[0].Substring(0, 2).ToUpper() : parts[0].ToUpper();
        return string.Concat(parts[0][0], parts[^1][0]).ToUpper();
    }

    // ตรวจสอบว่า user เป็น admin หรือไม่
    private bool IsAdmin()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        var userRole = HttpContext.Session.GetInt32("UserRole");
        return userId != null && userRole != 4; // ต้อง login และ role_id = 4 คือ customer ห้ามเข้า, role อื่นสามารถเข้าได้
    }

    // Redirect ถ้า user ไม่ใช่ admin
    private IActionResult? CheckAdminAccess()
    {
        if (!IsAdmin())
        {
            TempData["ErrorMessage"] = "คุณไม่มีสิทธิ์เข้าถึงหน้านี้"; 
            return RedirectToAction("Index", "Home");
        }
        return null;
    }

    public IActionResult Dashboard()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        var model = new DashboardModel();

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // Stat cards
            const string statsSql = @"
                SELECT
                    (SELECT COUNT(*) FROM Users WHERE role_id = 4) AS customer_count,
                    (SELECT COUNT(*) FROM Orders) AS order_count,
                    (SELECT COUNT(*) FROM Products) AS product_count,
                    (SELECT COALESCE(SUM(total_amount), 0) FROM Orders) AS revenue";

            using (var statsCmd = new MySqlCommand(statsSql, _connection))
            using (var statsReader = statsCmd.ExecuteReader())
            {
                if (statsReader.Read())
                {
                    var totalCustomers = statsReader.GetInt32("customer_count");
                    var totalOrders = statsReader.GetInt32("order_count");
                    var totalProducts = statsReader.GetInt32("product_count");
                    var revenue = statsReader.GetDecimal("revenue");

                    model.Stats = new List<StatCard>
                    {
                        new StatCard
                        {
                            Label = "Total Customers",
                            Value = totalCustomers.ToString("N0"),
                            Change = "+1.8% vs last month",
                            IsPositive = true,
                            IconClass = "icon-green"
                        },
                        new StatCard
                        {
                            Label = "Total Orders",
                            Value = totalOrders.ToString("N0"),
                            Change = "+8.2% vs last month",
                            IsPositive = true,
                            IconClass = "icon-purple"
                        },
                        new StatCard
                        {
                            Label = "Products",
                            Value = totalProducts.ToString("N0"),
                            Change = "+3 vs last month",
                            IsPositive = true,
                            IconClass = "icon-blue"
                        },
                        new StatCard
                        {
                            Label = "Revenue",
                            Value = $"฿{revenue:N0}",
                            Change = "-2.4% vs last month",
                            IsPositive = revenue >= 0,
                            IconClass = "icon-orange"
                        }
                    };
                }
            }

            // Recent orders
            const string ordersSql = @"
                SELECT
                    o.order_id,
                    o.order_date,
                    o.total_amount,
                    o.status,
                    u.username,
                    u.email,
                    COALESCE(up.first_name, '') AS first_name,
                    COALESCE(up.last_name, '') AS last_name
                FROM Orders o
                LEFT JOIN Users u ON u.user_id = o.user_id
                LEFT JOIN User_Profiles up ON up.user_id = u.user_id
                ORDER BY o.order_date DESC
                LIMIT 5";

            var recentOrders = new List<RecentOrder>();

            using (var ordersCmd = new MySqlCommand(ordersSql, _connection))
            using (var ordersReader = ordersCmd.ExecuteReader())
            {
                while (ordersReader.Read())
                {
                    var firstName = ordersReader.GetString("first_name");
                    var lastName = ordersReader.GetString("last_name");
                    var username = ordersReader.GetString("username");
                    var buyerName = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)
                        ? username
                        : $"{firstName} {lastName}".Trim();

                    recentOrders.Add(new RecentOrder
                    {
                        OrderId = ordersReader.GetInt32("order_id"),
                        Name = string.IsNullOrWhiteSpace(buyerName) ? username : buyerName,
                        Initials = GetInitials(buyerName),
                        Amount = $"฿{ordersReader.GetDecimal("total_amount"):N0}",
                        Status = NormalizeStatus(ordersReader.GetString("status")),
                        BadgeClass = GetBadgeClass(ordersReader.GetString("status")),
                        Product = "Loading..."
                    });
                }
            }

            const string itemSql = @"
                SELECT
                    p.name AS product_name,
                    COALESCE(pi.image_url, '~/image/default.png') AS image_url
                FROM OrderDetails od
                JOIN Products p ON p.product_id = od.product_id
                LEFT JOIN Product_Images pi ON pi.product_id = p.product_id AND pi.is_main = 1
                WHERE od.order_id = @orderId
                LIMIT 1";

            foreach (var order in recentOrders)
            {
                using (var itemCmd = new MySqlCommand(itemSql, _connection))
                {
                    itemCmd.Parameters.AddWithValue("@orderId", order.OrderId);
                    using (var itemReader = itemCmd.ExecuteReader())
                    {
                        if (itemReader.Read())
                        {
                            order.Product = itemReader.GetString("product_name");
                            order.ImageUrl = itemReader.GetString("image_url");
                        }
                        else
                        {
                            order.Product = "No products found";
                            order.ImageUrl = "~/image/default.png";
                        }
                    }
                }
            }

            model.RecentOrders = recentOrders;

            // Top customers
            const string topCustomersSql = @"
                SELECT
                    u.user_id,
                    u.username,
                    u.email,
                    COALESCE(up.first_name, '') AS first_name,
                    COALESCE(up.last_name, '') AS last_name,
                    COUNT(o.order_id) AS order_count,
                    COALESCE(SUM(o.total_amount), 0) AS total_amount
                FROM Users u
                LEFT JOIN User_Profiles up ON up.user_id = u.user_id
                LEFT JOIN Orders o ON o.user_id = u.user_id
                WHERE u.role_id = 4
                GROUP BY u.user_id, u.username, u.email, up.first_name, up.last_name
                HAVING order_count > 0
                ORDER BY total_amount DESC
                LIMIT 5";

            var topCustomers = new List<TopCustomer>();
            using (var topCmd = new MySqlCommand(topCustomersSql, _connection))
            using (var topReader = topCmd.ExecuteReader())
            {
                var rank = 1;
                while (topReader.Read())
                {
                    var firstName = topReader.GetString("first_name");
                    var lastName = topReader.GetString("last_name");
                    var username = topReader.GetString("username");
                    var customerName = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)
                        ? username
                        : $"{firstName} {lastName}".Trim();

                    topCustomers.Add(new TopCustomer
                    {
                        Rank = $"#{rank}",
                        Name = string.IsNullOrWhiteSpace(customerName) ? username : customerName,
                        Email = topReader.GetString("email"),
                        Amount = $"฿{topReader.GetDecimal("total_amount"):N0}",
                        Orders = $"{topReader.GetInt32("order_count")} orders"
                    });

                    rank++;
                }
            }

            model.TopCustomers = topCustomers;
            model.LastUpdated = DateTime.Now.ToString("dd MMM yyyy HH:mm");
        }
        catch (Exception ex)
        {
            ViewBag.ErrorMessage = $"เกิดข้อผิดพลาดในการโหลดข้อมูล Dashboard: {ex.Message}";
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }

        return View(model);
    }

    public IActionResult Settings()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        return View();
    }

    public IActionResult Customers()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        return View();
    }

    public IActionResult Orders()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        return View();
    }

    public IActionResult Products()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        var products = new List<Product>();
        var categories = new List<string>();

        try
        {
            // เปิดการเชื่อมต่อกับฐานข้อมูล
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // ดึง categories สำหรับ dropdown filter
            string catQuery = "SELECT DISTINCT category_name FROM Categories ORDER BY category_name";
            using (MySqlCommand catCmd = new MySqlCommand(catQuery, _connection))
            {
                using (MySqlDataReader reader = catCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        categories.Add(reader.GetString("category_name"));
                    }
                }
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

            // โหลด specifications สำหรับแต่ละ product
            string specQuery = "SELECT spec_id, product_id, spec_key, spec_value FROM Product_Specifications WHERE product_id = @productId";
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

        // ส่งข้อมูลผ่าน ViewBag ไปยัง Products.cshtml
        ViewBag.AllProducts = products;
        ViewBag.Categories = categories;
        ViewBag.TotalProducts = products.Count;
        ViewBag.InStockCount = products.Count(p => p.StockQuantity > 0);
        ViewBag.OutOfStockCount = products.Count(p => p.StockQuantity == 0);
        ViewBag.LowStockCount = products.Count(p => p.StockQuantity > 0 && p.StockQuantity <= 10);

        return View(products);
    }

    public IActionResult Categories()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        var model = new CategoriesModel();

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            string query = @"SELECT c.category_id, c.category_name, COUNT(p.product_id) AS product_count
FROM Categories c
LEFT JOIN Products p ON p.category_id = c.category_id
GROUP BY c.category_id, c.category_name
ORDER BY c.category_name";
            using (MySqlCommand cmd = new MySqlCommand(query, _connection))
            {
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        model.Categories.Add(new CategoryItem
                        {
                            Name = reader.GetString("category_name"),
                            Description = string.Empty,
                            ProductCount = reader.GetInt32("product_count"),
                            Status = "Active",
                            IsFeatured = false,
                            IsExpanded = false,
                            SubcategoryCount = null
                        });
                    }
                }
            }

            int totalProducts = model.Categories.Sum(c => c.ProductCount);
            model.Stats.Add(new CategoryStat
            {
                Label = "Total Categories",
                Value = model.Categories.Count.ToString(),
                IconClass = "si-blue",
                IconSvg = "<svg width=\"20\" height=\"20\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" viewBox=\"0 0 24 24\"><path d=\"M12 5v14M5 12h14\"/></svg>"
            });
            model.Stats.Add(new CategoryStat
            {
                Label = "Active Categories",
                Value = model.Categories.Count.ToString(),
                IconClass = "si-green",
                IconSvg = "<svg width=\"20\" height=\"20\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" viewBox=\"0 0 24 24\"><path d=\"M5 13l4 4L19 7\"/></svg>"
            });
            model.Stats.Add(new CategoryStat
            {
                Label = "Products Linked",
                Value = totalProducts.ToString(),
                IconClass = "si-orange",
                IconSvg = "<svg width=\"20\" height=\"20\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" viewBox=\"0 0 24 24\"><path d=\"M4 7h16M4 12h16M4 17h16\"/></svg>"
            });
            model.Stats.Add(new CategoryStat
            {
                Label = "Featured",
                Value = "0",
                IconClass = "si-purple",
                IconSvg = "<svg width=\"20\" height=\"20\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" viewBox=\"0 0 24 24\"><path d=\"M12 2l3.09 6.26L22 9.27l-5 4.87L18.18 22 12 18.56 5.82 22 7 14.14l-5-4.87 6.91-1.01L12 2z\"/></svg>"
            });
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

        return View(model);
    }

    public IActionResult Roles()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> UploadProductImage(IFormFile image)
    {
        if (image == null || image.Length == 0)
        {
            return BadRequest(new { success = false, message = "No image file provided." });
        }

        try
        {
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(image.FileName, image.OpenReadStream()),
                Folder = "keyboard_products",
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);
            if (uploadResult.StatusCode != HttpStatusCode.OK && uploadResult.StatusCode != HttpStatusCode.Created)
            {
                return BadRequest(new { success = false, message = uploadResult.Error?.Message ?? "Cloudinary upload failed." });
            }

            return Ok(new { success = true, imageUrl = uploadResult.SecureUrl?.ToString() });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    public IActionResult Promotions()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        return View();
    }

    private string NormalizeImageUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return "/image/default.png";
        }

        if (imageUrl.StartsWith("~/"))
        {
            return Url.Content(imageUrl);
        }

        return imageUrl;
    }

    [HttpPost]
    public IActionResult CreateProduct([FromBody] CreateProductRequest request)
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;
        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // 1. ดึง category_id จาก category_name
            int categoryId = 0;
            string catQuery = "SELECT category_id FROM Categories WHERE category_name = @catName";
            using (MySqlCommand cmd = new MySqlCommand(catQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@catName", request.CategoryName ?? "");
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    categoryId = Convert.ToInt32(result);
                }
            }

            // 2. ดึง brand_id หรือสร้าง brand ใหม่
            int brandId = 0;
            string checkBrandQuery = "SELECT brand_id FROM Brands WHERE brand_name = @brandName";
            using (MySqlCommand cmd = new MySqlCommand(checkBrandQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@brandName", request.BrandName ?? "");
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    brandId = Convert.ToInt32(result);
                }
                else
                {
                    // สร้าง brand ใหม่
                    string insertBrandQuery = "INSERT INTO Brands (brand_name) VALUES (@brandName)";
                    using (MySqlCommand insertCmd = new MySqlCommand(insertBrandQuery, _connection))
                    {
                        insertCmd.Parameters.AddWithValue("@brandName", request.BrandName ?? "");
                        insertCmd.ExecuteNonQuery();
                        brandId = (int)insertCmd.LastInsertedId;
                    }
                }
            }

            // 3. เพิ่มสินค้า
            string insertProductQuery = @"INSERT INTO Products (category_id, brand_id, name, price, stock_quantity, description) 
                                         VALUES (@categoryId, @brandId, @name, @price, @stock, @description)";
            int productId = 0;
            using (MySqlCommand cmd = new MySqlCommand(insertProductQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@categoryId", categoryId);
                cmd.Parameters.AddWithValue("@brandId", brandId);
                cmd.Parameters.AddWithValue("@name", request.Name ?? "");
                cmd.Parameters.AddWithValue("@price", request.Price);
                cmd.Parameters.AddWithValue("@stock", request.StockQuantity);
                cmd.Parameters.AddWithValue("@description", request.Description ?? "");
                cmd.ExecuteNonQuery();
                productId = (int)cmd.LastInsertedId;
            }

            // 3.1 เพิ่มรูปภาพหลักลง Product_Images
            string imageUrl = NormalizeImageUrl(request.ImageUrl);
            string insertImageQuery = "INSERT INTO Product_Images (product_id, image_url, is_main) VALUES (@productId, @imageUrl, 1)";
            using (MySqlCommand cmd = new MySqlCommand(insertImageQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", productId);
                cmd.Parameters.AddWithValue("@imageUrl", imageUrl);
                cmd.ExecuteNonQuery();
            }

            // 4. เพิ่ม specifications ถ้ามี
            if (request.Specifications != null && request.Specifications.Count > 0)
            {
                string insertSpecQuery = "INSERT INTO Product_Specifications (product_id, spec_key, spec_value) VALUES (@productId, @key, @value)";
                foreach (var spec in request.Specifications)
                {
                    using (MySqlCommand cmd = new MySqlCommand(insertSpecQuery, _connection))
                    {
                        cmd.Parameters.AddWithValue("@productId", productId);
                        cmd.Parameters.AddWithValue("@key", spec.SpecKey ?? "");
                        cmd.Parameters.AddWithValue("@value", spec.SpecValue ?? "");
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            return Ok(new { success = true, message = "Product added successfully", productId = productId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }

    [HttpPost]
    public IActionResult UpdateProduct([FromBody] UpdateProductRequest request)
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;
        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // 1. ดึง category_id จาก category_name
            int categoryId = 0;
            string catQuery = "SELECT category_id FROM Categories WHERE category_name = @catName";
            using (MySqlCommand cmd = new MySqlCommand(catQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@catName", request.CategoryName ?? "");
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    categoryId = Convert.ToInt32(result);
                }
            }

            // 2. ดึง brand_id หรือสร้าง brand ใหม่
            int brandId = 0;
            string checkBrandQuery = "SELECT brand_id FROM Brands WHERE brand_name = @brandName";
            using (MySqlCommand cmd = new MySqlCommand(checkBrandQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@brandName", request.BrandName ?? "");
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    brandId = Convert.ToInt32(result);
                }
                else
                {
                    // สร้าง brand ใหม่
                    string insertBrandQuery = "INSERT INTO Brands (brand_name) VALUES (@brandName)";
                    using (MySqlCommand insertCmd = new MySqlCommand(insertBrandQuery, _connection))
                    {
                        insertCmd.Parameters.AddWithValue("@brandName", request.BrandName ?? "");
                        insertCmd.ExecuteNonQuery();
                        brandId = (int)insertCmd.LastInsertedId;
                    }
                }
            }

            // 3. อัปเดตสินค้า
            string updateProductQuery = @"UPDATE Products 
                                         SET category_id = @categoryId, brand_id = @brandId, name = @name, 
                                             price = @price, stock_quantity = @stock, 
                                             description = @description 
                                         WHERE product_id = @productId";
            using (MySqlCommand cmd = new MySqlCommand(updateProductQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@categoryId", categoryId);
                cmd.Parameters.AddWithValue("@brandId", brandId);
                cmd.Parameters.AddWithValue("@name", request.Name ?? "");
                cmd.Parameters.AddWithValue("@price", request.Price);
                cmd.Parameters.AddWithValue("@stock", request.StockQuantity);
                cmd.Parameters.AddWithValue("@description", request.Description ?? "");
                cmd.Parameters.AddWithValue("@productId", request.ProductId);
                cmd.ExecuteNonQuery();
            }

            // 3.1 อัปเดตรูปภาพหลักใน Product_Images ถ้ามี imageUrl ใหม่
            if (!string.IsNullOrWhiteSpace(request.ImageUrl))
            {
                string imageUrl = NormalizeImageUrl(request.ImageUrl);
                string updateImageQuery = @"UPDATE Product_Images SET image_url = @imageUrl WHERE product_id = @productId AND is_main = 1";
                using (MySqlCommand cmd = new MySqlCommand(updateImageQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@productId", request.ProductId);
                    cmd.Parameters.AddWithValue("@imageUrl", imageUrl);
                    int affected = cmd.ExecuteNonQuery();

                    if (affected == 0)
                    {
                        string insertImageQuery = "INSERT INTO Product_Images (product_id, image_url, is_main) VALUES (@productId, @imageUrl, 1)";
                        using (var insertCmd = new MySqlCommand(insertImageQuery, _connection))
                        {
                            insertCmd.Parameters.AddWithValue("@productId", request.ProductId);
                            insertCmd.Parameters.AddWithValue("@imageUrl", imageUrl);
                            insertCmd.ExecuteNonQuery();
                        }
                    }
                }
            }

            // 4. ลบ specifications เก่า
            string deleteSpecQuery = "DELETE FROM Product_Specifications WHERE product_id = @productId";
            using (MySqlCommand cmd = new MySqlCommand(deleteSpecQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", request.ProductId);
                cmd.ExecuteNonQuery();
            }

            // 5. เพิ่ม specifications ใหม่
            if (request.Specifications != null && request.Specifications.Count > 0)
            {
                string insertSpecQuery = "INSERT INTO Product_Specifications (product_id, spec_key, spec_value) VALUES (@productId, @key, @value)";
                foreach (var spec in request.Specifications)
                {
                    using (MySqlCommand cmd = new MySqlCommand(insertSpecQuery, _connection))
                    {
                        cmd.Parameters.AddWithValue("@productId", request.ProductId);
                        cmd.Parameters.AddWithValue("@key", spec.SpecKey ?? "");
                        cmd.Parameters.AddWithValue("@value", spec.SpecValue ?? "");
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            return Ok(new { success = true, message = "Product updated successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }

    [HttpPost]
    public IActionResult DeleteProduct(int productId)
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;
        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // 1. ลบ specifications
            string deleteSpecQuery = "DELETE FROM Product_Specifications WHERE product_id = @productId";
            using (MySqlCommand cmd = new MySqlCommand(deleteSpecQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", productId);
                cmd.ExecuteNonQuery();
            }

            // 2. ลบ product images
            string deleteImagesQuery = "DELETE FROM Product_Images WHERE product_id = @productId";
            using (MySqlCommand cmd = new MySqlCommand(deleteImagesQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", productId);
                cmd.ExecuteNonQuery();
            }

            // 3. ลบสินค้า
            string deleteProductQuery = "DELETE FROM Products WHERE product_id = @productId";
            using (MySqlCommand cmd = new MySqlCommand(deleteProductQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", productId);
                cmd.ExecuteNonQuery();
            }

            return Ok(new { success = true, message = "Product deleted successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }

    [HttpGet]
    public IActionResult GetProductImages(int productId)
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;
        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            var images = new List<dynamic>();
            string query = "SELECT image_id, product_id, image_url, is_main FROM Product_Images WHERE product_id = @productId ORDER BY is_main DESC, image_id ASC";
            
            using (MySqlCommand cmd = new MySqlCommand(query, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", productId);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        images.Add(new
                        {
                            imageId = reader.GetInt32("image_id"),
                            productId = reader.GetInt32("product_id"),
                            imageUrl = reader.GetString("image_url"),
                            isMain = reader.GetInt32("is_main") == 1
                        });
                    }
                }
            }

            return Ok(new { success = true, images = images });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }

    [HttpPost]
    public IActionResult DeleteProductImage(int imageId, int productId)
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;
        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // ลบรูปภาพ
            string deleteQuery = "DELETE FROM Product_Images WHERE image_id = @imageId AND product_id = @productId";
            using (MySqlCommand cmd = new MySqlCommand(deleteQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@imageId", imageId);
                cmd.Parameters.AddWithValue("@productId", productId);
                cmd.ExecuteNonQuery();
            }

            return Ok(new { success = true, message = "Image deleted successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }

    [HttpPost]
    public IActionResult SetMainProductImage(int imageId, int productId)
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;
        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // ยกเลิก main status ของรูปอื่นๆ
            string updateOthersQuery = "UPDATE Product_Images SET is_main = 0 WHERE product_id = @productId";
            using (MySqlCommand cmd = new MySqlCommand(updateOthersQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", productId);
                cmd.ExecuteNonQuery();
            }

            // ตั้งรูปใหม่เป็น main
            string updateMainQuery = "UPDATE Product_Images SET is_main = 1 WHERE image_id = @imageId AND product_id = @productId";
            using (MySqlCommand cmd = new MySqlCommand(updateMainQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@imageId", imageId);
                cmd.Parameters.AddWithValue("@productId", productId);
                cmd.ExecuteNonQuery();
            }

            return Ok(new { success = true, message = "Main image set successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddProductImage(int productId, IFormFile image)
    {
        if (image == null || image.Length == 0)
        {
            return BadRequest(new { success = false, message = "No image file provided." });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // อัพโหลดไป Cloudinary
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(image.FileName, image.OpenReadStream()),
                Folder = "keyboard_products",
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);
            if (uploadResult.StatusCode != HttpStatusCode.OK && uploadResult.StatusCode != HttpStatusCode.Created)
            {
                return BadRequest(new { success = false, message = uploadResult.Error?.Message ?? "Cloudinary upload failed." });
            }

            string imageUrl = uploadResult.SecureUrl?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return BadRequest(new { success = false, message = "Cloudinary upload did not return a valid image URL." });
            }

            // เพิ่มรูปลงตาราง Product_Images
            string insertQuery = "INSERT INTO Product_Images (product_id, image_url, is_main) VALUES (@productId, @imageUrl, 0)";
            using (MySqlCommand cmd = new MySqlCommand(insertQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", productId);
                cmd.Parameters.AddWithValue("@imageUrl", imageUrl);
                cmd.ExecuteNonQuery();
            }

            return Ok(new { success = true, message = "Image added successfully", imageUrl = imageUrl });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }
}