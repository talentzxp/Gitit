using GitIt.Core;

namespace GitIt.Desktop;

public enum TimelineEventType { Created, Modified, Commented, Saved, Participated, VersionObserved }
public enum TimePrecision { Exact, VersionTime, EstimatedInterval, Unknown }

/// <summary>A human-readable event derived from Core evidence without inventing time precision.</summary>
public sealed record TimelineEvent(
    TimelineEventType EventType,
    string? Participant,
    string Version,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    TimePrecision TimePrecision,
    string EvidenceType,
    EvidenceStrength EvidenceStrength,
    string Description);
