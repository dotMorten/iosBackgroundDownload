# iOSBackgroundDownload Sample

Notable files:

#### `src\NSUrlSessionHandler.cs`: 
Forked from https://raw.githubusercontent.com/dotnet/macios/f1ae8365b67edea7c80e17e6d5efb66c4253b4d8/src/Foundation/NSUrlSessionHandler.cs but otherwise essentially unchanged apart from making it compile and changing namespace

#### `NSUrlSessionHandler.FileDownload.cs`
Additional members added to NSUrlSessionHandler to support FileDownload

#### `src\Platforms\iOS\AppDelegate.cs`
Added the following to resume and react to completed file download while app is in the background:
```cs
[Export("application:handleEventsForBackgroundURLSession:completionHandler:")]
public virtual void HandleEventsForBackgroundUrlCompletion(UIApplication application, string sessionIdentifier, Action completionHandler)
{
    // ...
}
```

### Usage

The new forked `NSUrlSessionHandler` has a new `DownloadFileAsync` method which will download the file and put it on disk to the provided location.
It'll return an HttpResponse just like SendAsync, but the `HttpResponse.Content` will be of type `NSUrlDownloadFileContent` which contains the path to the downloaded file.
See `src\MainPage.xaml.cs` for example
