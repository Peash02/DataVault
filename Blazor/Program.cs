using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DataVault.Blazor.Services;
using Blazored.LocalStorage;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<DataVault.Blazor.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// API HTTP Client
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7100/")
});

// Services
builder.Services.AddScoped<VaultApiService>();
builder.Services.AddBlazoredLocalStorage();

await builder.Build().RunAsync();
