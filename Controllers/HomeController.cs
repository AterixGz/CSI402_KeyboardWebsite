using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using KeyboardWebsiteProject.Models;

namespace KeyboardWebsiteProject.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly MySqlConnection _connection;

    public HomeController(ILogger<HomeController> logger, MySqlConnection connection)
    {
        _logger = logger;
        _connection = connection;
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
                                   COALESCE(b.brand_name, '') as brand_name
                            FROM Products p 
                            LEFT JOIN Product_Images pi ON p.product_id = pi.product_id AND pi.is_main = 1
                            LEFT JOIN Categories c ON p.category_id = c.category_id 
                            LEFT JOIN Brands b ON p.brand_id = b.brand_id
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
                            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? "" : reader.GetString("description")
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

    public IActionResult Shop()
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

        ViewBag.Products = products;
        return View();
    }

    [HttpPost]
    public IActionResult AddToCart(int productId, int quantity = 1, bool isBuyNow = false)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            if (Request.Headers.ContainsKey("X-Requested-With"))
            {
                return Json(new { success = false, message = "กรุณาเข้าสู่ระบบก่อนเพิ่มสินค้าลงในตะกร้า" });
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
                var result = cmd.ExecuteScalar();
                if (result == null)
                {
                    if (Request.Headers.ContainsKey("X-Requested-With"))
                    {
                        return Json(new { success = false, message = "สินค้าไม่ถูกต้อง" });
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

            string query = @"SELECT c.cart_id, c.product_id, c.quantity, p.name, p.price, p.stock_quantity,
                                   COALESCE(pi.image_url, '~/image/default.png') AS image_url,
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
                            Name = reader.GetString("name"),
                            Price = reader.GetDecimal("price"),
                            StockQuantity = reader.GetInt32("stock_quantity"),
                            ImageUrl = reader.IsDBNull(reader.GetOrdinal("image_url")) ? "~/image/default.png" : reader.GetString("image_url"),
                            CategoryName = reader.GetString("category_name"),
                            BrandName = reader.GetString("brand_name")
                        });
                    }
                }
            }

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

    public IActionResult Checkout()
    {
        // จุดจำลองการ checkout
        TempData["CartMessage"] = "ยังไม่เปิดใช้งานการชำระเงิน (Checkout) ในส่วนนี้";
        return RedirectToAction("Cart");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
