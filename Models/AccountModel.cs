namespace KeyboardWebsiteProject.Models;

public class User
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public string Phone { get; set; }
}

public class LoginViewModel
{
    public string Email { get; set; }
    public string Password { get; set; }
    public bool RememberMe { get; set; }
}

public class RegisterViewModel
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string ConfirmPassword { get; set; }
    public bool MarketingOptIn { get; set; }
    public bool AgreeToTerms { get; set; }
}

public class UserProfile
{
    public int ProfileId { get; set; }
    public int UserId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string DisplayName { get; set; }
    public DateTime? BirthDate { get; set; }
    public string AvatarUrl { get; set; }
    public string Bio { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class Order
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    public DateTime OrderDate { get; set; }
    public int ItemCount { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; }
}

public class ProfileViewModel
{
    public User User { get; set; }
    public UserProfile Profile { get; set; }
    public int OrderCount { get; set; }
    public int WishCount { get; set; }
    public int Points { get; set; }
    public List<Order> RecentOrders { get; set; } = new List<Order>();
}
