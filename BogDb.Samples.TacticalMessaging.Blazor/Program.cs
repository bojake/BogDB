using BogDb.Samples.TacticalMessaging.Blazor.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<TacticalMessagingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("TacticalMessaging")
        ?? "Data Source=tactical-messaging.db"));

builder.Services.AddSingleton<TacticalMessagingGraphService>();

var app = builder.Build();

// Ensure DB is created and seeded
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TacticalMessagingDbContext>>();
    using var ctx = factory.CreateDbContext();
    ctx.Database.EnsureCreated();
    TacticalMessagingSeedData.Seed(ctx);
}

// Warm up the graph service (loads all baselines into BogDB)
var graphService = app.Services.GetRequiredService<TacticalMessagingGraphService>();
graphService.WarmUp();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<BogDb.Samples.TacticalMessaging.Blazor.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
