using DataVault.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Session for JWT token + vault password
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opts =>
{
    opts.IdleTimeout = TimeSpan.FromHours(12);
    opts.Cookie.HttpOnly = true;
    opts.Cookie.IsEssential = true;
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    opts.Cookie.Name = ".DataVault.Session";
});

builder.Services.AddHttpContextAccessor();

// API HTTP Client
builder.Services.AddHttpClient<VaultApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7100/");
    client.Timeout = TimeSpan.FromSeconds(60);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.MapControllers();

app.Run();
