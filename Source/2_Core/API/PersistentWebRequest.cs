using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using JetBrains.Annotations;

namespace BeatLeader.WebRequests {
    [PublicAPI]
    public abstract class PersistentWebRequestBase {
        protected static IWebRequest<object> Send(
            string url,
            HttpMethod method,
            HttpContent? content = null,
            WebRequestParams? requestParams = null,
            Action<HttpRequestHeaders>? headersCallback = null,
            CancellationToken token = default,
            bool waitForLogin = true
        ) {
            var requestMessage = CreateAndValidateRequestMessage(url, method, content, headersCallback);
            return WebRequestFactory.Send(requestMessage, requestParams, token, waitForLogin);
        }

        protected static HttpRequestMessage CreateAndValidateRequestMessage(
            string url,
            HttpMethod method,
            HttpContent? content = null,
            Action<HttpRequestHeaders>? headersCallback = null
        ) {
            var requestMessage = new HttpRequestMessage {
                RequestUri = new Uri(url),
                Method = method
            };
            if (content is not null) requestMessage.Content = content;
            headersCallback?.Invoke(requestMessage.Headers);
            return requestMessage;
        }
    }

    public abstract class PersistentWebRequestBase<TResult, TDescriptor> : PersistentWebRequestBase
        where TDescriptor : IWebRequestResponseParser<TResult>, new() {
        private static readonly TDescriptor descriptor = new();

        protected static IWebRequest<TResult> SendRet(
            string url,
            HttpMethod method,
            HttpContent? content = null,
            WebRequestParams? requestParams = null,
            Action<HttpRequestHeaders>? headersCallback = null,
            CancellationToken token = default,
            bool waitForLogin = true
        ) {
            var requestMessage = CreateAndValidateRequestMessage(url, method, content, headersCallback);
            return WebRequestFactory.Send(requestMessage, descriptor, requestParams, token, waitForLogin);
        }
    }

    [PublicAPI]
    public abstract class PersistentSingletonWebRequestBase<T, TResult, TDescriptor> : PersistentWebRequestBase
        where T : PersistentSingletonWebRequestBase<T, TResult, TDescriptor>
        where TDescriptor : IWebRequestResponseParser<TResult>, new() {
        private static readonly TDescriptor descriptor = new();

        public static PersistentRequest<TResult> Request { get; } = new();

        protected static void SendRet(
            string url,
            HttpMethod method,
            HttpContent? content = null,
            WebRequestParams? requestParams = null,
            Action<HttpRequestHeaders>? headersCallback = null,
            TDescriptor? customParser = default,
            bool waitForLogin = true
        ) {
            var message = CreateAndValidateRequestMessage(url, method, content, headersCallback);
            Request.Send(message, customParser ?? descriptor, requestParams, waitForLogin);
        }

        private const string ObsoleteMessage = "Use the persistent Request instance instead.";

        [Obsolete(ObsoleteMessage)]
        public static void Cancel() => Request.Cancel();

        [Obsolete(ObsoleteMessage)]
        public static TResult? Result => Request.Result;

        [Obsolete(ObsoleteMessage)]
        public static RequestState RequestState => Request.RequestState;

        [Obsolete(ObsoleteMessage)]
        public static HttpStatusCode RequestStatusCode => Request.RequestStatusCode;

        [Obsolete(ObsoleteMessage)]
        public static string? FailReason => Request.FailReason;

        [Obsolete(ObsoleteMessage)]
        public static float DownloadProgress => Request.DownloadProgress;

        [Obsolete(ObsoleteMessage)]
        public static float UploadProgress => Request.UploadProgress;

        [Obsolete(ObsoleteMessage)]
        public static float OverallProgress => Request.OverallProgress;

        [Obsolete(ObsoleteMessage)]
        public static event WebRequestStateChangedDelegate<IWebRequest<TResult>>? StateChangedEvent {
            add => Request.StateChangedEvent += value;
            remove => Request.StateChangedEvent -= value;
        }

        [Obsolete(ObsoleteMessage)]
        public static event WebRequestProgressChangedDelegate<IWebRequest<TResult>>? ProgressChangedEvent {
            add => Request.ProgressChangedEvent += value;
            remove => Request.ProgressChangedEvent -= value;
        }
    }
}
