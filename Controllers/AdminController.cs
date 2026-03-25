using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using KeyboardWebsiteProject.Models;
using KeyboardWebsiteProject.Views.Admin;

public class AdminController : Controller
{
    private readonly MySqlConnection _connection;

    public AdminController(MySqlConnection connection)
    {
        _connection = connection;
    }

    public IActionResult Dashboard()
    {
        // 1. สร้างก้อนข้อมูลขึ้นมา
        var myDashboard = new DashboardModel(); 
        
        // 2. ส่งก้อนข้อมูลนั้นไปที่ไฟล์ Dashboard.cshtml
        return View(myDashboard); 
    }

    public IActionResult Settings()
    {
        return View();
    }

    public IActionResult Customers()
    {
        return View();
    }

    public IActionResult Orders()
    {
        return View();
    }

    public IActionResult Products()
    {
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

            // สร้าง SQL query ดึงข้อมูล Products กับ Categories
            string query = @"SELECT p.product_id, p.name, p.price, p.stock_quantity, p.image_url, 
                                   COALESCE(c.category_name, 'Uncategorized') as category_name 
                            FROM Products p 
                            LEFT JOIN Categories c ON p.category_id = c.category_id 
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
                            CategoryName = reader.GetString("category_name")
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

        // ส่งข้อมูลผ่าน ViewBag ไปยัง Products.cshtml
        ViewBag.AllProducts = products;
        ViewBag.Categories = categories;
        ViewBag.TotalProducts = products.Count;
        ViewBag.InStockCount = products.Count(p => p.StockQuantity > 0);
        ViewBag.OutOfStockCount = products.Count(p => p.StockQuantity == 0);
        ViewBag.LowStockCount = products.Count(p => p.StockQuantity > 0 && p.StockQuantity <= 10);

        return View(products);
    }
    
    public IActionResult Roles()
    {
        return View();
    }

    public IActionResult Promotions()
    {
        return View();
    }
}