namespace CcfEditor.Otmr.Bench;

public sealed record OtmrBenchPinDefinition(
    string Connector,
    string Pin,
    string Role,
    string Mio,
    string Channel,
    string ExpectedFunction,
    string ReturnOrPair,
    string SafetyInstruction,
    bool IsVoltageTestPoint,
    int? ExpectedRecordA,
    int? ExpectedRecordB,
    int? ExpectedCard,
    int? ExpectedChannel,
    string EvidenceStatus,
    string Source);
