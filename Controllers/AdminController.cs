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
    
    public IActionResult Roles()
    {
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
        return View();
    }

    [HttpPost]
    public IActionResult CreateProduct([FromBody] CreateProductRequest request)
    {
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
            string imageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? "~/image/default.png" : request.ImageUrl;
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

            // 3.1 อัปเดตรูปภาพหลักใน Product_Images
            string imageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? "~/image/default.png" : request.ImageUrl;
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

            string imageUrl = uploadResult.SecureUrl?.ToString();

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