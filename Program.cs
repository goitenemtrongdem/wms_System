// using AsrsWarehouse.Data;
// using Microsoft.EntityFrameworkCore;

// var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddControllersWithViews();
// builder.Services.AddDbContext<WarehouseDbContext>(options =>
//     options.UseSqlServer(builder.Configuration.GetConnectionString("WarehouseDb")));

// var app = builder.Build();

// if (!app.Environment.IsDevelopment())
// {
//     app.UseExceptionHandler("/Home/Error");
//     app.UseHsts();
// }

// app.UseHttpsRedirection();
// app.UseStaticFiles();
// app.UseRouting();
// app.MapControllers();
// app.MapControllerRoute(
//     name: "default",
//     pattern: "{controller=Dashboard}/{action=Index}/{id?}");

// app.Run();


using AsrsWarehouse.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

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

app.Run();