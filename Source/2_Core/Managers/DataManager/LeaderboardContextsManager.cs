using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.API;
using BeatLeader.Models;
using BeatLeader.Utils;
using Reactive.Components;

namespace BeatLeader.DataManager {
    public static class LeaderboardContextsManager {
        public static void Initialize() {
            ContextsRequest.Send().StateChangedEvent += HandleStateChanged;
        }

        private static void HandleStateChanged(WebRequests.IWebRequest<ServerScoresContext[]> instance, WebRequests.RequestState state, string? failReason) {
            switch (state) {
                case WebRequests.RequestState.Finished: 
                    var tasks = new List<Task>(instance.Result!.Length);

                    var contexts = instance.Result.Select(s => {
                        var context = new ScoresContext {
                            Id = s.Id,
                            Icon = BundleLoader.GeneralContextIcon,
                            Name = s.Name,
                            Description = s.Description,
                            Key = s.Key,
                        };

                        tasks.Add(LoadIconAsync(s.Icon, context));

                        return context;
                    }).ToArray();

                    ScoresContexts.allContexts = contexts;
                    _ = NotifyWhenIconsLoadedAsync(tasks, contexts).RunCatching();

                    break;

                case WebRequests.RequestState.Failed:
                    Plugin.Log.Debug($"Contexts retrieval failed! {failReason}");
                    break;
            }
        }

        private static async Task NotifyWhenIconsLoadedAsync(List<Task> tasks, ScoresContext[] contexts) {
            try {
                await Task.WhenAll(tasks);
            } finally {
                // Failed icon loads still publish the current list with its fallback icons.
                if (ReferenceEquals(ScoresContexts.allContexts, contexts)) {
                    PluginConfig.NotifyScoresContextListWasChanged();
                }
            }
        }

        private static async Task LoadIconAsync(string url, ScoresContext context) {
            var image = await ImageLoader.LoadImage(url, CancellationToken.None);

            if (image != null) {
                context.Icon = image.Sprite;
            } else {
                Plugin.Log.Error("Failed to load context icon");
            }
        }
    }
}
