﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;

namespace System.Net
{
    [EventSource(Name = "System.Net.NameResolution")]
    internal sealed class NameResolutionTelemetry : EventSource
    {
        public static readonly NameResolutionTelemetry Log = new NameResolutionTelemetry();

        private const int ResolutionStartEventId = 1;
        private const int ResolutionStopEventId = 2;
        private const int ResolutionFailedEventId = 3;

        private PollingCounter? _lookupsRequestedCounter;
        private PollingCounter? _currentLookupsCounter;
        private EventCounter? _lookupsDuration;

        private long _lookupsRequested;
        private long _currentLookups;

        protected override void OnEventCommand(EventCommandEventArgs command)
        {
            if (command.Command == EventCommand.Enable)
            {
                // The cumulative number of name resolution requests started since events were enabled
                _lookupsRequestedCounter ??= new PollingCounter("dns-lookups-requested", this, () => Interlocked.Read(ref _lookupsRequested))
                {
                    DisplayName = "DNS Lookups Requested"
                };

                // Current number of DNS requests pending
                _currentLookupsCounter ??= new PollingCounter("current-dns-lookups", this, () => Interlocked.Read(ref _currentLookups))
                {
                    DisplayName = "Current DNS Lookups"
                };

                _lookupsDuration ??= new EventCounter("dns-lookups-duration", this)
                {
                    DisplayName = "Average DNS Lookup Duration",
                    DisplayUnits = "ms"
                };
            }
        }

        [Event(ResolutionStartEventId, Level = EventLevel.Informational)]
        private void ResolutionStart(string hostNameOrAddress) => WriteEvent(ResolutionStartEventId, hostNameOrAddress);

        [Event(ResolutionStopEventId, Level = EventLevel.Informational)]
        private void ResolutionStop() => WriteEvent(ResolutionStopEventId);

        [Event(ResolutionFailedEventId, Level = EventLevel.Informational)]
        private void ResolutionFailed() => WriteEvent(ResolutionFailedEventId);


        [NonEvent]
        public long BeforeResolution(object hostNameOrAddress)
        {
            // System.Diagnostics.Metrics part
            NameResolutionMetrics.BeforeResolution(hostNameOrAddress, out string? host);

            // System.Diagnostics.Tracing part
            if (IsEnabled())
            {
                Interlocked.Increment(ref _lookupsRequested);
                Interlocked.Increment(ref _currentLookups);

                if (IsEnabled(EventLevel.Informational, EventKeywords.None))
                {
                    host ??= NameResolutionMetrics.GetHostnameFromStateObject(hostNameOrAddress);

                    ResolutionStart(host);
                }

                return Stopwatch.GetTimestamp();
            }

            return 0;
        }

        [NonEvent]
        public void AfterResolution(long? startingTimestamp, bool successful)
        {
            Debug.Assert(startingTimestamp.HasValue);

            if (startingTimestamp != 0)
            {
                Interlocked.Decrement(ref _currentLookups);

                _lookupsDuration?.WriteMetric(Stopwatch.GetElapsedTime(startingTimestamp.Value).TotalMilliseconds);

                if (IsEnabled(EventLevel.Informational, EventKeywords.None))
                {
                    if (!successful)
                    {
                        ResolutionFailed();
                    }

                    ResolutionStop();
                }
            }
        }
    }
}
