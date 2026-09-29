using System.Collections.Generic;
using BeatLeader.Models;
using BeatLeader.Models.AbstractReplay;
using UnityEngine;
using Zenject;

namespace BeatLeader.Replayer {
    internal class ReplayBeatmapData : IInitializable, IReplayBeatmapData {
        #region Injection

        [Inject] private readonly IReadonlyBeatmapData _beatmapData = null!;

        #endregion

        #region ReplayBeatmapData

        public IReadOnlyCollection<NoteData> NoteDatas {
            get {
                Initialize();
                return _generatedNoteDatas;
            }
        }

        public int FindNoteDataForEvent(NoteEvent noteEvent, IReplayNoteComparator noteComparator, int startIndex, out NoteData? noteData) {
            Initialize();
            const float tolerance = 1e-3f;
            var earliestTime = noteEvent.spawnTime - tolerance;
            var low = 0;
            var high = _generatedNoteDatas.Count;
            if (startIndex >= 0 && startIndex < high && _generatedNoteDatas[startIndex].time < earliestTime) {
                low = startIndex + 1;
            }
            while (low < high) {
                var middle = low + (high - low) / 2;
                if (_generatedNoteDatas[middle].time < earliestTime) low = middle + 1;
                else high = middle;
            }

            // Keep the first candidate as the cursor so chords may arrive in either cut order.
            // A backwards event starts a fresh search instead of skipping earlier notes.
            var index = low;
            for (var i = index; i < _generatedNoteDatas.Count; i++) {
                var data = _generatedNoteDatas[i];
                if (data.time > noteEvent.spawnTime + tolerance) break;
                if (Mathf.Abs(noteEvent.spawnTime - data.time) >= tolerance) continue;
                if (!noteComparator.Compare(noteEvent, data)) continue;
                noteData = data;
                return index;
            }
            Plugin.Log.Error("[Replayer] Failed to acquire NoteData with id: " + noteEvent.noteId);
            Plugin.Log.Warn("[Replayer] The replay seems to be broken!");

            noteData = null;
            return index;
        }

        #endregion

        #region Setup

        private bool _isInitialized;

        public void Initialize() {
            if (_isInitialized) return;
            var beatmapItems = _beatmapData.allBeatmapDataItems;
            var noteDataList = CreateSortedNoteDataList(beatmapItems);
            _generatedNoteDatas.AddRange(noteDataList);
            _isInitialized = true;
        }

        #endregion

        #region MoteData Handling

        private class BeatmapDataItemsComparer : IComparer<BeatmapDataItem> {
            public int Compare(BeatmapDataItem left, BeatmapDataItem right) {
                return left.time > right.time ? 1 : left.time < right.time ? -1 : 0;
            }
        }

        private static readonly BeatmapDataItemsComparer beatmapItemsComparer = new();
        private readonly List<NoteData> _generatedNoteDatas = new();

        private static IEnumerable<NoteData> CreateSortedNoteDataList(IEnumerable<BeatmapDataItem> items) {
            var result = new List<NoteData>();
            foreach (var item in items) {
                switch (item) {
                    case NoteData data:
                        result.Add(data);
                        break;
                    case SliderData sliderData:
                        ConvertAndAddSliderData(sliderData, result);
                        break;
                }
            }
            result.Sort(beatmapItemsComparer);
            return result;

            static void ConvertAndAddSliderData(SliderData sliderData, ICollection<NoteData> list) {
                var sliceCount = sliderData.sliceCount;
                for (var i = 1; i < sliceCount; ++i) {
                    var lineIndex = i < sliceCount - 1 ? sliderData.headLineIndex : sliderData.tailLineIndex;
                    var noteLineLayer = i < sliceCount - 1 ? sliderData.headLineLayer : sliderData.tailLineLayer;
                    var time = Mathf.LerpUnclamped(sliderData.time, sliderData.tailTime, (float)i / (sliceCount - 1));
                    var sliderNoteData = NoteData.CreateBurstSliderNoteData(
                        time,
                        sliderData.beat,
                        sliderData.rotation,
                        lineIndex,
                        noteLineLayer,
                        sliderData.headBeforeJumpLineLayer,
                        sliderData.colorType,
                        NoteCutDirection.Any,
                        1f
                    );
                    list.Add(sliderNoteData);
                }
            }
        }

        #endregion
    }
}