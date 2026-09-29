using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Utils;
using BeatLeader.WebRequests;
using BS_Utils.Gameplay;
using UnityEngine;
using Steamworks;

namespace BeatLeader.API {
    internal static class Authentication {
        #region Platform

        public enum AuthPlatform {
            Undefined,
            Steam,
            OculusPC
        }

        public static AuthPlatform Platform { get; private set; }

        public static void SetPlatform(AuthPlatform platform) {
            Platform = platform;
        }

        public static async Task<string?> PlatformTicket() {
            await GetUserInfo.GetUserAsync();

            var platformUserModel = Resources
                .FindObjectsOfTypeAll<PlatformLeaderboardsModel>()
                .Select(l => l._platform)
                .Last(x => x != null);

            var userInfo = new UserInfo(platformUserModel.key switch {
                "steam" => UserInfo.Platform.Steam,
                "oculus" => UserInfo.Platform.Oculus,
                "oculus-mock" => UserInfo.Platform.Oculus,
                "mock" => UserInfo.Platform.Test,
                _ => throw new NotSupportedException($"Unsupported platform: {platformUserModel.key}"),
            }, platformUserModel.user.userId.ToString(), platformUserModel.user.displayName);

            var tokenProvider = new PlatformAuthenticationTokenProvider(platformUserModel, userInfo);

            return Platform switch {
                AuthPlatform.Steam    => (await tokenProvider.GetXPlatformAccessToken(CancellationToken.None)).token,
                AuthPlatform.OculusPC => (await tokenProvider.GetXPlatformAccessToken(CancellationToken.None)).token,
                _                     => throw new InvalidOperationException($"Unsupported authentication platform: {Platform}")
            };
        }

        #endregion

        #region Login

        private static TaskCompletionSource<bool> _taskSource = new();
        private static bool _signedIn;
        private static Task? _loginTask;

        public static void ResetLogin() {
            WebRequestFactory.CookieContainer.SetCookies(new Uri(BLConstants.BEATLEADER_API_URL), "");
            _signedIn = false;
            // Preserve existing waiters while a login attempt is still running.
            if (_taskSource.Task.IsCompleted) _taskSource = new();
        }

        public static Task<bool> WaitLogin() {
            return _taskSource.Task;
        }

        public static Task Login() {
            if (_signedIn) return Task.CompletedTask;
            if (_loginTask is { IsCompleted: false }) return _loginTask;
            if (_taskSource.Task.IsCompleted) _taskSource = new();
            return _loginTask = CompleteLogin();
        }

        private static async Task CompleteLogin() {
            try {
                await LoginInternal();
            } catch (Exception exception) {
                Plugin.Log.Warn($"Login failed: {exception.Message}");
            } finally {
                _taskSource.TrySetResult(_signedIn);
            }
        }

        private static async Task LoginInternal() {

            if (!TryGetPlatformProvider(Platform, out var provider)) {
                Plugin.Log.Debug("Login failed! Unknown platform");
                return;
            }

            IWebRequest<object> result;
            if (Platform == AuthPlatform.Steam) {
                result = await SendSteamLogin(provider!);
            } else {
                var authToken = await PlatformTicket();
                if (authToken == null) {
                    Plugin.Log.Debug("Login failed! No auth token");
                    return;
                }
                result = await AuthRequest.Send(authToken, provider!).Join();
            }

            switch ((int)result.RequestStatusCode) {
                case 200:
                    Plugin.Log.Info("Login successful!");
                    _signedIn = true;
                    _taskSource.TrySetResult(true);
                    break;
                
                case BLConstants.MaintenanceStatus:
                    Plugin.Log.Debug("Login failed! Maintenance");
                    break;
                
                default:
                    Plugin.Log.Debug($"Login failed! status: {result.RequestStatusCode} error: {result.FailReason}");
                    break;
            }
        }

        private static async Task<IWebRequest<object>> SendSteamLogin(string provider) {
            var ticket = HAuthTicket.Invalid;
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var callback = Callback<GetTicketForWebApiResponse_t>.Create(response => {
                if (response.m_hAuthTicket != ticket) return;
                if (response.m_eResult != EResult.k_EResultOK ||
                    response.m_cubTicket <= 0 || response.m_cubTicket > response.m_rgubTicket.Length) {
                    completion.TrySetException(new InvalidOperationException($"Steam ticket request failed: {response.m_eResult}"));
                    return;
                }
                completion.TrySetResult(BitConverter.ToString(response.m_rgubTicket, 0, response.m_cubTicket).Replace("-", ""));
            });
            try {
                // BeatLeader's Steam verifier does not specify an identity. Do not
                // reuse the game's ticket, which belongs to the Meta backend.
                ticket = SteamUser.GetAuthTicketForWebApi(string.Empty);
                if (ticket == HAuthTicket.Invalid) throw new InvalidOperationException("Steam did not issue an authentication ticket");
                if (await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(15))) != completion.Task) {
                    throw new TimeoutException("Steam authentication ticket timed out");
                }
                return await AuthRequest.Send(await completion.Task, provider).Join();
            } finally {
                // Keep the ticket valid until the server has verified it.
                if (ticket != HAuthTicket.Invalid) SteamUser.CancelAuthTicket(ticket);
            }
        }

        private static bool TryGetPlatformProvider(AuthPlatform platform, out string? provider) {
            switch (platform) {
                case AuthPlatform.Steam:
                    provider = "steamTicket";
                    return true;
                
                case AuthPlatform.OculusPC:
                    provider = "oculusTicket";
                    return true;
                
                case AuthPlatform.Undefined:
                default:
                    provider = null;
                    return false;
            }
        }

        #endregion
    }
}
