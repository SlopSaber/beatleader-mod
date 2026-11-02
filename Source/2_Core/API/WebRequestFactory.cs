using System;
using BeatLeader.API;
using BeatLeader.Utils;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BeatLeader.WebRequests {
    public static class WebRequestFactory {
        internal static readonly CookieContainer CookieContainer = new();
        private static readonly HttpClientHandler httpClientHandler = new() { CookieContainer = CookieContainer};
        private static readonly HttpClient httpClient = new(httpClientHandler);

        static WebRequestFactory() {
            ServicePointManager.DefaultConnectionLimit = 20;
            ServicePointManager.MaxServicePointIdleTime = 10_000;
        }

        public static IWebRequest<object> Send(
                HttpRequestMessage requestMessage,
                WebRequestParams? requestParams = null,
                CancellationToken token = default,
                bool waitForLogin = true
            ) {
            requestParams ??= new();
            SendRequestDelegate sendCallback = waitForLogin
                ? (message, requestToken) => SendInternalLogin(message, requestParams.ResponseCompletionOption, requestToken)
                : (message, requestToken) => SendInternal(message, requestParams.ResponseCompletionOption, requestToken);

            return new WebRequestProcessor<object>(sendCallback, requestMessage, requestParams, null, token);
        }

        public static IWebRequest<T> Send<T>(
            HttpRequestMessage requestMessage,
            IWebRequestResponseParser<T> responseParser,
            WebRequestParams? requestParams = null,
            CancellationToken token = default,
            bool waitForLogin = true
        ) {
            requestParams ??= new();
            SendRequestDelegate sendCallback = waitForLogin
                ? (message, requestToken) => SendInternalLogin(message, requestParams.ResponseCompletionOption, requestToken)
                : (message, requestToken) => SendInternal(message, requestParams.ResponseCompletionOption, requestToken);

            return new WebRequestProcessor<T>(sendCallback, requestMessage, requestParams, responseParser, token);
        }

        public static IWebRequest<object> Send(
            Func<HttpRequestMessage> requestMessageFactory,
            WebRequestParams? requestParams = null,
            CancellationToken token = default,
            bool waitForLogin = true
        ) {
            return Send(requestMessageFactory(), requestParams, token, waitForLogin);
        }

        public static IWebRequest<T> Send<T>(
            Func<HttpRequestMessage> requestMessageFactory,
            IWebRequestResponseParser<T> responseParser,
            WebRequestParams? requestParams = null,
            CancellationToken token = default,
            bool waitForLogin = true
        ) {
            return Send(requestMessageFactory(), responseParser, requestParams, token, waitForLogin);
        }

        private static Task<HttpResponseMessage?> SendInternal(
            HttpRequestMessage requestMessage,
            HttpCompletionOption completionOption,
            CancellationToken token
        ) {
            ApplyDefaultHeaders(requestMessage);
            return httpClient.SendAsync(requestMessage, completionOption, token);
        }

        private static Task<HttpResponseMessage?> SendInternalLogin(
            HttpRequestMessage requestMessage,
            HttpCompletionOption completionOption,
            CancellationToken token
        ) {
            ApplyDefaultHeaders(requestMessage);

            return Task.Run(async () => {
                var loggedIn = await Authentication.WaitLogin();
                if (!loggedIn) return null;
                return await httpClient.SendAsync(requestMessage, completionOption, token);
            }).RunCatching();
        }

        private static void ApplyDefaultHeaders(HttpRequestMessage requestMessage) {
            requestMessage.Headers.Add("User-Agent", Plugin.UserAgent);
        }
    }
}
