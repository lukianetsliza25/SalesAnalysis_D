// SalesAnalysis.Web/Program.cs (Фрагмент конфігурації)
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SalesAnalysis.Data;
using SalesAnalysis.Data.Services;
using SalesAnalysis.ML.Services;

var builder = WebApplication.CreateBuilder(args);

// --- 1. Налаштування підключення до бази даних (PostgreSQL) ---
builder.Services.AddDbContext<SalesDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// --- 2. Налаштування Identity для авторизації ---
builder.Services.AddIdentity<IdentityUser<int>, IdentityRole<int>>(options => {
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
})
.AddEntityFrameworkStores<SalesDbContext>();

builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = "/Account/Auth";
    options.LogoutPath = "/Account/Auth";
});

// --- 3. Реєстрація Сервісів (Dependency Injection) ---
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<AnalysisService>();
builder.Services.AddSingleton<ClusteringService>();
builder.Services.AddSingleton<PredictionService>();

builder.Services.AddControllersWithViews();

// --- 4. Налаштування лімітів завантаження великих CSV-файлів (до 100 МБ) ---
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104857600; // 100 MB
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100_000_000;
});

// Сумісність точок часу DateTime для PostgreSQL
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var app = builder.Build();

// Гарантоване створення/міграція БД при старті
CreateDbIfNotExists(app);

// --- 5. Конвеєр обробки запитів (Middleware) ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Стартовий маршрут веде на сторінку авторизації
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Auth}/{id?}");

app.Run();

// --- ДОПОМІЖНИЙ МЕТОД ДЛЯ АВТОМІГРАЦІЇ ---
void CreateDbIfNotExists(IHost host)
{
    using (var scope = host.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<SalesDbContext>();
            context.Database.Migrate(); // Автоматично створить базу SalesAnalysisDb в Postgres
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred creating or migrating the DB.");
        }
    }
}