using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace GestureSign.Common.Lifecycle
{
    public static class BuildLifetime
    {
        internal const string BuiltAtMetadataKey = "TouchPilot.BuiltAtUtcTicks";
        internal const string ExpiresAtMetadataKey = "TouchPilot.ExpiresAtUtcTicks";

        public static BuildLifetimeDecision Evaluate(DateTimeOffset now)
        {
            AssemblyMetadataAttribute[] metadata = typeof(BuildLifetime).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToArray();
            string[] builtAtValues = metadata.Where(attribute =>
                    string.Equals(attribute.Key, BuiltAtMetadataKey, StringComparison.Ordinal))
                .Select(attribute => attribute.Value)
                .ToArray();
            string[] expiresAtValues = metadata.Where(attribute =>
                    string.Equals(attribute.Key, ExpiresAtMetadataKey, StringComparison.Ordinal))
                .Select(attribute => attribute.Value)
                .ToArray();

            if (builtAtValues.Length == 0 && expiresAtValues.Length == 0)
                return BuildLifetimeDecision.Unrestricted();
            if (builtAtValues.Length != 1 || expiresAtValues.Length != 1)
                return BuildLifetimeDecision.Invalid();
            return Evaluate(builtAtValues[0], expiresAtValues[0], now);
        }

        internal static BuildLifetimeDecision Evaluate(string builtAtTicksValue,
            string expiresAtTicksValue, DateTimeOffset now)
        {
            if (builtAtTicksValue == null && expiresAtTicksValue == null)
                return BuildLifetimeDecision.Unrestricted();
            if (!long.TryParse(builtAtTicksValue, NumberStyles.None, CultureInfo.InvariantCulture,
                    out long builtAtTicks) ||
                !long.TryParse(expiresAtTicksValue, NumberStyles.None, CultureInfo.InvariantCulture,
                    out long expiresAtTicks) || builtAtTicks <= 0 || expiresAtTicks <= 0)
                return BuildLifetimeDecision.Invalid();

            try
            {
                var builtAt = new DateTimeOffset(builtAtTicks, TimeSpan.Zero);
                var expiresAt = new DateTimeOffset(expiresAtTicks, TimeSpan.Zero);
                if (expiresAt != builtAt.AddMonths(3))
                    return BuildLifetimeDecision.Invalid();

                BuildLifetimeStatus status = now.ToUniversalTime() >= expiresAt
                    ? BuildLifetimeStatus.Expired
                    : BuildLifetimeStatus.Active;
                return new BuildLifetimeDecision(status, builtAt, expiresAt);
            }
            catch (ArgumentOutOfRangeException)
            {
                return BuildLifetimeDecision.Invalid();
            }
        }
    }

    public sealed class BuildLifetimeDecision
    {
        internal BuildLifetimeDecision(BuildLifetimeStatus status, DateTimeOffset? builtAtUtc,
            DateTimeOffset? expiresAtUtc)
        {
            Status = status;
            BuiltAtUtc = builtAtUtc;
            ExpiresAtUtc = expiresAtUtc;
        }

        public BuildLifetimeStatus Status { get; }

        public DateTimeOffset? BuiltAtUtc { get; }

        public DateTimeOffset? ExpiresAtUtc { get; }

        public bool CanRun => Status == BuildLifetimeStatus.Unrestricted ||
                              Status == BuildLifetimeStatus.Active;

        internal static BuildLifetimeDecision Unrestricted()
        {
            return new BuildLifetimeDecision(BuildLifetimeStatus.Unrestricted, null, null);
        }

        internal static BuildLifetimeDecision Invalid()
        {
            return new BuildLifetimeDecision(BuildLifetimeStatus.Invalid, null, null);
        }
    }

    public enum BuildLifetimeStatus
    {
        Unrestricted,
        Active,
        Expired,
        Invalid
    }
}
