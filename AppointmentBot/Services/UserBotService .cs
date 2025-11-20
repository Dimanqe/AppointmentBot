#region

using AppointmentBot.Clients;
using AppointmentBot.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

#endregion

namespace AppointmentBot.Services;

public class UserBotService : BackgroundService
{
    private readonly UserBotClient _botClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public UserBotService(UserBotClient botClient, IServiceScopeFactory scopeFactory)
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
            async (bot, update, ct) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var textController = scope.ServiceProvider.GetRequiredService<TextMessageController>();
                var inlineController = scope.ServiceProvider.GetRequiredService<InlineKeyboardController>();

                if (update.Message != null)
                    await textController.Handle(update.Message, ct);
                else if (update.CallbackQuery != null)
                    await inlineController.Handle(update.CallbackQuery, ct);
            },
            async (bot, ex, ct) => { Console.WriteLine($"User bot polling error: {ex.Message}"); },
            new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            stoppingToken
        );

        Console.WriteLine("✅ User bot started.");
        await Task.Delay(-1, stoppingToken); // держим сервис активным
    }
}