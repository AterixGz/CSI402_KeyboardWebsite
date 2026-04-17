using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using KeyboardWebsiteProject.Models;

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

    private static string GetPercentChangeText(decimal current, decimal previous)
    {
        if (previous == 0)
        {
            return current == 0 ? "0% vs last month" : "+100% vs last month";
        }

        var change = Math.Round((current - previous) / previous * 100m, 1);
        var sign = change >= 0 ? "+" : string.Empty;
        return $"{sign}{change:0.#}% vs last month";
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
            TempData["ErrorMessage"] = "You do not have permission to access this page.";
            return RedirectToAction("Index", "Home");
        }
        return null;
    }

    public IActionResult Dashboard()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        var model = new AdminDashboardModel();

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // Stat cards
            var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            const string statsSql = @"
                SELECT
                    (SELECT COUNT(*) FROM Users WHERE role_id = 4) AS customer_count,
                    (SELECT COUNT(*) FROM Users WHERE role_id = 4 AND created_at < @monthStart) AS customer_count_last_month,
                    (SELECT COUNT(*) FROM Orders) AS order_count,
                    (SELECT COUNT(*) FROM Orders WHERE order_date < @monthStart) AS order_count_last_month,
                    (SELECT COUNT(*) FROM Products) AS product_count,
                    (SELECT COUNT(*) FROM Products WHERE created_at < @monthStart) AS product_count_last_month,
                    (SELECT COALESCE(SUM(total_amount), 0) FROM Orders) AS revenue,
                    (SELECT COALESCE(SUM(total_amount), 0) FROM Orders WHERE order_date < @monthStart) AS revenue_last_month";

            using (var statsCmd = new MySqlCommand(statsSql, _connection))
            {
                statsCmd.Parameters.AddWithValue("@monthStart", monthStart);
                using (var statsReader = statsCmd.ExecuteReader())
                {
                    if (statsReader.Read())
                    {
                        var totalCustomers = statsReader.GetInt32("customer_count");
                        var customersLastMonth = statsReader.GetInt32("customer_count_last_month");
                        var totalOrders = statsReader.GetInt32("order_count");
                        var ordersLastMonth = statsReader.GetInt32("order_count_last_month");
                        var totalProducts = statsReader.GetInt32("product_count");
                        var productsLastMonth = statsReader.GetInt32("product_count_last_month");
                        var revenue = statsReader.GetDecimal("revenue");
                        var revenueLastMonth = statsReader.GetDecimal("revenue_last_month");

                        model.Stats = new List<StatCard>
                        {
                            new StatCard
                            {
                                Label = "Total Customers",
                                Value = totalCustomers.ToString("N0"),
                                Change = GetPercentChangeText(totalCustomers, customersLastMonth),
                                IsPositive = totalCustomers >= customersLastMonth,
                                IconClass = "icon-green"
                            },
                            new StatCard
                            {
                                Label = "Total Orders",
                                Value = totalOrders.ToString("N0"),
                                Change = GetPercentChangeText(totalOrders, ordersLastMonth),
                                IsPositive = totalOrders >= ordersLastMonth,
                                IconClass = "icon-purple"
                            },
                            new StatCard
                            {
                                Label = "Products",
                                Value = totalProducts.ToString("N0"),
                                Change = GetPercentChangeText(totalProducts, productsLastMonth),
                                IsPositive = totalProducts >= productsLastMonth,
                                IconClass = "icon-blue"
                            },
                            new StatCard
                            {
                                Label = "Revenue",
                                Value = $"฿{revenue:N0}",
                                Change = GetPercentChangeText(revenue, revenueLastMonth),
                                IsPositive = revenue >= revenueLastMonth,
                                IconClass = "icon-orange"
                            }
                        };
                    }
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
                    COALESCE(SUM(o.total_amount), 0) AS total_spend
                FROM Users u
                JOIN Orders o ON o.user_id = u.user_id
                LEFT JOIN User_Profiles up ON up.user_id = u.user_id
                GROUP BY u.user_id, u.username, u.email, up.first_name, up.last_name
                ORDER BY total_spend DESC
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
                        Amount = $"฿{topReader.GetDecimal("total_spend"):N0}",
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

    public IActionResult Permissions()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        return View();
    } 

    public IActionResult Stock()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        var products = new List<Product>();

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            const string sql = @"
                SELECT
                    p.product_id,
                    p.name,
                    p.price,
                    p.stock_quantity,
                    c.category_name,
                    COALESCE(pi.image_url, '') AS image_url
                FROM Products p
                LEFT JOIN Categories c ON c.category_id = p.category_id
                LEFT JOIN Product_Images pi ON pi.product_id = p.product_id AND pi.is_main = 1
                ORDER BY p.name ASC";

            using var cmd = new MySqlCommand(sql, _connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                products.Add(new Product
                {
                    ProductId = reader.GetInt32("product_id"),
                    Name = reader["name"] as string ?? string.Empty,
                    Price = reader.GetDecimal("price"),
                    StockQuantity = reader.GetInt32("stock_quantity"),
                    CategoryName = reader["category_name"] as string ?? "",
                    ImageUrl = reader["image_url"] as string ?? string.Empty
                });
            }
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

    [HttpPost]
    public IActionResult UpdateStock([FromBody] StockUpdateRequest request)
    {
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        if (request == null || request.ProductId <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid product or quantity" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            const string productSql = "SELECT stock_quantity FROM Products WHERE product_id = @productId";
            int currentStock;
            using (var cmd = new MySqlCommand(productSql, _connection))
            {
                cmd.Parameters.AddWithValue("@productId", request.ProductId);
                var result = cmd.ExecuteScalar();
                if (result == null)
                {
                    return NotFound(new { success = false, message = "Product not found" });
                }

                currentStock = Convert.ToInt32(result);
            }

            var updatedStock = currentStock + request.QuantityChange;
            if (updatedStock < 0)
            {
                updatedStock = 0;
            }

            const string updateSql = "UPDATE Products SET stock_quantity = @stock WHERE product_id = @productId";
            using (var cmd = new MySqlCommand(updateSql, _connection))
            {
                cmd.Parameters.AddWithValue("@stock", updatedStock);
                cmd.Parameters.AddWithValue("@productId", request.ProductId);
                cmd.ExecuteNonQuery();
            }

            return Ok(new { success = true, message = "Stock updated successfully", updatedStock });
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

        var model = new AdminCustomerViewModel();

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            const string statsSql = @"
                SELECT
                    (SELECT COUNT(*) FROM Users WHERE role_id = 4) AS total_customers,
                    (SELECT COUNT(*) FROM Users WHERE role_id = 4 
                     AND MONTH(created_at) = MONTH(CURRENT_DATE()) 
                     AND YEAR(created_at) = YEAR(CURRENT_DATE())) AS new_this_month,
                    (SELECT COUNT(*) FROM Orders) AS total_orders,
                    (SELECT COUNT(DISTINCT user_id) FROM (
                        SELECT user_id, COUNT(*) AS order_count
                        FROM Orders
                        GROUP BY user_id
                        HAVING order_count > 1
                    ) AS repeat_calc) AS repeat_customers";

            using (var cmd = new MySqlCommand(statsSql, _connection))
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    model.TotalCustomers = reader.GetInt32("total_customers");
                    model.NewThisMonth = reader.GetInt32("new_this_month");
                    model.TotalOrders = reader.GetInt32("total_orders");
                    model.RepeatCustomers = reader.GetInt32("repeat_customers");
                }
            }

            const string customersSql = @"
                SELECT
                    u.user_id,
                    COALESCE(up.first_name, '') AS first_name,
                    COALESCE(up.last_name, '') AS last_name,
                    COALESCE(u.email, '') AS email,
                    COALESCE(u.phone, '') AS phone,
                    u.created_at AS created_at,
                    COUNT(o.order_id) AS order_count,
                    COALESCE(SUM(o.total_amount), 0) AS total_spent,
                    MIN(o.order_date) AS first_order_date,
                    MAX(o.order_date) AS last_order_date
                FROM Users u
                LEFT JOIN User_Profiles up ON up.user_id = u.user_id
                LEFT JOIN Orders o ON o.user_id = u.user_id
                WHERE u.role_id = 4
                GROUP BY u.user_id, up.first_name, up.last_name, u.email, u.phone, u.created_at
                ORDER BY last_order_date DESC, total_spent DESC";

            using (var cmd = new MySqlCommand(customersSql, _connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var firstName = reader.GetString("first_name");
                    var lastName = reader.GetString("last_name");
                    var name = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)
                        ? reader.GetString("email")
                        : $"{firstName} {lastName}".Trim();

                    var spent = reader.GetDecimal("total_spent");
                    var lastOrderDate = reader.IsDBNull(reader.GetOrdinal("last_order_date"))
                        ? (DateTime?)null
                        : reader.GetDateTime("last_order_date");

                    var status = "Inactive";
                    if (spent >= 2000) status = "VIP";
                    else if (lastOrderDate.HasValue && lastOrderDate.Value >= DateTime.Today.AddDays(-30)) status = "Active";

                    model.Customers.Add(new AdminCustomerItem
                    {
                        UserId = reader.GetInt32("user_id"),
                        Name = name,
                        Email = reader.GetString("email"),
                        Phone = reader.GetString("phone"),
                        Orders = reader.GetInt32("order_count"),
                        Spent = spent,
                        Created = reader.IsDBNull(reader.GetOrdinal("created_at"))
                            ? "-"
                            : reader.GetDateTime("created_at").ToString("MMM d, yyyy"),
                        LastOrder = lastOrderDate?.ToString("MMM d, yyyy") ?? "-",
                        Initials = GetInitials(name),
                        Color = status == "VIP" ? "orange" : status == "Active" ? "green" : "purple"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            ViewBag.ErrorMessage = $"Failed to load customer data: {ex.Message}";
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

    public IActionResult Orders()
    {
        // ตรวจสอบสิทธิ์
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        var model = new AdminOrdersViewModel();

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            const string ordersSql = @"
                SELECT
                    o.order_id,
                    o.order_date,
                    o.total_amount,
                    o.status,
                    u.username,
                    u.email,
                    COALESCE(up.first_name, '') AS first_name,
                    COALESCE(up.last_name, '') AS last_name,
                    GROUP_CONCAT(CONCAT(p.name, ' x', od.quantity) SEPARATOR ', ') AS products,
                    SUM(od.quantity) AS item_count
                FROM Orders o
                LEFT JOIN Users u ON u.user_id = o.user_id
                LEFT JOIN User_Profiles up ON up.user_id = u.user_id
                LEFT JOIN OrderDetails od ON od.order_id = o.order_id
                LEFT JOIN Products p ON p.product_id = od.product_id
                GROUP BY o.order_id, o.order_date, o.total_amount, o.status, u.username, u.email, up.first_name, up.last_name
                ORDER BY o.order_date DESC";

            using (var cmd = new MySqlCommand(ordersSql, _connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var firstName = reader.GetString("first_name");
                    var lastName = reader.GetString("last_name");
                    var username = reader.GetString("username");
                    var email = reader.GetString("email");
                    var status = NormalizeStatus(reader.GetString("status"));
                    var orderDate = reader.GetDateTime("order_date");

                    model.Orders.Add(new AdminOrderItem
                    {
                        Id = $"#ORD-{reader.GetInt32("order_id"):D3}",
                        First = string.IsNullOrWhiteSpace(firstName) ? username : firstName,
                        Last = lastName,
                        Email = email,
                        Products = reader.IsDBNull(reader.GetOrdinal("products")) ? "" : reader.GetString("products"),
                        Items = reader.IsDBNull(reader.GetOrdinal("item_count")) ? 0 : reader.GetInt32("item_count"),
                        Amount = reader.GetDecimal("total_amount"),
                        Status = status,
                        Date = orderDate.ToString("MMM d, yyyy"),
                        Time = orderDate.ToString("h:mm tt"),
                        Color = status == "Completed" ? "av-blue" : status == "Pending" ? "av-orange" : "av-purple"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            ViewBag.ErrorMessage = $"Failed to load orders: {ex.Message}";
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

            string query = @"SELECT c.category_id, c.category_name, p.product_id, p.name AS product_name, p.price, p.stock_quantity
FROM Categories c
LEFT JOIN Products p ON p.category_id = c.category_id
ORDER BY c.category_name, p.name";
            var categoryMap = new Dictionary<int, CategoryItem>();

            using (MySqlCommand cmd = new MySqlCommand(query, _connection))
            {
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int categoryId = reader.GetInt32("category_id");
                        if (!categoryMap.TryGetValue(categoryId, out var category))
                        {
                            category = new CategoryItem
                            {
                                CategoryId = categoryId,
                                Name = reader.GetString("category_name"),
                                Description = string.Empty,
                                ProductCount = 0,
                                Status = "Active",
                                IsFeatured = false,
                                IsExpanded = false,
                                SubcategoryCount = null,
                            };
                            categoryMap.Add(categoryId, category);
                        }

                        if (!reader.IsDBNull(reader.GetOrdinal("product_id")))
                        {
                            var product = new Product
                            {
                                ProductId = reader.GetInt32("product_id"),
                                Name = reader.GetString("product_name"),
                                Price = reader.IsDBNull(reader.GetOrdinal("price")) ? 0m : reader.GetDecimal("price"),
                                StockQuantity = reader.IsDBNull(reader.GetOrdinal("stock_quantity")) ? 0 : reader.GetInt32("stock_quantity")
                            };

                            category.Products.Add(product);
                            category.ProductCount = category.Products.Count;
                        }
                    }
                }
            }

            model.Categories.AddRange(categoryMap.Values);

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

    [HttpPost]
    public IActionResult AddCategory([FromBody] CategoryRequest request)
    {
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        if (request == null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { success = false, message = "Category name is required." });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            string checkQuery = "SELECT category_id FROM Categories WHERE category_name = @name";
            using (var cmd = new MySqlCommand(checkQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@name", request.Name.Trim());
                if (cmd.ExecuteScalar() != null)
                {
                    return BadRequest(new { success = false, message = "A category with that name already exists." });
                }
            }

            string insertQuery = "INSERT INTO Categories (category_name) VALUES (@name)";
            using (var cmd = new MySqlCommand(insertQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@name", request.Name.Trim());
                cmd.ExecuteNonQuery();
            }

            return Ok(new { success = true, message = "Category added." });
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
    public IActionResult EditCategory([FromBody] CategoryRequest request)
    {
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        if (request == null || request.CategoryId <= 0 || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { success = false, message = "Invalid category data." });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            string checkQuery = "SELECT category_id FROM Categories WHERE category_name = @name AND category_id <> @id";
            using (var cmd = new MySqlCommand(checkQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@name", request.Name.Trim());
                cmd.Parameters.AddWithValue("@id", request.CategoryId);
                if (cmd.ExecuteScalar() != null)
                {
                    return BadRequest(new { success = false, message = "A category with that name already exists." });
                }
            }

            string updateQuery = "UPDATE Categories SET category_name = @name WHERE category_id = @id";
            using (var cmd = new MySqlCommand(updateQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@name", request.Name.Trim());
                cmd.Parameters.AddWithValue("@id", request.CategoryId);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                {
                    return NotFound(new { success = false, message = "Category not found." });
                }
            }

            return Ok(new { success = true, message = "Category updated." });
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
    public IActionResult DeleteCategory([FromBody] DeleteCategoryRequest request)
    {
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        if (request == null || request.CategoryId <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid category." });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            string deleteQuery = "DELETE FROM Categories WHERE category_id = @id";
            using (var cmd = new MySqlCommand(deleteQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@id", request.CategoryId);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                {
                    return NotFound(new { success = false, message = "Category not found." });
                }
            }

            return Ok(new { success = true, message = "Category deleted." });
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

        var promotions = new List<PromotionViewModel>();

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
                _connection.Open();

            const string couponSql = @"SELECT coupon_id, code, discount_value, discount_type, expiry_date, usage_limit, used_count
                                       FROM Coupons";

            using (var couponCmd = new MySqlCommand(couponSql, _connection))
            using (var reader = couponCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var code = reader.GetString("code").Trim();
                    var expiryDate = reader.IsDBNull(reader.GetOrdinal("expiry_date"))
                        ? (DateTime?)null
                        : reader.GetDateTime("expiry_date");
                    var usageLimit = reader.IsDBNull(reader.GetOrdinal("usage_limit"))
                        ? 0
                        : reader.GetInt32("usage_limit");
                    var usedCount = reader.IsDBNull(reader.GetOrdinal("used_count"))
                        ? 0
                        : reader.GetInt32("used_count");
                    var discountValue = reader.GetDecimal("discount_value");
                    var discountType = reader.GetString("discount_type")?.Trim().ToLower() ?? "percent";
                    var status = "open";

                    if (expiryDate.HasValue && DateTime.UtcNow.Date > expiryDate.Value.Date)
                        status = "closed";
                    else if (usageLimit > 0 && usedCount >= usageLimit)
                        status = "closed";

                    var discountText = discountType == "fixed"
                        ? $"฿{discountValue:0.##}"
                        : $"{discountValue:0.##}%";

                    promotions.Add(new PromotionViewModel
                    {
                        Id = reader.GetInt32("coupon_id"),
                        Type = "coupon",
                        Name = code,
                        Code = code,
                        Discount = discountText,
                        DiscountType = discountType,
                        MinSpend = "฿0",
                        IsFreeShipping = false,
                        Category = "All Categories",
                        DateStart = expiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                        DateEnd = expiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                        ExpiryDate = expiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                        Status = status,
                        Used = usedCount,
                        Limit = usageLimit,
                        ItemJson = "[]"
                    });
                }
            }

            const string promoSql = @"SELECT promo_id, promo_name, min_spend, discount_amount, is_free_shipping, start_date, end_date, is_active
                                       FROM Promotions";

            var systemPromotionCategories = new Dictionary<int, string>();

            using (var promoCmd = new MySqlCommand(promoSql, _connection))
            using (var reader = promoCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var name = reader.GetString("promo_name");
                    var minSpend = reader["min_spend"]?.ToString() ?? "฿0";
                    var discountAmount = reader.IsDBNull(reader.GetOrdinal("discount_amount"))
                        ? 0m
                        : reader.GetDecimal("discount_amount");
                    var starts = reader.IsDBNull(reader.GetOrdinal("start_date"))
                        ? (DateTime?)null
                        : reader.GetDateTime("start_date");
                    var ends = reader.IsDBNull(reader.GetOrdinal("end_date"))
                        ? (DateTime?)null
                        : reader.GetDateTime("end_date");
                    var isFreeShipping = !reader.IsDBNull(reader.GetOrdinal("is_free_shipping")) && reader.GetBoolean("is_free_shipping");
                    var isActive = !reader.IsDBNull(reader.GetOrdinal("is_active")) && reader.GetBoolean("is_active");
                    var status = isActive ? "open" : "closed";

                    promotions.Add(new PromotionViewModel
                    {
                        Id = reader.GetInt32("promo_id"),
                        Type = "system",
                        Name = name,
                        Code = string.Empty,
                        Discount = $"฿{discountAmount:0.##}",
                        DiscountType = "fixed",
                        MinSpend = minSpend,
                        IsFreeShipping = isFreeShipping,
                        Category = "All Categories",
                        DateStart = starts?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                        DateEnd = ends?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                        ExpiryDate = ends?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                        Status = status,
                        Used = 0,
                        Limit = 0
                    });
                }
            }

            var systemPromotionRequirements = new Dictionary<int, List<PromotionRequirementViewModel>>();
            const string promoReqSql = @"SELECT pr.promo_id,
                                                pr.category_id,
                                                pr.product_id,
                                                pr.min_quantity,
                                                COALESCE(c.category_name, pc.category_name, 'All Categories') AS category_name,
                                                p.name AS product_name
                                           FROM Promotion_Requirements pr
                                           LEFT JOIN Categories c ON pr.category_id = c.category_id
                                           LEFT JOIN Products p ON pr.product_id = p.product_id
                                           LEFT JOIN Categories pc ON p.category_id = pc.category_id;";

            using (var reqCmd = new MySqlCommand(promoReqSql, _connection))
            using (var reqReader = reqCmd.ExecuteReader())
            {
                while (reqReader.Read())
                {
                    var promoId = reqReader.GetInt32("promo_id");
                    var categoryName = reqReader.IsDBNull(reqReader.GetOrdinal("category_name"))
                        ? "All Categories"
                        : reqReader.GetString("category_name").Trim();
                    var productName = reqReader.IsDBNull(reqReader.GetOrdinal("product_name"))
                        ? string.Empty
                        : reqReader.GetString("product_name").Trim();
                    var productId = reqReader.IsDBNull(reqReader.GetOrdinal("product_id"))
                        ? (int?)null
                        : reqReader.GetInt32("product_id");
                    var minQuantity = reqReader.IsDBNull(reqReader.GetOrdinal("min_quantity"))
                        ? 0
                        : reqReader.GetInt32("min_quantity");

                    if (!systemPromotionRequirements.TryGetValue(promoId, out var list))
                    {
                        list = new List<PromotionRequirementViewModel>();
                        systemPromotionRequirements[promoId] = list;
                    }

                    list.Add(new PromotionRequirementViewModel
                    {
                        Category = categoryName,
                        ProductId = productId,
                        ProductName = productName,
                        MinQty = minQuantity,
                        MaxQty = 0
                    });
                }
            }

            foreach (var promo in promotions.Where(p => p.Type == "system"))
            {
                if (systemPromotionRequirements.TryGetValue(promo.Id, out var requirements) && requirements.Any())
                {
                    promo.Items = requirements;
                    promo.ItemJson = System.Text.Json.JsonSerializer.Serialize(requirements, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    });
                    var distinctCategories = requirements
                        .Select(i => i.Category)
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    promo.Category = distinctCategories.Any()
                        ? string.Join(", ", distinctCategories)
                        : "All Categories";
                }
                else if (systemPromotionCategories.TryGetValue(promo.Id, out var categoryName))
                {
                    promo.Category = string.IsNullOrWhiteSpace(categoryName) ? "All Categories" : categoryName;
                    promo.ItemJson = "[]";
                }
                else
                {
                    promo.ItemJson = "[]";
                }
            }

            var promoCategories = new List<string> { "All Categories" };
            const string categorySql = "SELECT category_name FROM Categories ORDER BY category_name;";
            using (var categoryCmd = new MySqlCommand(categorySql, _connection))
            using (var categoryReader = categoryCmd.ExecuteReader())
            {
                while (categoryReader.Read())
                {
                    var categoryName = categoryReader.GetString("category_name").Trim();
                    if (!string.IsNullOrWhiteSpace(categoryName) && !promoCategories.Contains(categoryName))
                        promoCategories.Add(categoryName);
                }
            }

            var promoProducts = new List<Product>();
            const string productSql = @"SELECT p.product_id, p.name, COALESCE(c.category_name, 'All Categories') AS category_name
                                       FROM Products p
                                       LEFT JOIN Categories c ON p.category_id = c.category_id
                                       WHERE p.is_active = 1
                                       ORDER BY p.name;";
            using (var productCmd = new MySqlCommand(productSql, _connection))
            using (var productReader = productCmd.ExecuteReader())
            {
                while (productReader.Read())
                {
                    promoProducts.Add(new Product
                    {
                        ProductId = productReader.GetInt32("product_id"),
                        Name = productReader.IsDBNull(productReader.GetOrdinal("name")) ? string.Empty : productReader.GetString("name"),
                        CategoryName = productReader.IsDBNull(productReader.GetOrdinal("category_name")) ? "All Categories" : productReader.GetString("category_name")
                    });
                }
            }

            ViewBag.PromoCategories = promoCategories;
            ViewBag.PromoProducts = promoProducts;
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }

        return View(promotions);
    }

    [HttpPost]
    public IActionResult SavePromotion([FromBody] SavePromotionRequest request)
    {
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;
        if (request == null)
            return BadRequest(new { success = false, message = "Invalid promotion request." });

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.StartDate) || (request.Type != "coupon" && string.IsNullOrWhiteSpace(request.EndDate)))
            return BadRequest(new { success = false, message = "Please complete the required promotion fields." });

        if (request.Type == "coupon" && string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { success = false, message = "Coupon code is required for coupon promotions." });

        if (request.Type == "system" && (request.Items == null || request.Items.Count == 0))
            return BadRequest(new { success = false, message = "Please add at least one product or category requirement for system promotions." });

        if (_connection.State == System.Data.ConnectionState.Closed)
            _connection.Open();

        using var transaction = _connection.BeginTransaction();
        try
        {
            decimal discountValue = request.DiscountValue;
            decimal minSpend = request.MinSpend;
            if (minSpend < 0) minSpend = 0;
            bool isActive = request.Status?.Trim().ToLowerInvariant() == "open";
            DateTime? startDate = null;
            DateTime? endDate = null;
            DateTime? expiryDate = null;
            var dateFormats = new[] { "yyyy-MM-dd", "MM/dd/yyyy", "yyyy/MM/dd" };

            if (DateTime.TryParseExact(request.StartDate, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedStart))
                startDate = parsedStart;
            if (DateTime.TryParseExact(request.EndDate, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd))
                endDate = parsedEnd;
            if (!string.IsNullOrWhiteSpace(request.Expiry) && DateTime.TryParseExact(request.Expiry, dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedExpiry))
                expiryDate = parsedExpiry;

            // Coupons may have no expiry date. Do not auto-fill it from the start or end date.
            if (request.Type == "coupon" && request.Status?.Trim().ToLowerInvariant() == "closed")
            {
                if (!expiryDate.HasValue || expiryDate.Value.Date >= DateTime.UtcNow.Date)
                    expiryDate = DateTime.UtcNow.Date.AddDays(-1);
            }

            var couponDiscountType = (request.DiscountType?.ToLowerInvariant() == "percent" || request.DiscountType?.ToLowerInvariant() == "percentage")
                ? "Percentage"
                : "Fixed";

            var isUpdate = request.Id > 0;
            var oldType = request.OldType?.Trim().ToLowerInvariant() ?? request.Type;

            if (request.Type == "coupon")
            {
                if (isUpdate && oldType == "coupon")
                {
                    const string updateCouponSql = @"UPDATE Coupons
                                                     SET code = @code,
                                                         discount_value = @discountValue,
                                                         discount_type = @discountType,
                                                         expiry_date = @expiryDate,
                                                         usage_limit = @usageLimit
                                                     WHERE coupon_id = @id;";
                    using var couponCmd = new MySqlCommand(updateCouponSql, _connection, transaction);
                    couponCmd.Parameters.AddWithValue("@code", request.Code);
                    couponCmd.Parameters.AddWithValue("@discountValue", discountValue);
                    couponCmd.Parameters.AddWithValue("@discountType", couponDiscountType);
                    couponCmd.Parameters.AddWithValue("@expiryDate", expiryDate.HasValue ? (object)expiryDate.Value : DBNull.Value);
                    couponCmd.Parameters.AddWithValue("@usageLimit", request.Limit);
                    couponCmd.Parameters.AddWithValue("@id", request.Id);
                    couponCmd.ExecuteNonQuery();
                }
                else
                {
                    if (isUpdate && oldType == "system")
                    {
                        const string deleteRequirementsSql = @"DELETE FROM Promotion_Requirements WHERE promo_id = @id;";
                        using var deleteReqCmd = new MySqlCommand(deleteRequirementsSql, _connection, transaction);
                        deleteReqCmd.Parameters.AddWithValue("@id", request.Id);
                        deleteReqCmd.ExecuteNonQuery();

                        const string deletePromoSql = @"DELETE FROM Promotions WHERE promo_id = @id;";
                        using var deletePromoCmd = new MySqlCommand(deletePromoSql, _connection, transaction);
                        deletePromoCmd.Parameters.AddWithValue("@id", request.Id);
                        deletePromoCmd.ExecuteNonQuery();
                    }

                    const string insertCouponSql = @"INSERT INTO Coupons (code, discount_value, discount_type, expiry_date, usage_limit, used_count)
                                                     VALUES (@code, @discountValue, @discountType, @expiryDate, @usageLimit, 0);";
                    using var couponCmd = new MySqlCommand(insertCouponSql, _connection, transaction);
                    couponCmd.Parameters.AddWithValue("@code", request.Code);
                    couponCmd.Parameters.AddWithValue("@discountValue", discountValue);
                    couponCmd.Parameters.AddWithValue("@discountType", couponDiscountType);
                    couponCmd.Parameters.AddWithValue("@expiryDate", expiryDate.HasValue ? (object)expiryDate.Value : DBNull.Value);
                    couponCmd.Parameters.AddWithValue("@usageLimit", request.Limit);
                    couponCmd.ExecuteNonQuery();
                }
            }
            else
            {
                if (isUpdate && oldType == "system")
                {
                    const string updatePromoSql = @"UPDATE Promotions
                                                   SET promo_name = @name,
                                                       min_spend = @minSpend,
                                                       discount_amount = @discountAmount,
                                                       is_free_shipping = @freeShipping,
                                                       start_date = @startDate,
                                                       end_date = @endDate,
                                                       is_active = @isActive
                                                   WHERE promo_id = @id;";
                    using var promoCmd = new MySqlCommand(updatePromoSql, _connection, transaction);
                    promoCmd.Parameters.AddWithValue("@name", request.Name);
                    promoCmd.Parameters.AddWithValue("@minSpend", minSpend);
                    promoCmd.Parameters.AddWithValue("@discountAmount", discountValue);
                    promoCmd.Parameters.AddWithValue("@freeShipping", request.IsFreeShipping);
                    promoCmd.Parameters.AddWithValue("@startDate", startDate.HasValue ? (object)startDate.Value : DBNull.Value);
                    promoCmd.Parameters.AddWithValue("@endDate", endDate.HasValue ? (object)endDate.Value : DBNull.Value);
                    promoCmd.Parameters.AddWithValue("@isActive", isActive);
                    promoCmd.Parameters.AddWithValue("@id", request.Id);
                    promoCmd.ExecuteNonQuery();

                    const string deleteRequirementsSql = @"DELETE FROM Promotion_Requirements WHERE promo_id = @id;";
                    using var deleteReqCmd = new MySqlCommand(deleteRequirementsSql, _connection, transaction);
                    deleteReqCmd.Parameters.AddWithValue("@id", request.Id);
                    deleteReqCmd.ExecuteNonQuery();

                    const string insertRequirementSql = @"INSERT INTO Promotion_Requirements (promo_id, category_id, product_id, min_quantity)
                                                          VALUES (@promoId, @categoryId, @productId, @minQuantity);";
                    foreach (var item in request.Items)
                    {
                        int? categoryId = null;
                        if (!string.IsNullOrWhiteSpace(item.Category) && item.Category.ToLower() != "all categories")
                        {
                            const string categorySql = "SELECT category_id FROM Categories WHERE category_name = @categoryName LIMIT 1";
                            using var categoryCmd = new MySqlCommand(categorySql, _connection, transaction);
                            categoryCmd.Parameters.AddWithValue("@categoryName", item.Category);
                            var categoryResult = categoryCmd.ExecuteScalar();
                            if (categoryResult != null && categoryResult != DBNull.Value)
                                categoryId = Convert.ToInt32(categoryResult);
                        }

                        if (!categoryId.HasValue && item.ProductId.HasValue)
                        {
                            const string productCategorySql = "SELECT category_id FROM Products WHERE product_id = @productId LIMIT 1";
                            using var productCategoryCmd = new MySqlCommand(productCategorySql, _connection, transaction);
                            productCategoryCmd.Parameters.AddWithValue("@productId", item.ProductId.Value);
                            var categoryResult = productCategoryCmd.ExecuteScalar();
                            if (categoryResult != null && categoryResult != DBNull.Value)
                                categoryId = Convert.ToInt32(categoryResult);
                        }

                        using var reqCmd = new MySqlCommand(insertRequirementSql, _connection, transaction);
                        reqCmd.Parameters.AddWithValue("@promoId", request.Id);
                        reqCmd.Parameters.AddWithValue("@categoryId", categoryId.HasValue ? (object)categoryId.Value : DBNull.Value);
                        reqCmd.Parameters.AddWithValue("@productId", item.ProductId.HasValue ? (object)item.ProductId.Value : DBNull.Value);
                        reqCmd.Parameters.AddWithValue("@minQuantity", item.MaxQty > 0 ? item.MaxQty : item.MinQty);
                        reqCmd.ExecuteNonQuery();
                    }
                }
                else if (isUpdate && oldType == "coupon")
                {
                    const string deleteCouponSql = @"DELETE FROM Coupons WHERE coupon_id = @id;";
                    using var deleteCouponCmd = new MySqlCommand(deleteCouponSql, _connection, transaction);
                    deleteCouponCmd.Parameters.AddWithValue("@id", request.Id);
                    deleteCouponCmd.ExecuteNonQuery();

                    const string insertPromoSql = @"INSERT INTO Promotions (promo_name, min_spend, discount_amount, is_free_shipping, start_date, end_date, is_active)
                                                   VALUES (@name, @minSpend, @discountAmount, @freeShipping, @startDate, @endDate, @isActive);";
                    int promoId;
                    using (var promoCmd = new MySqlCommand(insertPromoSql, _connection, transaction))
                    {
                        promoCmd.Parameters.AddWithValue("@name", request.Name);
                        promoCmd.Parameters.AddWithValue("@minSpend", minSpend);
                        promoCmd.Parameters.AddWithValue("@discountAmount", discountValue);
                        promoCmd.Parameters.AddWithValue("@freeShipping", request.IsFreeShipping);
                        promoCmd.Parameters.AddWithValue("@startDate", startDate.HasValue ? (object)startDate.Value : DBNull.Value);
                        promoCmd.Parameters.AddWithValue("@endDate", endDate.HasValue ? (object)endDate.Value : DBNull.Value);
                        promoCmd.Parameters.AddWithValue("@isActive", isActive);
                        promoCmd.ExecuteNonQuery();
                        promoId = Convert.ToInt32(promoCmd.LastInsertedId);
                    }

                    const string insertRequirementSql = @"INSERT INTO Promotion_Requirements (promo_id, category_id, product_id, min_quantity)
                                                          VALUES (@promoId, @categoryId, @productId, @minQuantity);";
                    foreach (var item in request.Items)
                    {
                        int? categoryId = null;
                        if (!string.IsNullOrWhiteSpace(item.Category) && item.Category.ToLower() != "all categories")
                        {
                            const string categorySql = "SELECT category_id FROM Categories WHERE category_name = @categoryName LIMIT 1";
                            using var categoryCmd = new MySqlCommand(categorySql, _connection, transaction);
                            categoryCmd.Parameters.AddWithValue("@categoryName", item.Category);
                            var categoryResult = categoryCmd.ExecuteScalar();
                            if (categoryResult != null && categoryResult != DBNull.Value)
                                categoryId = Convert.ToInt32(categoryResult);
                        }

                        if (!categoryId.HasValue && item.ProductId.HasValue)
                        {
                            const string productCategorySql = "SELECT category_id FROM Products WHERE product_id = @productId LIMIT 1";
                            using var productCategoryCmd = new MySqlCommand(productCategorySql, _connection, transaction);
                            productCategoryCmd.Parameters.AddWithValue("@productId", item.ProductId.Value);
                            var categoryResult = productCategoryCmd.ExecuteScalar();
                            if (categoryResult != null && categoryResult != DBNull.Value)
                                categoryId = Convert.ToInt32(categoryResult);
                        }

                        using var reqCmd = new MySqlCommand(insertRequirementSql, _connection, transaction);
                        reqCmd.Parameters.AddWithValue("@promoId", promoId);
                        reqCmd.Parameters.AddWithValue("@categoryId", categoryId.HasValue ? (object)categoryId.Value : DBNull.Value);
                        reqCmd.Parameters.AddWithValue("@productId", item.ProductId.HasValue ? (object)item.ProductId.Value : DBNull.Value);
                        reqCmd.Parameters.AddWithValue("@minQuantity", item.MaxQty > 0 ? item.MaxQty : item.MinQty);
                        reqCmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    const string insertPromoSql = @"INSERT INTO Promotions (promo_name, min_spend, discount_amount, is_free_shipping, start_date, end_date, is_active)
                                                   VALUES (@name, @minSpend, @discountAmount, @freeShipping, @startDate, @endDate, @isActive);";
                    int promoId;
                    using (var promoCmd = new MySqlCommand(insertPromoSql, _connection, transaction))
                    {
                        promoCmd.Parameters.AddWithValue("@name", request.Name);
                        promoCmd.Parameters.AddWithValue("@minSpend", minSpend);
                        promoCmd.Parameters.AddWithValue("@discountAmount", discountValue);
                        promoCmd.Parameters.AddWithValue("@freeShipping", request.IsFreeShipping);
                        promoCmd.Parameters.AddWithValue("@startDate", startDate.HasValue ? (object)startDate.Value : DBNull.Value);
                        promoCmd.Parameters.AddWithValue("@endDate", endDate.HasValue ? (object)endDate.Value : DBNull.Value);
                        promoCmd.Parameters.AddWithValue("@isActive", isActive);
                        promoCmd.ExecuteNonQuery();
                        promoId = Convert.ToInt32(promoCmd.LastInsertedId);
                    }

                    const string insertRequirementSql = @"INSERT INTO Promotion_Requirements (promo_id, category_id, product_id, min_quantity)
                                                          VALUES (@promoId, @categoryId, @productId, @minQuantity);";
                    foreach (var item in request.Items)
                    {
                        int? categoryId = null;
                        if (!string.IsNullOrWhiteSpace(item.Category) && item.Category.ToLower() != "all categories")
                        {
                            const string categorySql = "SELECT category_id FROM Categories WHERE category_name = @categoryName LIMIT 1";
                            using var categoryCmd = new MySqlCommand(categorySql, _connection, transaction);
                            categoryCmd.Parameters.AddWithValue("@categoryName", item.Category);
                            var categoryResult = categoryCmd.ExecuteScalar();
                            if (categoryResult != null && categoryResult != DBNull.Value)
                                categoryId = Convert.ToInt32(categoryResult);
                        }

                        if (!categoryId.HasValue && item.ProductId.HasValue)
                        {
                            const string productCategorySql = "SELECT category_id FROM Products WHERE product_id = @productId LIMIT 1";
                            using var productCategoryCmd = new MySqlCommand(productCategorySql, _connection, transaction);
                            productCategoryCmd.Parameters.AddWithValue("@productId", item.ProductId.Value);
                            var categoryResult = productCategoryCmd.ExecuteScalar();
                            if (categoryResult != null && categoryResult != DBNull.Value)
                                categoryId = Convert.ToInt32(categoryResult);
                        }

                        using var reqCmd = new MySqlCommand(insertRequirementSql, _connection, transaction);
                        reqCmd.Parameters.AddWithValue("@promoId", promoId);
                        reqCmd.Parameters.AddWithValue("@categoryId", categoryId.HasValue ? (object)categoryId.Value : DBNull.Value);
                        reqCmd.Parameters.AddWithValue("@productId", item.ProductId.HasValue ? (object)item.ProductId.Value : DBNull.Value);
                        reqCmd.Parameters.AddWithValue("@minQuantity", item.MaxQty > 0 ? item.MaxQty : item.MinQty);
                        reqCmd.ExecuteNonQuery();
                    }
                }
            }

            transaction.Commit();
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return BadRequest(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
    }

    [HttpPost]
    public IActionResult DeletePromotion([FromBody] DeletePromotionRequest request)
    {
        var accessCheck = CheckAdminAccess();
        if (accessCheck != null) return accessCheck;

        if (request == null || request.PromotionId <= 0 || string.IsNullOrWhiteSpace(request.Type))
            return BadRequest(new { success = false, message = "Invalid promotion delete request." });

        if (_connection.State == System.Data.ConnectionState.Closed)
            _connection.Open();

        using var transaction = _connection.BeginTransaction();
        try
        {
            if (request.Type == "coupon")
            {
                const string deleteCouponSql = "DELETE FROM Coupons WHERE coupon_id = @id";
                using var cmd = new MySqlCommand(deleteCouponSql, _connection, transaction);
                cmd.Parameters.AddWithValue("@id", request.PromotionId);
                var rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                    return NotFound(new { success = false, message = "Coupon not found." });
            }
            else if (request.Type == "system")
            {
                const string deleteRequirementsSql = "DELETE FROM Promotion_Requirements WHERE promo_id = @id";
                using (var reqCmd = new MySqlCommand(deleteRequirementsSql, _connection, transaction))
                {
                    reqCmd.Parameters.AddWithValue("@id", request.PromotionId);
                    reqCmd.ExecuteNonQuery();
                }

                const string deletePromoSql = "DELETE FROM Promotions WHERE promo_id = @id";
                using var promoCmd = new MySqlCommand(deletePromoSql, _connection, transaction);
                promoCmd.Parameters.AddWithValue("@id", request.PromotionId);
                var rows = promoCmd.ExecuteNonQuery();
                if (rows == 0)
                    return NotFound(new { success = false, message = "System promotion not found." });
            }
            else
            {
                return BadRequest(new { success = false, message = "Unsupported promotion type." });
            }

            transaction.Commit();
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return BadRequest(new { success = false, message = ex.Message });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                _connection.Close();
        }
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

    public class SavePromotionRequest
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public decimal DiscountValue { get; set; }
        public string DiscountType { get; set; } = "percent";
        public decimal MinSpend { get; set; }
        public bool IsFreeShipping { get; set; }
        public string Category { get; set; } = string.Empty;
        public int Limit { get; set; }
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string Expiry { get; set; } = string.Empty;
        public string Status { get; set; } = "open";
        public int Id { get; set; }
        public string OldType { get; set; } = string.Empty;
        public List<PromotionRequirementRequest> Items { get; set; } = new();
    }

    public class PromotionRequirementRequest
    {
        public string Category { get; set; } = string.Empty;
        public int? ProductId { get; set; }
        public int MinQty { get; set; }
        public int MaxQty { get; set; }
    }

    public class CategoryRequest
    {
        public int CategoryId { get; set; }
        public string? Name { get; set; }
    }

    public class DeleteCategoryRequest
    {
        public int CategoryId { get; set; }
    }

    public class StockUpdateRequest
    {
        public int ProductId { get; set; }
        public int QuantityChange { get; set; }
        public string? Note { get; set; }
    }

    public class DeletePromotionRequest
    {
        public int PromotionId { get; set; }
        public string Type { get; set; } = string.Empty;
    }
}