﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Hermit
{
    public sealed class EventBroker : IEventBroker
    {
        private static EventBroker defaultInstance;

        public static EventBroker Default => defaultInstance ?? (defaultInstance = new EventBroker());

        #region Runtime variables

        private readonly object _locker = new object();

        private readonly EventNode _rootNode = new EventNode("Root");

        public readonly ConcurrentDictionary<string, EventData> StickyEvents =
            new ConcurrentDictionary<string, EventData>();

        private readonly List<object> _registeredObjects = new List<object>();

        #endregion

        #region Registration

        public void Register(object obj)
        {
            if (obj == null) { return; }

            lock (_locker)
            {
                if (_registeredObjects.Contains(obj)) { return; }

                _registeredObjects.Add(obj);
            }

            var subscriptions = SubscriptionHelper.FindSubscriber(obj);
            foreach (var subscription in subscriptions)
            {
                var endpoint = subscription.Endpoint;
                if (subscription.IsSticky)
                {
                    if (StickyEvents.TryGetValue(endpoint, out var eventData))
                    {
                        TriggerEvent(subscription, eventData);
                    }
                }

                EventNode.AddSubscription(_rootNode, subscription);
            }
        }

        public void Unregister(object obj)
        {
            if (obj == null) { return; }

            lock (_locker)
            {
                if (!_registeredObjects.Contains(obj)) { return; }

                _registeredObjects.Remove(obj);
            }

            var subscriptions = SubscriptionHelper.FindSubscriber(obj);
            foreach (var subscription in subscriptions) { EventNode.RemoveSubscription(_rootNode, subscription); }
        }

        #endregion

        #region Trigger

        public void Trigger(string endpoint)
        {
            Trigger(endpoint, EventData.Empty);
        }

        public void Trigger<TEventData>(TEventData payloads) where TEventData : EventData
        {
            Trigger(HermitEvent.DefaultEndpoint, payloads);
        }

        public void Trigger<TEventData>(string endpoint, TEventData payloads) where TEventData : EventData
        {
            var eventNodes = _rootNode.GetEventNodes(endpoint);
            var subscriptions = eventNodes.SelectMany(e => e.GetSubscriptions()).ToList();
            subscriptions.Sort();

            foreach (var subscription in subscriptions) { TriggerEvent(subscription, payloads); }
        }

        public void TriggerSticky(string endpoint)
        {
            TriggerSticky(endpoint, EventData.Empty);
        }

        public void TriggerSticky<TEventData>(TEventData payloads) where TEventData : EventData
        {
            TriggerSticky(HermitEvent.DefaultEndpoint, payloads);
        }

        public void TriggerSticky<TEventData>(string endpoint, TEventData payloads) where TEventData : EventData
        {
            Trigger(endpoint, payloads);
            CacheStickyEvents(endpoint, payloads);
        }

        public async Task TriggerAsync(string endpoint)
        {
            await TriggerAsync(endpoint, EventData.Empty);
        }

        public async Task TriggerAsync<TEventData>(TEventData payloads)
            where TEventData : EventData
        {
            await TriggerAsync(HermitEvent.DefaultEndpoint, payloads);
        }

        public async Task TriggerAsync<TEventData>(string endpoint, TEventData payloads)
            where TEventData : EventData
        {
            var eventNodes = _rootNode.GetEventNodes(endpoint);
            var subscriptions = eventNodes.SelectMany(e => e.GetSubscriptions()).ToList();
            subscriptions.Sort();

            foreach (var subscription in subscriptions) { await TriggerEventAsync(subscription, payloads); }
        }

        #endregion

        #region Helpers

        private void CacheStickyEvents(string endPoint, EventData payload)
        {
            var nodes = endPoint.Split('.');
            var lastElement = nodes[nodes.Length - 1];
            if (lastElement.Equals("*")) { Her.Error($"Trigger sticky with wildcard endpoint is not supported."); }
            else { StickyEvents.TryAdd(endPoint, payload); }
        }

        private static bool IsMainThread()
        {
            var currentThreadId = Thread.CurrentThread.ManagedThreadId;
            var mainThreadId = MainThreadDispatcher.Instance.MainThreadId;
            return currentThreadId == mainThreadId;
        }

        private static void TriggerEvent<TEventData>(Subscription subscription, TEventData data)
            where TEventData : EventData
        {
            var eventDataType = typeof(TEventData);
            var methodName = subscription.MethodName;
            var subEventType = subscription.EventDataType;
            var noParameters = subscription.EventDataType == null;

            if (!noParameters && eventDataType != subscription.EventDataType)
            {
                Debug.LogError($"Subscription[{methodName}]: [{subEventType}] is not match [{eventDataType}].");
                return;
            }

            if (subscription.ThreadMode == ThreadMode.Main && !IsMainThread())
            {
                MainThreadDispatcher.Instance.Enqueue(() =>
                {
                    if (noParameters) { ((Action) subscription.MethodInvoker).Invoke(); }
                    else { ((Action<TEventData>) subscription.MethodInvoker).Invoke(data); }
                });
            }

            if (noParameters) { ((Action) subscription.MethodInvoker).Invoke(); }
            else { ((Action<TEventData>) subscription.MethodInvoker).Invoke(data); }
        }

        private static async Task TriggerEventAsync<TEventData>(Subscription subscription, TEventData data)
            where TEventData : EventData
        {
            var eventDataType = typeof(TEventData);
            var methodName = subscription.MethodName;
            var subEventType = subscription.EventDataType;
            var noParameters = subscription.EventDataType == null;

            if (!noParameters && eventDataType != subscription.EventDataType)
            {
                Debug.LogError($"Subscription[{methodName}]: [{subEventType}] is not match [{eventDataType}].");
                return;
            }

            // if (subscription.ThreadMode == ThreadMode.Main)
            // {
            //     Debug.LogWarning($"ThreadMode must be ThreadMode.Current while using async calls.");
            // }

            if (noParameters) { await ((Func<Task>) subscription.MethodInvoker).Invoke(); }
            else { await ((Func<TEventData, Task>) subscription.MethodInvoker).Invoke(data); }
        }

        #endregion
    }
}
