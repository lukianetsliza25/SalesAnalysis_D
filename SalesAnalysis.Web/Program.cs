// SalesAnalysis.Web/Program.cs (Фрагмент конфігурації)
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesAnalysis.Data;
using SalesAnalysis.Data.Services;
using SalesAnalysis.ML.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Налаштування підключення до бази даних (PostgreSQL)
builder.Services.AddDbContext<SalesDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// 2. Налаштування Identity для авторизації користувачів (ізоляція даних за UserId)
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

// 3. Реєстрація Сервісів інтелектуального ядра (Dependency Injection)
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<AnalysisService>();
builder.Services.AddSingleton<ClusteringService>();
builder.Services.AddSingleton<PredictionService>();

builder.Services.AddControllersWithViews();

// 4. Оптимізація вебсервера Kestrel під завантаження великих транзакційних CSV-масивів
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 104857600; // Строго 100 МБ на рівні HTTP-сервера
});

// Забезпечення сумісності форматів дат DateTime із вимогами СКБД PostgreSQL
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var app = builder.Build();

// Автоматичний запуск міграцій та створення бази даних SalesAnalysisDb при старті
CreateDbIfNotExists(app);

// 5. Конвеєр обробки запитів (Middleware)
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Стартовий маршрут системи (веде на сторінку авторизації)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Auth}/{id?}");

app.Run();

// Допоміжний метод для автоматичного розгортання структури БД в PostgreSQL
void CreateDbIfNotExists(IHost host)
{
    using (var scope = host.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<SalesDbContext>();
            context.Database.Migrate();
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Помилка автоматичного розгортання міграцій СКБД PostgreSQL.");
        }
    }
}