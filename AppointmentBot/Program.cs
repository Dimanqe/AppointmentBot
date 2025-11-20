#region

using System.Text;
using AppointmentBot.Configuration;
using AppointmentBot.Controllers;
using AppointmentBot.Data;
using AppointmentBot.Repositories;
using AppointmentBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

#endregion

namespace AppointmentBot;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var host = CreateHostBuilder(args).Build();

        Console.WriteLine("✅ Telegram bots service started...");
        await host.RunAsync();
        Console.WriteLine("🛑 Telegram bots service stopped.");
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        DateTime now = TimeZoneInfo.ConvertTime(DateTime.Now, tz);

    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                var appSettings = BuildAppSettings();
                services.AddSingleton(appSettings);

                // --- Database setup ---
                services.AddDbContext<BotDbContext>(options =>
                    options.UseNpgsql(appSettings.PostgresConnection));

                // --- Telegram bot clients ---
                services.AddSingleton<UserBotClient>(_ => new UserBotClient(appSettings.UserBotToken));
                services.AddSingleton<AdminBotClient>(_ => new AdminBotClient(appSettings.AdminBotToken));

                // --- Repositories ---

                //services.AddScoped<BotRepository>(sp =>
                //{
                //    var db = sp.GetRequiredService<BotDbContext>();
                //    var adminBot = sp.GetRequiredService<AdminBotClient>();
                //    var userBot = sp.GetRequiredService<UserBotClient>();
                //    var adminRepository = sp.GetRequiredService<AdminRepository>();
                //    return new BotRepository(db, adminBot, userBot);
                //});

                //services.AddScoped<AdminRepository>(sp =>
                //{
                //    var db = sp.GetRequiredService<BotDbContext>();
                //    var adminBot = sp.GetRequiredService<AdminBotClient>();
                //    var userBot = sp.GetRequiredService<UserBotClient>();
                //    return new AdminRepository(db, adminBot, userBot);
                //});

                services.AddScoped<AdminRepository>();
                services.AddScoped<BotRepository>();

                // --- Controllers ---
                services.AddTransient<TextMessageController>();
                services.AddTransient<InlineKeyboardController>();
                services.AddTransient<AdminBotController>();

                // --- Session storage ---
                services.AddSingleton<IUserSessionStorage, MemoryUserSessionStorage>();
                services.AddSingleton<IAdminSessionStorage, AdminSessionStorage>();

                // --- Background bot services ---
                services.AddHostedService<UserBotService>();
                services.AddHostedService<AdminBotService>();
                services.AddHostedService<ReminderService>();

            });
    }

    private static AppSettings BuildAppSettings()
    {
        return new AppSettings
        {
            UserBotToken = Environment.GetEnvironmentVariable("UserBotToken"),
            AdminBotToken = Environment.GetEnvironmentVariable("AdminBotToken"),
            PostgresConnection = Environment.GetEnvironmentVariable("PostgresConnection")
                                 ?? "Host=localhost;Port=5432;Database=botdb;Username=postgres;Password=postgres;"
        };
    }
}