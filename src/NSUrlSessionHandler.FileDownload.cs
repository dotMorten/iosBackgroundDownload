// Extensions to NSUrlSessionHandler to support backgroun file downloads
#if __IOS__
using Foundation;
using Security;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Threading;

namespace dotMorten.Http
{
    public partial class NSUrlSessionHandler
    {
        public delegate void DownloadFileProgress(long bytesWritten, long totalBytesWritten, long totalBytesExpectedToWrite);

        class InflightDownloadData : InflightData
        {
            public InflightDownloadData(string requestUrl, string? filePath, CancellationToken cancellationToken, HttpRequestMessage request, DownloadFileProgress? progress)
                : base(requestUrl, cancellationToken, request)
            {
                ProgressCallback = progress;
                FilePath = filePath;
            }

            internal DownloadFileProgress? ProgressCallback { get; }

            internal string? FilePath { get; }
        }

        public Task<HttpResponseMessage> DownloadFileAsync(string requestUri, string? filePath = null, CancellationToken cancellationToken = default) => DownloadFileAsync(new HttpRequestMessage(HttpMethod.Get, requestUri), filePath, null, cancellationToken);

        public Task<HttpResponseMessage> DownloadFileAsync(HttpRequestMessage request, string? filePath = null, CancellationToken cancellationToken = default) => DownloadFileAsync(request, filePath, null, cancellationToken);
        
        public async Task<HttpResponseMessage> DownloadFileAsync(HttpRequestMessage request, string? filePath = null, DownloadFileProgress? progressCallback = null, CancellationToken cancellationToken = default)
        {
            Volatile.Write(ref sentRequest, true);

            var nsrequest = await CreateRequest(request).ConfigureAwait(false);
            var dataTask = session.CreateDownloadTask(nsrequest);

            var inflightData = new InflightDownloadData(request.RequestUri?.AbsoluteUri!, filePath, cancellationToken, request, progressCallback);

            lock (inflightRequestsLock)
            {
                inflightRequests.Add(dataTask, inflightData);
            }

            if (dataTask.State == NSUrlSessionTaskState.Suspended)
                dataTask.Resume();

            // as per documentation: 
            // If this token is already in the canceled state, the 
            // delegate will be run immediately and synchronously.
            // Any exception the delegate generates will be 
            // propagated out of this method call.
            //
            // The execution of the register ensures that if we 
            // receive a already cancelled token or it is cancelled
            // just before this call, we will cancel the task. 
            // Other approaches are harder, since querying the state
            // of the token does not guarantee that in the next
            // execution a threads cancels it.
            cancellationToken.Register(() =>
            {
                RemoveInflightData(dataTask);
                inflightData.CompletionSource.TrySetCanceled();
            });

            return await inflightData.CompletionSource.Task.ConfigureAwait(false);
        }

        internal NSUrlSession Session => session;

        public sealed class NSUrlDownloadFileContent : HttpContent
        {
            readonly string _filePath;
            readonly string? _suggestedFilename;
            readonly bool _deleteFileOnDispose;

            internal NSUrlDownloadFileContent(string filePath, string? suggestedFilename, string? mimeType, bool deleteOnDispose)
            {
                _deleteFileOnDispose = deleteOnDispose;
                _filePath = filePath;
                _suggestedFilename = suggestedFilename;
                if (!string.IsNullOrEmpty(mimeType))
                    Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
            }

            public override string ToString() => _filePath;

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                stream = File.OpenRead(_filePath);
                return Task.CompletedTask;
            }

            protected override bool TryComputeLength(out long length)
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(_filePath);
                if (fi.Exists)
                {
                    length = fi.Length;
                    return true;
                }
                length = -1;
                return false;
            }

            public string? SuggestedFilename => _suggestedFilename;

