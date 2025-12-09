using Foundation;
using UIKit;
using UserNotifications;

namespace IOSBackgroundDownload
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        [Export("application:handleEventsForBackgroundURLSession:completionHandler:")]
        public virtual void HandleEventsForBackgroundUrlCompletion(UIApplication application, string sessionIdentifier, Action completionHandler)
        {
            if (sessionIdentifier == "id")
            {
                _ = MainPage.BackgroundHandler; //ensure creation

                var content = new UNMutableNotificationContent
                {
                    Title = "Download complete",
                    Body = $"It worked!",
                    CategoryIdentifier = "COMPLETE",
                    Sound = UNNotificationSound.Default
                };

                var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(1, false);

                var request = UNNotificationRequest.FromIdentifier("ALERT_REQUEST", content, trigger);

                UNUserNotificationCenter.Current.AddNotificationRequest(request, (error) =>
                {
                    if (error != null)
                    {
                        Console.WriteLine("Error: " + error);
                    }
                });
            }
            completionHandler();
        }
    }
}
