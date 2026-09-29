using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Utils;
using IPA.Utilities.Async;
using JetBrains.Annotations;

namespace BeatLeader.WebRequests {
    /// <summary>Owns the latest request while retaining subscribers across replacements.</summary>
    [PublicAPI]
    public sealed class PersistentRequest<TResult> : IWebRequest<TResult> {
        private readonly object _gate = new();
        private IWebRequest<TResult>? _current;
        private CancellationTokenSource? _tokenSource;
        private string? _failureReason;
        private int _generation;
        private event WebRequestStateChangedDelegate<IWebRequest<TResult>>? _stateChanged;
        private event WebRequestProgressChangedDelegate<IWebRequest<TResult>>? _progressChanged;

        public TResult? Result => _current is { } request ? request.Result : default;
        public RequestState RequestState => _current?.RequestState
            ?? (_failureReason == null ? RequestState.Uninitialized : RequestState.Failed);
        public HttpStatusCode RequestStatusCode => _current?.RequestStatusCode ?? default;
        public string? FailReason => _current?.FailReason ?? _failureReason;
        public HttpContentHeaders? Headers => _current?.Headers;
        public float DownloadProgress => _current?.DownloadProgress ?? 0;
        public float UploadProgress => _current?.UploadProgress ?? 0;
        public float OverallProgress => _current?.OverallProgress ?? 0;

        public event WebRequestStateChangedDelegate<IWebRequest<TResult>>? StateChangedEvent {
            add {
                lock (_gate) {
                    _stateChanged += value;
                    var request = _current ?? this;
                    value?.Invoke(request, request.RequestState, request.FailReason);
                }
            }
            remove { lock (_gate) _stateChanged -= value; }
        }

        public event WebRequestProgressChangedDelegate<IWebRequest<TResult>>? ProgressChangedEvent {
            add {
                lock (_gate) {
                    _progressChanged += value;
                    if (_current is { } request) {
                        value?.Invoke(request, request.DownloadProgress, request.UploadProgress, request.OverallProgress);
                    }
                }
            }
            remove { lock (_gate) _progressChanged -= value; }
        }

        internal void Send(HttpRequestMessage message, IWebRequestResponseParser<TResult> parser,
            WebRequestParams? parameters, bool waitForLogin) {
            lock (_gate) {
                RetireCurrent();
                _failureReason = null;
                _tokenSource = new CancellationTokenSource();
                try {
                    var request = WebRequestFactory.Send(message, parser, parameters, _tokenSource.Token, waitForLogin);
                    _current = request;
                    request.StateChangedEvent += OnStateChanged;
                    request.ProgressChangedEvent += OnProgressChanged;
                    OnStateChanged(request, request.RequestState, request.FailReason);
                } catch (Exception exception) {
                    _tokenSource.Dispose();
                    _tokenSource = null;
                    message.Dispose();
                    _failureReason = "Unable to start request";
                    Plugin.Log.Error(exception);
                    OnStateChanged(this, RequestState.Failed, _failureReason);
                }
            }
        }

        internal void Fail(string reason) {
            lock (_gate) {
                RetireCurrent();
                _failureReason = reason;
                OnStateChanged(this, RequestState.Failed, reason);
            }
        }

        private void OnStateChanged(IWebRequest<TResult> request, RequestState state, string? reason) {
            lock (_gate) {
                if (!ReferenceEquals(request, _current ?? this)) return;
                var generation = _generation;
                _ = UnityMainThreadTaskScheduler.Factory.StartNew(() => {
                    lock (_gate) {
                        if (generation != _generation) return;
                        _stateChanged?.Invoke(request, state, reason);
                    }
                }).RunCatching();
            }
        }

        private void OnProgressChanged(IWebRequest<TResult> request, float download, float upload, float overall) {
            lock (_gate) {
                if (!ReferenceEquals(request, _current)) return;
                var generation = _generation;
                _ = UnityMainThreadTaskScheduler.Factory.StartNew(() => {
                    lock (_gate) {
                        if (generation != _generation) return;
                        _progressChanged?.Invoke(request, download, upload, overall);
                    }
                }).RunCatching();
            }
        }

        public Task<IWebRequest<TResult>> Join() {
            lock (_gate) return _current?.Join() ?? Task.FromResult<IWebRequest<TResult>>(this);
        }

        public void Cancel() {
            lock (_gate) _tokenSource?.Cancel();
        }

        public void Dispose() {
            lock (_gate) {
                RetireCurrent();
                _failureReason = null;
                _stateChanged = null;
                _progressChanged = null;
            }
        }

        private void RetireCurrent() {
            _generation++;
            var request = _current;
            var source = _tokenSource;
            _current = null;
            _tokenSource = null;
            if (request == null) {
                source?.Dispose();
                return;
            }
            request.StateChangedEvent -= OnStateChanged;
            request.ProgressChangedEvent -= OnProgressChanged;
            source?.Cancel();
            _ = DisposeWhenFinished(request, source).RunCatching();
        }

        private static async Task DisposeWhenFinished(IWebRequest<TResult> request, CancellationTokenSource? source) {
            try {
                await request.Join().ConfigureAwait(false);
            } finally {
                request.Dispose();
                source?.Dispose();
            }
        }
    }
}
