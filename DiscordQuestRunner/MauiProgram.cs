using DiscordQuestRunner.Services;
using Microsoft.Extensions.Logging;

namespace DiscordQuestRunner
{
    /// <summary>
    /// Registers application services, fonts, and logging for the MAUI host.
    /// </summary>
    public static class MauiProgram
    {
        /// <summary>
        /// Builds the shared MAUI application container.
        /// </summary>
        /// <returns>The configured <see cref="MauiApp"/>.</returns>
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton<DiscordService>();
            builder.Services.AddTransient<Pages.QuestRunnerPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
