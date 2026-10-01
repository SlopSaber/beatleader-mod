using System;
using System.Collections.Generic;
using BeatLeader.Models;

namespace BeatLeader {
    public class EventsProcessor<T> : IEventsProcessor where T : struct {
        public delegate float TimeSelectorDelegate(T item);

        public EventsProcessor(IEnumerable<T> events, TimeSelectorDelegate timeSelector)
            : this(timeSelector, new LinkedList<T>(events)) { }

        private EventsProcessor(TimeSelectorDelegate timeSelector, LinkedList<T> events) {
            _events = events;
            _timeSelectorDelegate = timeSelector;
            ResetNode();
        }

        // The prepared list transfers ownership; event subscribers can mutate its nodes.
        internal static EventsProcessor<T> FromPreparedEvents(LinkedList<T> events, TimeSelectorDelegate timeSelector) {
            return new(timeSelector, events);
        }

        public bool CurrentEventHasTimeMismatch { get; private set; }
        public bool QueueIsBeingAdjusted { get; private set; }

        public event Action<LinkedListNode<T>>? EventDequeuedEvent;
        public event Action? EventQueueAdjustStartedEvent;
        public event Action? EventQueueAdjustFinishedEvent;

        private readonly LinkedList<T> _events;
        private readonly TimeSelectorDelegate _timeSelectorDelegate;
        private LinkedListNode<T>? _currentEvent;
        private float _lastTime;

        public void Tick(float time) {
            do {
                var nextEvent = _currentEvent?.Value;
                if (nextEvent is null) break;

                var nextEventTime = _timeSelectorDelegate(nextEvent.Value);
                if (nextEventTime > time) break;

                EventDequeuedEvent?.Invoke(_currentEvent!);
                _currentEvent = _currentEvent!.Next;
            } while (true);
            
            _lastTime = time;
            if (!QueueIsBeingAdjusted) return;
            
            QueueIsBeingAdjusted = false;
            CurrentEventHasTimeMismatch = false;
            EventQueueAdjustFinishedEvent?.Invoke();
        }

        public void AdjustQueue(float newTimeMilestone) {
            if (newTimeMilestone < _lastTime) {
                ResetNode();
                _lastTime = 0f;
                CurrentEventHasTimeMismatch = true;
            }

            QueueIsBeingAdjusted = true;
            EventQueueAdjustStartedEvent?.Invoke();
        }

        private void ResetNode() {
            _currentEvent = _events.First;
        }
    }
}
