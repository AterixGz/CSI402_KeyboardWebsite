using Microsoft.AspNetCore.Mvc;
using KeyboardWebsiteProject.Views.Admin; // เรียกใช้ namespace ของ Model

public class AdminController : Controller
{
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
        return View();
    }
    
    public IActionResult Roles()
    {
        return View();
    }
}