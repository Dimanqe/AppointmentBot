#region

using AppointmentBot.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

#endregion

namespace AppointmentBot;

public class AdminBotService : BackgroundService
{
    private readonly AdminBotClient _botClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public AdminBotService(AdminBotClient botClient, IServiceScopeFactory scopeFactory)
    {
        _botClient = botClient;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ✅ Зарегистрируем команды /start и /help при запуске
        await _botClient.Client.SetMyCommandsAsync(new[]
        {
            new BotCommand { Command = "start", Description = "Запустить бота" },
            new BotCommand { Command = "help", Description = "Помощь" }
        }, cancellationToken: stoppingToken);

        _botClient.Client.StartReceiving(
            async (botClient, update, ct) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var controller = scope.ServiceProvider.GetRequiredService<AdminBotController>();

                try
                {
                    if (update.Type == UpdateType.Message && update.Message?.Text != null)
                        await controller.HandleAdminMessage(update.Message, ct);
                    else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
                        await controller.Handle(update.CallbackQuery, ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error handling admin update: {ex.Message}");
                }
            },
            async (botClient, exception, ct) =>
            {
                var errorMessage = exception switch
                {
                    ApiRequestException apiEx => $"Telegram API Error: [{apiEx.ErrorCode}] {apiEx.Message}",
                    _ => exception.ToString()
                };
                Console.WriteLine(errorMessage);
            },
            new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            stoppingToken
        );

        Console.WriteLine("✅ Admin bot started.");
        await Task.Delay(-1, stoppingToken); // держим сервис активным
    }
}