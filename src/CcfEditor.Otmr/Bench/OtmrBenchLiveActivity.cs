namespace CcfEditor.Otmr.Bench;

/// <summary>
/// A decoded read/live signal change. Milestone 1 does not create these yet;
/// the contract exists so the bench UI can be connected to a verified decoder later
/// without changing the pin-test workflow.
/// </summary>
public sealed record OtmrBenchLiveActivity(
    DateTimeOffset Timestamp,
    int RecordIndex,
    int? Card,
    int? Channel,
    string StateOrValue,
    string RawHex = "");
