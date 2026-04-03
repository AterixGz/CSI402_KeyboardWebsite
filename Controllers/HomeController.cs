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

            // สร้าง SQL query ดึงข้อมูล Products กับ Categories และ Brands (ได้ 4 รายการ)
            string query = @"SELECT p.product_id, p.name, p.price, p.stock_quantity, p.image_url, p.description,
                                   COALESCE(c.category_name, 'Uncategorized') as category_name,
                                   COALESCE(b.brand_name, '') as brand_name
                            FROM Products p 
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

            // สร้าง SQL query ดึงข้อมูล Products กับ Categories และ Brands
            string query = @"SELECT p.product_id, p.name, p.price, p.stock_quantity, p.image_url, p.description,
                                   COALESCE(c.category_name, 'Uncategorized') as category_name,
                                   COALESCE(b.brand_name, '') as brand_name
                            FROM Products p 
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

    public IActionResult Cart()
    {
        return View();
    }

    public IActionResult Productdetails(int id)
    {
        Product product = null;

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // Fetch product details
            string query = @"SELECT p.product_id, p.name, p.price, p.stock_quantity, p.image_url, p.description,
                                   COALESCE(c.category_name, 'Uncategorized') as category_name,
                                   COALESCE(b.brand_name, '') as brand_name
                            FROM Products p 
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

   

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
