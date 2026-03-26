namespace KeyboardWebsiteProject.Models;

public class User
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
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
