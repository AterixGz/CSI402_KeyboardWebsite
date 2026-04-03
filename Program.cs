using CloudinaryDotNet;
using MySql.Data.MySqlClient;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddTransient<MySqlConnection>(_ => new MySqlConnection(connectionString));
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var account = new Account(
        config["Cloudinary:CloudName"] ?? string.Empty,
        config["Cloudinary:ApiKey"] ?? string.Empty,
        config["Cloudinary:ApiSecret"] ?? string.Empty
    );
    return new Cloudinary(account);
});

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
