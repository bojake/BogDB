using BogDb.Samples.LLMBenchmarks.Blazor.Components;
using BogDb.Samples.LLMBenchmarks.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register the graph database as a singleton — seeds once on first use.
builder.Services.AddSingleton<BenchmarkGraphService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Eagerly initialize the graph so the first page load is instant.
app.Services.GetRequiredService<BenchmarkGraphService>();

app.Run();
