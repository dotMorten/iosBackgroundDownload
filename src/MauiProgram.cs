using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

namespace IOSBackgroundDownload
{
    public static class MauiProgram
    {
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
#if __IOS__
            builder.ConfigureLifecycleEvents(events =>
            {
                events.AddiOS(ios =>
                {
                    ios.AddEvent("HandleEventsForBackgroundUrl", () =>
                    {

                    });
                    ios.AddEvent("BackgroundUrl", () =>
                    {

                    });
                });
            });
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
