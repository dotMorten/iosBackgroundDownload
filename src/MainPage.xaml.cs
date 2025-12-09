#if __IOS__
using Foundation;
using UserNotifications;
#endif

using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace IOSBackgroundDownload
{
    public partial class MainPage : ContentPage
    {
        int count = 0;

        public MainPage()
        {
            InitializeComponent();
            // CounterBtn.GestureRecognizers.Add(new TapGestureRecognizer
            // {
            //     Command = new Command(() => OnCounterClicked(this, EventArgs.Empty))
            // });
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            filepath = Path.Combine(Path.GetTempPath(), "DataDownload.zip");
            UpdatebuttonState();
        }
        public string filepath;

        public void UpdatebuttonState()
        {
            bool exists = File.Exists(filepath);
            CounterBtn.IsEnabled = !exists;
            Deletebtn.IsEnabled = exists;
            if (exists)
            {
                CounterBtn.Text = "File Downloaded";
            }
        }

        private async void OnCounterClicked(object? sender, EventArgs e)
        {
            try
            {
#if __IOS__
                UNUserNotificationCenter.Current.RequestAuthorization(UNAuthorizationOptions.Alert, (approved, err) =>
                {
                    bool hasNotificationsPermission = approved;
                });


                CounterBtn.Text = "Downloading...";
                
                var handler = MainPage.BackgroundHandler;
                using var client = new HttpClient(handler, false);
                
                using HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Get, "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100/dotnet-sdk-10.0.100-osx-arm64.tar.gz");                

                using var response = await handler.DownloadFileAsync(msg, filepath, (bytesWritten, totalBytesWritten, totalBytesExpectedToWrite) =>
                {
                    // Raise progress events in callback
                    Dispatcher.Dispatch(() =>
                    {
                        Progress.Text = $"Downloaded {totalBytesWritten} of {totalBytesExpectedToWrite} bytes ({(totalBytesWritten * 100) / totalBytesExpectedToWrite}%)";
                    });
                }, CancellationToken.None);

                using var content = response.Content as dotMorten.Http.NSUrlSessionHandler.NSUrlDownloadFileContent;
                Debug.WriteLine($"Downloaded file: {content?.FilePath}");
                Debug.WriteLine($"  Suggested name: {content?.SuggestedFilename}");
                
                CounterBtn.Text = "OK";

                client.Dispose();
#endif
            }
            catch(System.Exception ex)
            {
                Debugger.Break();
            }
            UpdatebuttonState();
        }

        private void Deletebtn_Clicked(object sender, EventArgs e)
        {
            if (File.Exists(filepath))
                File.Delete(filepath);
            UpdatebuttonState();
        }

#if __IOS__
        private static dotMorten.Http.NSUrlSessionHandler? _backgroundHandler;

        public static dotMorten.Http.NSUrlSessionHandler BackgroundHandler
        {
            get
            {
                if (_backgroundHandler is null)
                {
                    var config = NSUrlSessionConfiguration.CreateBackgroundSessionConfiguration("id");
                    config.SessionSendsLaunchEvents = true;
                    _backgroundHandler = new dotMorten.Http.NSUrlSessionHandler(config);
                }
                return _backgroundHandler;
            }
        }
#endif
    }
}
