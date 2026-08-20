using AsrsWarehouse.Data;
using AsrsWarehouse.Hubs;
using AsrsWarehouse.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddSignalR();

builder.Services.AddSingleton<PlcModbusService>();

builder.Services.AddHostedService<PlcMonitorService>();

builder.Services.AddDbContext<WarehouseDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("WarehouseDb")
    );
});

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}"
);

app.MapHub<WarehouseHub>(
    "/warehouseHub"
);

app.Run();