            public string FilePath => _filePath;

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (_deleteFileOnDispose && File.Exists(_filePath))
                {
                    try
                    {
                        File.Delete(_filePath);
                    }
                    catch { }
                }
            }
        }

        partial class NSUrlSessionHandlerDelegate : INSUrlSessionDownloadDelegate
        {
            void INSUrlSessionDownloadDelegate.DidResume(NSUrlSession session, NSUrlSessionDownloadTask downloadTask, long resumeFileOffset, long expectedTotalBytes)
            {
                // TODO: Not quite sure what to do here yet, if anything. Would be called after resuming a download if the app was killed during the download.
            }

            void INSUrlSessionDownloadDelegate.DidWriteData(NSUrlSession session, NSUrlSessionDownloadTask downloadTask, long bytesWritten, long totalBytesWritten, long totalBytesExpectedToWrite)
            {
                var inflightData = GetInflightData(downloadTask) as InflightDownloadData;
                inflightData?.ProgressCallback?.Invoke(bytesWritten, totalBytesWritten, totalBytesExpectedToWrite);
            }

            void INSUrlSessionDownloadDelegate.DidFinishDownloading(NSUrlSession session, NSUrlSessionDownloadTask downloadTask, NSUrl location)
            {
                try
                {
                    DidFinishDownloadingImpl(session, downloadTask, location);
                }
                catch
                {
                    throw;
                }
            }
          
            void DidFinishDownloadingImpl(NSUrlSession session, NSUrlSessionDownloadTask dataTask, NSUrl response)
            {
                var inflight = GetInflightData(dataTask) as InflightDownloadData;

                if (inflight is null)
                {
                    return;
                }

                try
                {
                    // We must move the file, because the original will be deleted by the OS the moment we exit the delegate
                    // The temp file will be deleted once the response content disposes.
                    bool deleteOnDispose = string.IsNullOrEmpty(inflight.FilePath);
                    string filePath = deleteOnDispose ? Path.GetTempFileName() : inflight.FilePath!;
                    File.Move(response.Path!, filePath, true);
                    var content = new NSUrlDownloadFileContent(filePath, dataTask.Response?.SuggestedFilename, dataTask.Response?.MimeType, deleteOnDispose);
                    if (!inflight.Completed)
                    {
                        dataTask.Cancel();
                    }

                    var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = content,
                        RequestMessage = inflight.Request,
                    };
                    var wasRedirected = dataTask.CurrentRequest?.Url?.AbsoluteString != dataTask.OriginalRequest?.Url?.AbsoluteString;
                    if (wasRedirected)
                        httpResponse.RequestMessage.RequestUri = dataTask.CurrentRequest?.Url;
                    var urlResponse = dataTask.Response;


                    // it might be confusing that we are not using the managed CookieStore here, this is ONLY for those cookies that have been retrieved from
                    // the server via a Set-Cookie header, the managed container does not know a thing about this and apple is storing them in the native
                    // cookie container. Once we have the cookies from the response, we need to update the managed cookie container
                    var absoluteUri = urlResponse?.Url;
                    if (session.Configuration.HttpCookieStorage is not null && absoluteUri is not null)
                    {
                        var cookies = session.Configuration.HttpCookieStorage.CookiesForUrl(absoluteUri);
                        UpdateManagedCookieContainer(absoluteUri!, cookies);
                        for (var index = 0; index < cookies.Length; index++)
                        {
                            httpResponse.Headers.TryAddWithoutValidation(SetCookie, cookies[index].GetHeaderValue());
                        }
                    }

                    inflight.Response = httpResponse;

                    // We don't want to send the response back to the task just yet.  Because we want to mimic .NET behavior
                    // as much as possible.  When the response is sent back in .NET, the content stream is ready to read or the
                    // request has completed, because of this we want to send back the response in DidReceiveData or DidCompleteWithError
                    if (dataTask.State == NSUrlSessionTaskState.Suspended)
                        dataTask.Resume();

                }
                catch (Exception ex)
                {
                    inflight.CompletionSource.TrySetException(ex);
                    inflight.Stream.TrySetException(ex);

                    sessionHandler.RemoveInflightData(dataTask);
                }
            }
        }
    }
}
#endif