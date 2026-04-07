using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using KeyboardWebsiteProject.Models;
using System.Security.Cryptography;

namespace KeyboardWebsiteProject.Controllers;

public class AccountController : Controller
{
    private readonly MySqlConnection _connection;

    public AccountController(MySqlConnection connection)
    {
        _connection = connection;
    }

    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Login(LoginViewModel model)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // ตรวจสอบว่าผู้ใช้มีอยู่ในระบบหรือไม่
            string checkQuery = "SELECT user_id, role_id, password_hash FROM Users WHERE email = @email OR username = @email";
            string? passwordHash = null;
            int userId = 0;
            int userRole = 0;

            using (MySqlCommand cmd = new MySqlCommand(checkQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@email", model.Email);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        userId = reader.GetInt32("user_id");
                        userRole = reader.GetInt32("role_id");
                        passwordHash = reader.GetString("password_hash");
                    }
                }
            }

            // ถ้าไม่พบผู้ใช้ หรือรหัสผ่านไม่ถูกต้อง
            if (userId == 0 || !VerifyPassword(model.Password, passwordHash))
            {
                ModelState.AddModelError("", "Email หรือรหัสผ่านไม่ถูกต้อง");
                return View(model);
            }

            // ล้างการเชื่อมต่อ
            _connection.Close();

            // สร้าง session หรือ authentication cookie
            // ในที่นี้เราใช้ session เก็บ user_id และ role_id
            HttpContext.Session.SetInt32("UserId", userId);
            HttpContext.Session.SetInt32("UserRole", userRole);
            HttpContext.Session.SetString("Email", model.Email);

            // Redirect ไปยัง Home page
            return RedirectToAction("Index", "Home");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"เกิดข้อผิดพลาด: {ex.Message}");
            return View(model);
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }

    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Register(RegisterViewModel model)
    {
        try
        {
            // ตรวจสอบ validation
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // ตรวจสอบความตรงกันของรหัสผ่าน
            if (model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError("ConfirmPassword", "รหัสผ่านไม่ตรงกัน");
                return View(model);
            }

            // ตรวจสอบว่าตกลงเงื่อนไขการใช้บริการ
            if (!model.AgreeToTerms)
            {
                ModelState.AddModelError("AgreeToTerms", "คุณต้องตกลงเงื่อนไขการใช้บริการ");
                return View(model);
            }

            // ตรวจสอบว่า email หรือ username มีอยู่แล้ว
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            string checkQuery = "SELECT COUNT(*) FROM Users WHERE email = @email OR username = @username";
            using (MySqlCommand cmd = new MySqlCommand(checkQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@email", model.Email);
                cmd.Parameters.AddWithValue("@username", model.Username);
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                
                if (count > 0)
                {
                    ModelState.AddModelError("", "Email หรือ Username นี้มีอยู่แล้ว");
                    return View(model);
                }
            }

            // แฮชรหัสผ่านโดยใช้ bcrypt
            string passwordHash = HashPassword(model.Password);

            // เพิ่มผู้ใช้ใหม่ (role_id = 4 สำหรับ user ทั่วไป)
            string insertUserQuery = @"INSERT INTO Users (role_id, username, email, password_hash) 
                                      VALUES (4, @username, @email, @passwordHash)";
            int userId = 0;
            using (MySqlCommand cmd = new MySqlCommand(insertUserQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@username", model.Username);
                cmd.Parameters.AddWithValue("@email", model.Email);
                cmd.Parameters.AddWithValue("@passwordHash", passwordHash);
                cmd.ExecuteNonQuery();
                userId = (int)cmd.LastInsertedId;
            }

            // สร้าง profile สำหรับผู้ใช้ใหม่
            string insertProfileQuery = @"INSERT INTO User_Profiles (user_id, first_name, last_name, display_name) 
                                         VALUES (@userId, @firstName, @lastName, @displayName)";
            using (MySqlCommand cmd = new MySqlCommand(insertProfileQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId);
                cmd.Parameters.AddWithValue("@firstName", model.FirstName ?? "");
                cmd.Parameters.AddWithValue("@lastName", model.LastName ?? "");
                cmd.Parameters.AddWithValue("@displayName", $"{model.FirstName} {model.LastName}".Trim());
                cmd.ExecuteNonQuery();
            }

            // บันทึกการเลือก opt-in การตลาด (ถ้าต้องการ)
            if (model.MarketingOptIn)
            {
                // คุณสามารถบันทึกสิ่งนี้ในตาราง preferences หรือ audit
                // ในตัวอย่างนี้เพียงแค่บันทึกไว้
            }

            // ล้างการเชื่อมต่อ
            _connection.Close();

            // ส่ง redirect ไปยัง login page หรือ success page
            TempData["SuccessMessage"] = "สมัครสมาชิกสำเร็จ! กรุณาเข้าสู่ระบบ";
            return RedirectToAction("Login");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"เกิดข้อผิดพลาด: {ex.Message}");
            return View(model);
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }

    public IActionResult Profile()
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return RedirectToAction("Login");
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // Get user data with role name
            string userQuery = @"SELECT u.user_id, u.username, u.email, u.phone, r.role_name
                                 FROM Users u
                                 LEFT JOIN Roles r ON u.role_id = r.role_id
                                 WHERE u.user_id = @userId";
            object? user = null;
            using (MySqlCommand cmd = new MySqlCommand(userQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        user = new
                        {
                            UserId = reader.GetInt32("user_id"),
                            RoleName = reader["role_name"] as string ?? "สมาชิก",
                            Username = reader["username"] as string ?? "",
                            Email = reader["email"] as string ?? "",
                            Phone = reader["phone"] as string ?? ""
                        };
                    }
                    else
                    {
                        return RedirectToAction("Login");
                    }
                }
            }

            // Get profile data
            string profileQuery = @"SELECT profile_id, first_name, last_name, display_name, birth_date, avatar_url, bio
                                    FROM User_Profiles
                                    WHERE user_id = @userId";
            object? profile = null;
            using (MySqlCommand cmd = new MySqlCommand(profileQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        profile = new
                        {
                            ProfileId = reader.GetInt32("profile_id"),
                            FirstName = reader["first_name"] as string ?? "สมาชิก",
                            LastName = reader["last_name"] as string ?? "ใหม่",
                            DisplayName = reader["display_name"] as string ?? "",
                            BirthDate = reader.IsDBNull(reader.GetOrdinal("birth_date")) ? (DateTime?)null : reader.GetDateTime("birth_date"),
                            AvatarUrl = reader["avatar_url"] as string ?? "",
                            Bio = reader["bio"] as string ?? ""
                        };
                    }
                    else
                    {
                        // Create default profile if not exists
                        profile = new
                        {
                            ProfileId = 0,
                            FirstName = "สมาชิก",
                            LastName = "ใหม่",
                            DisplayName = "",
                            BirthDate = (DateTime?)null,
                            AvatarUrl = "",
                            Bio = ""
                        };
                    }
                }
            }

            // Get addresses
            string addressQuery = @"SELECT address_id, receiver_name, phone_number, address_line1, sub_district, district, province, postal_code, is_default
                                    FROM User_Addresses
                                    WHERE user_id = @userId
                                    ORDER BY is_default DESC, address_id";
            var addresses = new List<dynamic>();
            using (MySqlCommand cmd = new MySqlCommand(addressQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        addresses.Add(new
                        {
                            AddressId = reader.GetInt32("address_id"),
                            ReceiverName = reader["receiver_name"] as string ?? "",
                            PhoneNumber = reader["phone_number"] as string ?? "",
                            AddressLine1 = reader["address_line1"] as string ?? "",
                            SubDistrict = reader["sub_district"] as string ?? "",
                            District = reader["district"] as string ?? "",
                            Province = reader["province"] as string ?? "",
                            PostalCode = reader["postal_code"] as string ?? "",
                            IsDefault = reader.GetBoolean("is_default")
                        });
                    }
                }
            }

            // Get wishlist items
            var wishlists = new List<dynamic>();
            string wishlistQuery = @"SELECT w.wishlist_id, p.product_id, p.name, p.price, p.stock_quantity,
                                           COALESCE(pi.image_url, '~/image/default.png') AS image_url,
                                           COALESCE(c.category_name, 'Uncategorized') AS category_name
                                    FROM Wishlist w
                                    JOIN Products p ON w.product_id = p.product_id
                                    LEFT JOIN Product_Images pi ON p.product_id = pi.product_id AND pi.is_main = 1
                                    LEFT JOIN Categories c ON p.category_id = c.category_id
                                    WHERE w.user_id = @userId
                                    ORDER BY w.added_at DESC";
            using (MySqlCommand cmd = new MySqlCommand(wishlistQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        wishlists.Add(new
                        {
                            WishlistId = reader.GetInt32("wishlist_id"),
                            ProductId = reader.GetInt32("product_id"),
                            ProductName = reader["name"] as string ?? "",
                            Price = reader.GetDecimal("price"),
                            Category = reader["category_name"] as string ?? "Uncategorized",
                            ImageUrl = reader["image_url"] as string ?? "~/image/default.png",
                            InStock = reader.GetInt32("stock_quantity") > 0
                        });
                    }
                }
            }

            // Get recent orders (last 5)
            string orderQuery = @"SELECT o.order_id, o.order_date, o.total_amount, o.discount_amount, o.status, c.code AS coupon_code,
                                         COUNT(od.detail_id) as item_count
                                  FROM Orders o
                                  LEFT JOIN OrderDetails od ON o.order_id = od.order_id
                                  LEFT JOIN Coupons c ON o.coupon_id = c.coupon_id
                                  WHERE o.user_id = @userId
                                  GROUP BY o.order_id, o.order_date, o.total_amount, o.discount_amount, o.status, c.code
                                  ORDER BY o.order_date DESC
                                  LIMIT 5";
            var orders = new List<dynamic>();
            using (MySqlCommand cmd = new MySqlCommand(orderQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        orders.Add(new
                        {
                            OrderId = $"ORD-{reader.GetInt32("order_id"):D3}",
                            OrderDate = reader.GetDateTime("order_date").ToString("dd MMM yyyy"),
                            ItemCount = reader.GetInt32("item_count"),
                            Subtotal = reader.GetDecimal("total_amount") + reader.GetDecimal("discount_amount"),
                            DiscountAmount = reader.GetDecimal("discount_amount"),
                            TotalAmount = reader.GetDecimal("total_amount"),
                            CouponCode = reader["coupon_code"] as string,
                            Status = reader["status"] as string ?? "pending"
                        });
                    }
                }
            }

            // Get counts
            int orderCount = orders.Count; // Or query total count
            int wishCount = wishlists.Count;
            int addressCount = addresses.Count;

            ViewBag.User = user;
            ViewBag.Profile = profile;
            ViewBag.Addresses = addresses;
            ViewBag.RecentOrders = orders;
            ViewBag.Wishlists = wishlists;
            ViewBag.OrderCount = orderCount;
            ViewBag.WishCount = wishCount;
            ViewBag.AddressCount = addressCount;

            return View();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"เกิดข้อผิดพลาด: {ex.Message}");
            return View();
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }

    public IActionResult Logout()
    {
        // ล้าง session
        HttpContext.Session.Clear();
        
        // Redirect ไปยัง Home page
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    public IActionResult UpdateProfile(string firstName, string lastName, string displayName, DateTime? birthdate, string bio)
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบ" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // Check if profile exists
            string checkQuery = "SELECT profile_id FROM User_Profiles WHERE user_id = @userId";
            int profileId = 0;
            using (MySqlCommand cmd = new MySqlCommand(checkQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    profileId = Convert.ToInt32(result);
                }
            }

            if (profileId > 0)
            {
                // Update existing profile
                string updateQuery = @"UPDATE User_Profiles 
                                       SET first_name = @firstName, last_name = @lastName, display_name = @displayName, 
                                           birth_date = @birthdate, bio = @bio 
                                       WHERE profile_id = @profileId";
                using (MySqlCommand cmd = new MySqlCommand(updateQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@firstName", firstName ?? "");
                    cmd.Parameters.AddWithValue("@lastName", lastName ?? "");
                    cmd.Parameters.AddWithValue("@displayName", displayName ?? "");
                    cmd.Parameters.AddWithValue("@birthdate", birthdate.HasValue ? birthdate.Value : (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@bio", bio ?? "");
                    cmd.Parameters.AddWithValue("@profileId", profileId);
                    cmd.ExecuteNonQuery();
                }
            }
            else
            {
                // Insert new profile
                string insertQuery = @"INSERT INTO User_Profiles (user_id, first_name, last_name, display_name, birth_date, bio) 
                                       VALUES (@userId, @firstName, @lastName, @displayName, @birthdate, @bio)";
                using (MySqlCommand cmd = new MySqlCommand(insertQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId.Value);
                    cmd.Parameters.AddWithValue("@firstName", firstName ?? "");
                    cmd.Parameters.AddWithValue("@lastName", lastName ?? "");
                    cmd.Parameters.AddWithValue("@displayName", displayName ?? "");
                    cmd.Parameters.AddWithValue("@birthdate", birthdate.HasValue ? birthdate.Value : (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@bio", bio ?? "");
                    cmd.ExecuteNonQuery();
                }
            }

            return Json(new { success = true, message = "บันทึกข้อมูลเรียบร้อยแล้ว" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาด: {ex.Message}" });
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
    public IActionResult UpdateUser(string username, string email, string phone)
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบ" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // Check if username or email already exists for other users
            string checkQuery = "SELECT COUNT(*) FROM Users WHERE (username = @username OR email = @email) AND user_id != @userId";
            using (MySqlCommand cmd = new MySqlCommand(checkQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@username", username ?? "");
                cmd.Parameters.AddWithValue("@email", email ?? "");
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                if (count > 0)
                {
                    return Json(new { success = false, message = "Username หรือ Email นี้มีผู้ใช้แล้ว" });
                }
            }

            // Update user
            string updateQuery = "UPDATE Users SET username = @username, email = @email, phone = @phone WHERE user_id = @userId";
            using (MySqlCommand cmd = new MySqlCommand(updateQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@username", username ?? "");
                cmd.Parameters.AddWithValue("@email", email ?? "");
                cmd.Parameters.AddWithValue("@phone", phone ?? "");
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                cmd.ExecuteNonQuery();
            }

            return Json(new { success = true, message = "บันทึกข้อมูลเรียบร้อยแล้ว" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาด: {ex.Message}" });
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
    public IActionResult AddAddress(string receiver, string phone, string line1, string subdistrict, string district, string province, string postal, bool isDefault)
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบ" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            // If setting as default, unset other defaults
            if (isDefault)
            {
                string unsetQuery = "UPDATE User_Addresses SET is_default = 0 WHERE user_id = @userId";
                using (MySqlCommand cmd = new MySqlCommand(unsetQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId.Value);
                    cmd.ExecuteNonQuery();
                }
            }

            // Insert new address
            string insertQuery = @"INSERT INTO User_Addresses (user_id, receiver_name, phone_number, address_line1, sub_district, district, province, postal_code, is_default) 
                                   VALUES (@userId, @receiver, @phone, @line1, @subdistrict, @district, @province, @postal, @isDefault)";
            using (MySqlCommand cmd = new MySqlCommand(insertQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                cmd.Parameters.AddWithValue("@receiver", receiver ?? "");
                cmd.Parameters.AddWithValue("@phone", phone ?? "");
                cmd.Parameters.AddWithValue("@line1", line1 ?? "");
                cmd.Parameters.AddWithValue("@subdistrict", subdistrict ?? "");
                cmd.Parameters.AddWithValue("@district", district ?? "");
                cmd.Parameters.AddWithValue("@province", province ?? "");
                cmd.Parameters.AddWithValue("@postal", postal ?? "");
                cmd.Parameters.AddWithValue("@isDefault", isDefault);
                cmd.ExecuteNonQuery();
            }

            return Json(new { success = true, message = "บันทึกที่อยู่เรียบร้อยแล้ว" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาด: {ex.Message}" });
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
    public IActionResult UpdateAddress(int addressId, string receiver, string phone, string line1, string subdistrict, string district, string province, string postal, bool isDefault)
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบ" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            if (isDefault)
            {
                string unsetQuery = "UPDATE User_Addresses SET is_default = 0 WHERE user_id = @userId";
                using (MySqlCommand cmd = new MySqlCommand(unsetQuery, _connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId.Value);
                    cmd.ExecuteNonQuery();
                }
            }

            string updateQuery = @"UPDATE User_Addresses
                                   SET receiver_name = @receiver,
                                       phone_number = @phone,
                                       address_line1 = @line1,
                                       sub_district = @subdistrict,
                                       district = @district,
                                       province = @province,
                                       postal_code = @postal,
                                       is_default = @isDefault
                                   WHERE address_id = @addressId
                                     AND user_id = @userId";

            using (MySqlCommand cmd = new MySqlCommand(updateQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@receiver", receiver ?? "");
                cmd.Parameters.AddWithValue("@phone", phone ?? "");
                cmd.Parameters.AddWithValue("@line1", line1 ?? "");
                cmd.Parameters.AddWithValue("@subdistrict", subdistrict ?? "");
                cmd.Parameters.AddWithValue("@district", district ?? "");
                cmd.Parameters.AddWithValue("@province", province ?? "");
                cmd.Parameters.AddWithValue("@postal", postal ?? "");
                cmd.Parameters.AddWithValue("@isDefault", isDefault);
                cmd.Parameters.AddWithValue("@addressId", addressId);
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                {
                    return Json(new { success = false, message = "ไม่พบที่อยู่หรือไม่มีสิทธิ์แก้ไข" });
                }
            }

            return Json(new { success = true, message = "แก้ไขที่อยู่เรียบร้อยแล้ว" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาด: {ex.Message}" });
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
    public IActionResult SetDefaultAddress(int addressId)
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบ" });
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            string unsetQuery = "UPDATE User_Addresses SET is_default = 0 WHERE user_id = @userId";
            using (MySqlCommand cmd = new MySqlCommand(unsetQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                cmd.ExecuteNonQuery();
            }

            string setQuery = "UPDATE User_Addresses SET is_default = 1 WHERE address_id = @addressId AND user_id = @userId";
            using (MySqlCommand cmd = new MySqlCommand(setQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@addressId", addressId);
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                {
                    return Json(new { success = false, message = "ไม่พบที่อยู่หรือไม่มีสิทธิ์ตั้งค่า" });
                }
            }

            return Json(new { success = true, message = "ตั้งที่อยู่เริ่มต้นเรียบร้อยแล้ว" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาด: {ex.Message}" });
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
    public async Task<IActionResult> UpdateAvatar(IFormFile avatar)
    {
        int? userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
        {
            return Json(new { success = false, message = "กรุณาเข้าสู่ระบบ" });
        }

        if (avatar == null || avatar.Length == 0)
        {
            return Json(new { success = false, message = "กรุณาเลือกไฟล์รูปภาพ" });
        }

        try
        {
            // Validate file type
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var extension = Path.GetExtension(avatar.FileName).ToLower();
            if (!allowedExtensions.Contains(extension))
            {
                return Json(new { success = false, message = "รองรับเฉพาะไฟล์ JPG, PNG, GIF เท่านั้น" });
            }

            // Validate file size (max 5MB)
            if (avatar.Length > 5 * 1024 * 1024)
            {
                return Json(new { success = false, message = "ไฟล์รูปภาพต้องไม่เกิน 5MB" });
            }

            // Generate unique filename
            var fileName = $"{userId}_{Guid.NewGuid()}{extension}";
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
            Directory.CreateDirectory(uploadsFolder);
            var filePath = Path.Combine(uploadsFolder, fileName);

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await avatar.CopyToAsync(stream);
            }

            // Update database
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }

            string updateQuery = "UPDATE User_Profiles SET avatar_url = @avatarUrl WHERE user_id = @userId";
            using (MySqlCommand cmd = new MySqlCommand(updateQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@avatarUrl", $"/uploads/avatars/{fileName}");
                cmd.Parameters.AddWithValue("@userId", userId.Value);
                cmd.ExecuteNonQuery();
            }

            return Json(new { success = true, message = "อัปโหลดรูปโปรไฟล์เรียบร้อยแล้ว", avatarUrl = $"/uploads/avatars/{fileName}" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"เกิดข้อผิดพลาด: {ex.Message}" });
        }
        finally
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }

    // ฟังก์ชันแฮชรหัสผ่าน
    private string HashPassword(string password)
    {
        // ใช้ PBKDF2 สำหรับแฮชรหัสผ่านอย่างปลอดภัย
        using (var rng = RandomNumberGenerator.Create())
        {
            byte[] salt = new byte[16];
            rng.GetBytes(salt);
            
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password: password,
                salt: salt,
                iterations: 10000,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: 20
            );
            
            byte[] hashWithSalt = new byte[36];
            Array.Copy(salt, 0, hashWithSalt, 0, 16);
            Array.Copy(hash, 0, hashWithSalt, 16, 20);
            
            return Convert.ToBase64String(hashWithSalt);
        }
    }

    // ฟังก์ชันยืนยันรหัสผ่าน
    private bool VerifyPassword(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;

        try
        {
            // แปลง hash จาก Base64
            byte[] hashWithSalt = Convert.FromBase64String(hash);
            
            // แยก salt ออกจาก hash
            byte[] salt = new byte[16];
            Array.Copy(hashWithSalt, 0, salt, 0, 16);
            
            // แฮชรหัสผ่านที่ป้อนเข้ามาด้วยเกลือเดิม
            byte[] hashOfInput = Rfc2898DeriveBytes.Pbkdf2(
                password: password,
                salt: salt,
                iterations: 10000,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: 20
            );
            
            // เปรียบเทียบกับ hash ที่เก็บไว้
            for (int i = 0; i < 20; i++)
            {
                if (hashWithSalt[i + 16] != hashOfInput[i])
                {
                    return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
