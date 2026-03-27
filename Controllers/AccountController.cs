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
            string checkQuery = "SELECT user_id, password_hash FROM Users WHERE email = @email OR username = @email";
            string passwordHash = null;
            int userId = 0;

            using (MySqlCommand cmd = new MySqlCommand(checkQuery, _connection))
            {
                cmd.Parameters.AddWithValue("@email", model.Email);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        userId = reader.GetInt32("user_id");
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
            // ในที่นี้เราใช้ session เก็บ user_id
            HttpContext.Session.SetInt32("UserId", userId);
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

            // เพิ่มผู้ใช้ใหม่ (role_id = 2 สำหรับ customer ทั่วไป)
            string insertUserQuery = @"INSERT INTO Users (role_id, username, email, password_hash) 
                                      VALUES (2, @username, @email, @passwordHash)";
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
        return View();
    }

    public IActionResult Logout()
    {
        // ล้าง session
        HttpContext.Session.Clear();
        
        // Redirect ไปยัง Home page
        return RedirectToAction("Index", "Home");
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
    private bool VerifyPassword(string password, string hash)
    {
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
