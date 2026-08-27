using AsrsWarehouse.Data;
using AsrsWarehouse.Hubs;
using AsrsWarehouse.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Railway assigns PORT at runtime. Locally, the Docker image defaults to 8080.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddControllersWithViews();

builder.Services.AddSignalR();

builder.Services.Configure<PlcOptions>(builder.Configuration.GetSection("Plc"));
builder.Services.Configure<CameraOptions>(builder.Configuration.GetSection("Camera"));
builder.Services.Configure<StoragePolicyOptions>(builder.Configuration.GetSection("StoragePolicy"));

builder.Services.AddSingleton<PlcModbusService>();
#if CLOUD_BUILD
builder.Services.AddSingleton<IQrCameraService, CloudUnavailableCameraService>();
builder.Services.AddSingleton<IQrCodeReaderService, CloudUnavailableQrCodeReaderService>();
#else
builder.Services.AddSingleton<IQrCameraService, ImvGigECameraService>();
builder.Services.AddSingleton<IQrCodeReaderService, QrCodeReaderService>();
#endif
builder.Services.AddSingleton<HardwareStatusService>();
builder.Services.AddScoped<InboundScanService>();
builder.Services.AddScoped<WarehouseWorkflowService>();

#if !CLOUD_BUILD
builder.Services.AddHostedService<PlcMonitorService>();
#endif

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

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
