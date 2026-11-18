using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PtixiakiReservations.Configurations;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using PtixiakiReservations.Seeders;
using PtixiakiReservations.Services;
using Serilog;
using Serilog.Exceptions;
using Serilog.Formatting.Elasticsearch;
using Serilog.Settings.Configuration;
using Serilog.Sinks.SystemConsole.Themes;
using Serilog.Sinks.Elasticsearch;
using System;
using Microsoft.AspNetCore.Authorization;
using PtixiakiReservations.Hubs;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsEnvironment("EF"))
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    var efApp = builder.Build();
    return 0;
}


var elasticUrl = builder.Configuration["ElasticSettings:Url"] ?? "http://elasticsearch:9200";
var indexPrefix = builder.Configuration["ElasticSettings:DefaultIndex"] ?? "events";

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithExceptionDetails()
    .WriteTo.Console()
    .WriteTo.Debug()
    .WriteTo.Elasticsearch(
        new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(new Uri(elasticUrl))
        {
            IndexFormat = $"{indexPrefix}-logs-{DateTime.UtcNow:yyyy-MM}",
            AutoRegisterTemplate = true,
            OverwriteTemplate = true,
            DetectElasticsearchVersion = true,
            AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7,
            NumberOfShards = 1,
            NumberOfReplicas = 0,
            ModifyConnectionSettings = x =>
                x.BasicAuthentication("", "").ServerCertificateValidationCallback((o, c, ch, e) => true),
            CustomFormatter = new ElasticsearchJsonFormatter(),
            EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog |
                               EmitEventFailureHandling.WriteToFailureSink |
                               EmitEventFailureHandling.RaiseCallback
        })
    .CreateLogger();

builder.Host.UseSerilog();

try
{
    Log.Information("Starting web application");

    
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
    {
        var postgresConnection = builder.Configuration.GetConnectionString("DefaultConnection");

        Log.Information("Using PostgreSQL database");
        options.UseNpgsql(postgresConnection);
    });


    builder.Services.AddSignalR();

    builder.Services.AddHttpClient<IAILayoutService, AILayoutService>();

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                   ForwardedHeaders.XForwardedProto |
                                   ForwardedHeaders.XForwardedHost;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            options.Stores.MaxLengthForKeys = 128;

            options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultUI()
        .AddDefaultTokenProviders();

    builder.Services.AddControllersWithViews().AddXmlSerializerFormatters();
    builder.Services.AddRazorPages();

    builder.Services.AddMemoryCache();

    builder.Services.Configure<ElasticSettings>(builder.Configuration.GetSection("ElasticSettings"));
    builder.Services.AddSingleton<IElasticSearch, ElasticSearchService>();

    builder.Services.AddTransient<IEmailService, EmailService>();

    builder.Services.AddScoped<IEventGeneratorService, EventGeneratorService>();

    builder.Services.AddMvc();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.LoginPath = "/Identity/Account/Login";
        options.LogoutPath = "/Identity/Account/Logout";
        options.AccessDeniedPath = "/Identity/Account/AccessDenied";
        options.ReturnUrlParameter = "returnUrl";
        options.SlidingExpiration = true;

        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    });

    builder.Services.AddSignalR();
    builder.Services.AddScoped<SearchSyncService>();
    builder.Services.AddHostedService<ElasticsearchStartupService>();

    var app = builder.Build();


if (!builder.Environment.IsEnvironment("EF"))
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        context.Database.EnsureCreated();
        try{
            await PtixiakiReservations.Seeders.RoleSeeder.SeedRolesAndAdminAsync(services);
            Log.Information("Ensuring database is created...");
            await context.Database.EnsureCreatedAsync();
            Log.Information("Database created successfully");

            Log.Information("Seeding database...");
            DataSeeder.BasicDataSeed(context, userManager, roleManager);
            Log.Information("Database seeded successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while seeding the database");
        }
    }
}

    app.UseForwardedHeaders();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
        app.UseMigrationsEndPoint();
    }
    else
    {
        app.UseExceptionHandler("/Home/Error");
    }


    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=31536000";
        }
    });
    app.MapStaticAssets();

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Events}/{action=HomePage}/{id?}");
    app.MapRazorPages();

    app.Use(async (context, next) =>
    {
        if (context.Request.Path == "/")
        {
            context.Response.Redirect("/Events/HomePage");
            return;
        }

        await next();
    });

    app.MapHub<EventSphere.Hubs.SupportHub>("/supporthub");
    app.MapHub<SeatHub>("/seathub");

    app.Run();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}