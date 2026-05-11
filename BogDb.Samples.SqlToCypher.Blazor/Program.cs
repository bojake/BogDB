using BogDb.Samples.SqlToCypher.Blazor.Components;
using BogDb.Samples.SqlToCypher.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Singleton graph service — seeds schema + data once, stays alive for the session.
builder.Services.AddSingleton<SqlToCypherGraphService>();

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

// Eagerly warm up the graph (schema + seed happen in the ctor).
app.Services.GetRequiredService<SqlToCypherGraphService>();

app.Run();
