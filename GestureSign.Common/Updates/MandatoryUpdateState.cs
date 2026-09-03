using NuGet.Versioning;
using System;
using System.IO;

namespace GestureSign.Common.Updates
{
    public sealed class MandatoryUpdateState
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string PendingVersion { get; set; }

        public DateTimeOffset? FirstSeenUtc { get; set; }

        public DateTimeOffset LastObservedUtc { get; set; } = DateTimeOffset.MinValue;
    }

    public sealed class MandatoryUpdateDecision
    {
        internal MandatoryUpdateDecision(bool updatePending, bool mustUpdate, string pendingVersion,
            DateTimeOffset? deadlineUtc, TimeSpan? remaining)
        {
            UpdatePending = updatePending;
            MustUpdate = mustUpdate;
            PendingVersion = pendingVersion;
            DeadlineUtc = deadlineUtc;
            Remaining = remaining;
        }

        public bool UpdatePending { get; }

        public bool MustUpdate { get; }

        public string PendingVersion { get; }

        public DateTimeOffset? DeadlineUtc { get; }

        public TimeSpan? Remaining { get; }
    }

    public static class MandatoryUpdatePolicy
    {
        public static readonly TimeSpan OfflineGracePeriod = TimeSpan.FromDays(3);

        public static MandatoryUpdateDecision RecordSuccessfulCheck(MandatoryUpdateState state,
            NuGetVersion currentVersion, NuGetVersion latestVersion, DateTimeOffset now)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (currentVersion == null)
                throw new ArgumentNullException(nameof(currentVersion));
            if (latestVersion == null)
                throw new ArgumentNullException(nameof(latestVersion));

            DateTimeOffset observedNow = ObserveClock(state, now);
            NuGetVersion pendingVersion = null;
            bool hasPendingUpdate = state.FirstSeenUtc.HasValue &&
                                    ReleaseVersion.TryParse(state.PendingVersion,
                                        out pendingVersion) &&
                                    VersionComparer.VersionRelease.Compare(pendingVersion,
                                        currentVersion) > 0;
            if (VersionComparer.VersionRelease.Compare(latestVersion, currentVersion) <= 0)
            {
                if (hasPendingUpdate)
                    return CreateDecision(state, observedNow);
                state.PendingVersion = null;
                state.FirstSeenUtc = null;
                return CreateDecision(state, observedNow);
            }

            if (!hasPendingUpdate)
                state.FirstSeenUtc = observedNow;
            if (!hasPendingUpdate ||
                VersionComparer.VersionRelease.Compare(latestVersion, pendingVersion) > 0)
                state.PendingVersion = ReleaseVersion.ToReleaseString(latestVersion);
            return CreateDecision(state, observedNow);
        }

        public static MandatoryUpdateDecision ObserveOffline(MandatoryUpdateState state, DateTimeOffset now)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            return CreateDecision(state, ObserveClock(state, now));
        }

        public static void Validate(MandatoryUpdateState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (state.SchemaVersion != MandatoryUpdateState.CurrentSchemaVersion)
                throw new InvalidDataException("The mandatory update state schema is not supported.");
            if (state.LastObservedUtc.Offset != TimeSpan.Zero)
                throw new InvalidDataException("The mandatory update clock must use UTC.");

            bool hasVersion = !string.IsNullOrWhiteSpace(state.PendingVersion);
            if (hasVersion != state.FirstSeenUtc.HasValue)
                throw new InvalidDataException("The mandatory update state is incomplete.");
            if (!hasVersion)
                return;

            string canonical = ReleaseVersion.ToReleaseString(ReleaseVersion.Parse(state.PendingVersion));
            if (!string.Equals(state.PendingVersion, canonical, StringComparison.Ordinal))
                throw new InvalidDataException("The pending update version is not canonical SemVer.");
            if (state.FirstSeenUtc.Value.Offset != TimeSpan.Zero ||
                state.LastObservedUtc < state.FirstSeenUtc.Value)
                throw new InvalidDataException("The mandatory update timestamps are invalid.");
        }

        private static DateTimeOffset ObserveClock(MandatoryUpdateState state, DateTimeOffset now)
        {
            DateTimeOffset utcNow = now.ToUniversalTime();
            if (state.LastObservedUtc.Offset != TimeSpan.Zero)
                throw new InvalidDataException("The stored update clock is not UTC.");
            if (utcNow > state.LastObservedUtc)
                state.LastObservedUtc = utcNow;
            return state.LastObservedUtc;
        }

        private static MandatoryUpdateDecision CreateDecision(MandatoryUpdateState state,
            DateTimeOffset observedNow)
        {
            if (string.IsNullOrWhiteSpace(state.PendingVersion) || !state.FirstSeenUtc.HasValue)
                return new MandatoryUpdateDecision(false, false, null, null, null);

            DateTimeOffset deadline = state.FirstSeenUtc.Value + OfflineGracePeriod;
            TimeSpan remaining = deadline - observedNow;
            bool mustUpdate = remaining <= TimeSpan.Zero;
            return new MandatoryUpdateDecision(true, mustUpdate, state.PendingVersion, deadline,
                mustUpdate ? TimeSpan.Zero : remaining);
        }
    }
}
