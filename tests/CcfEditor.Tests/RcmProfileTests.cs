using System.Text.Json;
using System.Text.Json.Nodes;
using CcfEditor.Core;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.Tests;

public sealed class RcmProfileTests
{
    [Fact]
    public void CreateFromCcfImportsLogicalMappingsButLeavesPhysicalFieldsUnassigned()
    {
        (CcfDocument document, byte[] source) = LoadCcf();
        RcmProfile profile = RcmProfileFactory.CreateFromCcf(document, "Class 171", FixedTime);

        Assert.NotEmpty(profile.Pins);
        Assert.Empty(profile.Connectors);
        Assert.All(profile.Pins, input =>
        {
            Assert.NotEqual(Guid.Empty, input.Id);
            Assert.Equal(string.Empty, input.Connector);
            Assert.Equal(string.Empty, input.Pin);
            Assert.Equal(string.Empty, input.Mio);
            Assert.Equal(string.Empty, input.PhysicalChannel);
            Assert.Equal(string.Empty, input.ReturnOrPair);
            Assert.Equal(string.Empty, input.SafetyClassification);
            Assert.False(input.Testable);
            Assert.NotNull(input.CcfReference);
        });
        Assert.Contains(profile.Pins, input => input.CcfReference?.RecordA == 0 &&
            input.CcfReference.RecordB == 12 && input.CcfReference.LogicalCard == 0 &&
            input.CcfReference.LogicalChannel == 0);
        Assert.Equal(source, File.ReadAllBytes(document.SourcePath!));
    }

    [Fact]
    public async Task ProfileCanOpenWithoutAnyLoadedCcf()
    {
        RcmProfile profile = CreateEditableProfile();
        string path = TempJsonPath();
        try
        {
            await RcmProfileJson.SaveAsync(path, profile, FixedTime.AddMinutes(1));
            RcmProfile reopened = await RcmProfileJson.LoadAsync(path);
            Assert.Equal("CCF NOT LOADED", RcmProfileJson.GetCcfStatus(reopened, null));
            Assert.Single(reopened.Pins);
        }
        finally { DeleteIfExists(path); }
    }

    [Fact]
    public async Task OpenRestoresConnectorsPinsAndTestProgress()
    {
        RcmProfile profile = CreateEditableProfile();
        Capture(profile.Pins.Single(), RcmElectricalTestState.VoltageRemoved, 0x4A);
        string path = TempJsonPath();
        try
        {
            await RcmProfileJson.SaveAsync(path, profile, FixedTime.AddMinutes(1));
            RcmProfile reopened = await RcmProfileJson.LoadAsync(path);

            Assert.Equal("J1", Assert.Single(reopened.Connectors).Name);
            RcmPinProfile restored = Assert.Single(reopened.Pins);
            Assert.Equal("A", restored.Pin);
            Assert.True(restored.VoltageRemoved.Tested);
            Assert.Single(restored.VoltageRemoved.CompleteRawFrames);
            Assert.Equal(RcmResultStates.VoltageRemovedCaptured, restored.RcmResult);
        }
        finally { DeleteIfExists(path); }
    }

    [Fact]
    public void AddConnectorCreatesArbitraryJsonOwnedConnector()
    {
        RcmProfile profile = EmptyProfile();
        RcmProfileEditor.AddConnector(profile, "MIO-A");
        Assert.Equal("MIO-A", Assert.Single(profile.Connectors).Name);
    }

    [Fact]
    public void RenameConnectorPreservesStableIdAndEvidence()
    {
        RcmProfile profile = CreateEditableProfile();
        RcmPinProfile input = profile.Pins.Single();
        Guid id = input.Id;
        Capture(input, RcmElectricalTestState.VoltageRemoved, 0x4A);

        RcmProfileEditor.RenameConnector(profile, "J1", "J2");

        RcmPinProfile renamed = profile.GetInput(id);
        Assert.Equal("J2", renamed.Connector);
        Assert.True(renamed.VoltageRemoved.Tested);
        Assert.Single(renamed.VoltageRemoved.CompleteRawFrames);
    }

    [Fact]
    public void DeleteConnectorContainingInputsRequiresExplicitDeletionPath()
    {
        RcmProfile profile = CreateEditableProfile();
        Assert.Throws<InvalidOperationException>(() =>
            RcmProfileEditor.DeleteConnector(profile, "J1", deleteInputs: false));
        Assert.Single(profile.Pins);

        Assert.Equal(1, RcmProfileEditor.DeleteConnector(profile, "J1", deleteInputs: true));
        Assert.Empty(profile.Connectors);
        Assert.Empty(profile.Pins);
    }

    [Fact]
    public void AddInputSupportsCompleteManualPhysicalAndLogicalMapping()
    {
        RcmProfile profile = EmptyProfile();
        RcmPinProfile input = RcmProfileEditor.AddInput(profile, CompleteEdit());

        Assert.NotEqual(Guid.Empty, input.Id);
        Assert.Equal("J1", input.Connector);
        Assert.Equal("A", input.Pin);
        Assert.Equal("Throttle 1", input.Function);
        Assert.Equal("1", input.Mio);
        Assert.True(input.Testable);
        Assert.Equal(0, input.CcfReference!.RecordA);
        Assert.Equal(12, input.CcfReference.RecordB);
        Assert.Equal(RcmResultStates.NotTested, input.RcmResult);
        Assert.Equal("J1", Assert.Single(profile.Connectors).Name);
    }

    [Fact]
    public void EditPinAndConnectorUsesStableIdentity()
    {
        RcmProfile profile = CreateEditableProfile();
        RcmPinProfile input = profile.Pins.Single();
        Guid id = input.Id;
        RcmInputEdit edit = RcmInputEdit.From(input);
        edit.Connector = "J2";
        edit.Pin = "C";

        RcmProfileEditor.UpdateInput(profile, id, edit);

        Assert.Equal(id, input.Id);
        Assert.Equal("J2", input.Connector);
        Assert.Equal("C", input.Pin);
        Assert.Contains(profile.Connectors, connector => connector.Name == "J2");
    }

    [Fact]
    public void EditLogicalCcfFieldsValidatesAgainstLoadedCcf()
    {
        (CcfDocument document, _) = LoadCcf();
        RcmProfile profile = CreateEditableProfile();
        RcmPinProfile input = profile.Pins.Single();
        RcmInputEdit edit = RcmInputEdit.From(input);
        edit.RecordA = 1;
        edit.RecordB = 13;
        edit.LogicalCard = 2;
        edit.LogicalChannel = 3;

        RcmInputEditResult result = RcmProfileEditor.UpdateInput(profile, input.Id, edit, document);
        Assert.True(result.LogicalMappingChanged);
        Assert.Equal(1, input.CcfReference!.RecordA);
        Assert.Equal(13, input.CcfReference.RecordB);
        Assert.Equal(2, input.CcfReference.LogicalCard);
        Assert.Equal(3, input.CcfReference.LogicalChannel);

        edit.RecordA = document.Records.Count;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RcmProfileEditor.UpdateInput(profile, input.Id, edit, document));
        RcmProfileEditor.UpdateInput(profile, input.Id, edit, document: null);
    }

    [Fact]
    public async Task SchemaOneProfileWithoutIdsMigratesToPersistentIds()
    {
        RcmProfile profile = CreateEditableProfile();
        profile.SchemaVersion = "1.0";
        JsonNode root = JsonSerializer.SerializeToNode(profile, JsonOptions)!;
        foreach (JsonNode? node in root["pins"]!.AsArray())
            node!.AsObject().Remove("id");
        string path = TempJsonPath();
        try
        {
            await File.WriteAllTextAsync(path, root.ToJsonString(JsonOptions));
            RcmProfile migrated = await RcmProfileJson.LoadAsync(path);
            Guid assigned = migrated.Pins.Single().Id;
            Assert.NotEqual(Guid.Empty, assigned);
            Assert.Equal(RcmProfile.CurrentSchemaVersion, migrated.SchemaVersion);

            await RcmProfileJson.SaveAsync(path, migrated, FixedTime.AddMinutes(2));
            Assert.Equal(assigned, (await RcmProfileJson.LoadAsync(path)).Pins.Single().Id);
        }
        finally { DeleteIfExists(path); }
    }

    [Fact]
    public async Task SaveAndReopenPreservesEveryEdit()
    {
        RcmProfile profile = CreateEditableProfile();
        RcmPinProfile input = profile.Pins.Single();
        RcmInputEdit edit = RcmInputEdit.From(input);
        edit.Pin = "Z";
        edit.Function = "Edited Function";
        edit.Role = "Edited Role";
        edit.Mio = "MIO-9";
        edit.PhysicalChannel = "17";
        edit.ReturnOrPair = "J1-Y";
        edit.SafetyClassification = "Bench instruction";
        edit.Notes = "Operator notes";
        edit.RecordAText = "LOW description";
        edit.RecordBText = "HIGH description";
        RcmProfileEditor.UpdateInput(profile, input.Id, edit);
        string path = TempJsonPath();
        try
        {
            await RcmProfileJson.SaveAsync(path, profile, FixedTime.AddMinutes(3));
            RcmPinProfile restored = (await RcmProfileJson.LoadAsync(path)).GetInput(input.Id);

            Assert.Equal("Z", restored.Pin);
            Assert.Equal("Edited Function", restored.Function);
            Assert.Equal("Edited Role", restored.Role);
            Assert.Equal("MIO-9", restored.Mio);
            Assert.Equal("17", restored.PhysicalChannel);
            Assert.Equal("J1-Y", restored.ReturnOrPair);
            Assert.Equal("Bench instruction", restored.SafetyClassification);
            Assert.Equal("Operator notes", restored.Notes);
            Assert.Equal("LOW description", restored.CcfReference!.RecordAText);
            Assert.Equal("HIGH description", restored.CcfReference.RecordBText);
        }
        finally { DeleteIfExists(path); }
    }

    [Fact]
    public void CcfAssociationReportsMatchMismatchAndNotLoaded()
    {
        (CcfDocument document, _) = LoadCcf();
        RcmProfile profile = RcmProfileFactory.CreateFromCcf(document, "Class 171", FixedTime);
        Assert.Equal("CCF NOT LOADED", RcmProfileJson.GetCcfStatus(profile, null));
        Assert.Equal("CCF MATCH", RcmProfileJson.GetCcfStatus(profile, document));
        profile.SourceCcfSha256 = new string('0', 64);
        Assert.Equal("CCF MISMATCH", RcmProfileJson.GetCcfStatus(profile, document));
    }

    [Fact]
    public void ZeroFrameCaptureIsNoDataAndCannotBeComparedOrCountAsProgress()
    {
        RcmProfile profile = CreateEditableProfile();
        RcmPinProfile input = profile.Pins.Single();
        var coordinator = new RcmCaptureWindowCoordinator();

        coordinator.Begin(input, RcmElectricalTestState.VoltageRemoved, FixedTime);
        RcmStateEvidence evidence = coordinator.Stop(FixedTime.AddSeconds(2));

        Assert.False(evidence.Tested);
        Assert.True(evidence.NoOtmrData);
        Assert.Equal(0, evidence.FrameCount);
        Assert.Empty(evidence.CandidateRawSignature);
        Assert.Equal(0, profile.CompletedTestablePinCount);
        Assert.Equal(RcmResultStates.NotTested, input.RcmResult);
        Assert.Throws<InvalidOperationException>(() => coordinator.Compare(input, FixedTime.AddSeconds(3)));
    }

    [Fact]
    public async Task LegacyTestedTrueWithZeroFramesNormalizesToNoData()
    {
        RcmProfile profile = CreateEditableProfile();
        profile.SchemaVersion = "1.0";
        RcmPinProfile input = profile.Pins.Single();
        input.VoltageRemoved = new RcmStateEvidence
        {
            Tested = true,
            CaptureStart = FixedTime,
            CaptureStop = FixedTime.AddSeconds(2),
            CandidateRawSignature = "empty-signature-must-not-survive"
        };
        input.VoltageApplied24V = new RcmStateEvidence
        {
            Tested = true,
            CaptureStart = FixedTime.AddSeconds(3),
            CaptureStop = FixedTime.AddSeconds(5),
            CandidateRawSignature = "second-empty-signature"
        };
        input.Comparison.ComparedAt = FixedTime.AddSeconds(6);
        input.RcmResult = RcmResultStates.NoRepeatableDifference;
        string path = TempJsonPath();
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, JsonOptions));
            RcmPinProfile restored = (await RcmProfileJson.LoadAsync(path)).Pins.Single();
            Assert.False(restored.VoltageRemoved.Tested);
            Assert.True(restored.VoltageRemoved.NoOtmrData);
            Assert.Empty(restored.VoltageRemoved.CandidateRawSignature);
            Assert.False(restored.VoltageApplied24V.Tested);
            Assert.True(restored.VoltageApplied24V.NoOtmrData);
            Assert.Empty(restored.VoltageApplied24V.CandidateRawSignature);
            Assert.Null(restored.Comparison.ComparedAt);
            Assert.Equal(RcmResultStates.NotTested, restored.RcmResult);
        }
        finally { DeleteIfExists(path); }
    }

    [Fact]
    public void PhysicalMetadataEditDoesNotEraseEvidenceOrComparison()
    {
        RcmProfile profile = CreateEditableProfile();
        RcmPinProfile input = profile.Pins.Single();
        Capture(input, RcmElectricalTestState.VoltageRemoved, 0x4A);
        Capture(input, RcmElectricalTestState.VoltageApplied24V, 0x4B);
        new RcmCaptureWindowCoordinator().Compare(input, FixedTime.AddMinutes(1));
        string priorResult = input.RcmResult;
        DateTimeOffset? comparedAt = input.Comparison.ComparedAt;
        RcmInputEdit edit = RcmInputEdit.From(input);
        edit.Connector = "TB1";
        edit.Pin = "9";
        edit.Notes = "Physical data corrected";

        RcmProfileEditor.UpdateInput(profile, input.Id, edit);

        Assert.True(input.VoltageRemoved.Tested);
        Assert.True(input.VoltageApplied24V.Tested);
        Assert.NotEmpty(input.VoltageRemoved.CompleteRawFrames);
        Assert.NotEmpty(input.VoltageApplied24V.CompleteRawFrames);
        Assert.Equal(comparedAt, input.Comparison.ComparedAt);
        Assert.Equal(priorResult, input.RcmResult);
    }

    [Fact]
    public void LogicalMappingEditReportsEvidenceWarningConditionWithoutClearingEvidence()
    {
        RcmProfile profile = CreateEditableProfile();
        RcmPinProfile input = profile.Pins.Single();
        Capture(input, RcmElectricalTestState.VoltageRemoved, 0x4A);
        RcmInputEdit edit = RcmInputEdit.From(input);
        edit.RecordA = 7;

        RcmInputEditResult result = RcmProfileEditor.UpdateInput(profile, input.Id, edit);

        Assert.True(result.LogicalMappingChanged);
        Assert.True(result.HadCapturedEvidence);
        Assert.True(input.VoltageRemoved.Tested);
        Assert.Single(input.VoltageRemoved.CompleteRawFrames);
    }

    [Fact]
    public async Task ProfileOperationsNeverModifySourceCcfBytes()
    {
        (CcfDocument document, byte[] before) = LoadCcf();
        RcmProfile profile = RcmProfileFactory.CreateFromCcf(document, "Class 171", FixedTime);
        RcmPinProfile input = profile.Pins.First();
        RcmInputEdit edit = RcmInputEdit.From(input);
        edit.Connector = "J1";
        edit.Pin = "A";
        edit.Testable = true;
        RcmProfileEditor.UpdateInput(profile, input.Id, edit, document);
        string path = TempJsonPath();
        try
        {
            await RcmProfileJson.SaveAsync(path, profile, FixedTime.AddMinutes(1));
            _ = await RcmProfileJson.LoadAsync(path);
            Assert.Equal(before, File.ReadAllBytes(document.SourcePath!));
        }
        finally { DeleteIfExists(path); }
    }

    [Fact]
    public void NoPassOrFailIsAvailableWithoutVerifiedDecoder()
    {
        Assert.DoesNotContain("PASS", RcmResultStates.Allowed);
        Assert.DoesNotContain("FAIL", RcmResultStates.Allowed);
        RcmPinProfile input = CreateEditableProfile().Pins.Single();
        Capture(input, RcmElectricalTestState.VoltageRemoved, 0x4A);
        Capture(input, RcmElectricalTestState.VoltageApplied24V, 0x4B);
        new RcmCaptureWindowCoordinator().Compare(input, FixedTime.AddMinutes(1));
        Assert.False(input.Comparison.DecoderVerified);
        Assert.DoesNotContain(input.RcmResult, new[] { "PASS", "FAIL" });
    }

    private static readonly DateTimeOffset FixedTime =
        new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static RcmProfile EmptyProfile() => new()
    {
        VehicleType = "Class 171",
        SourceCcfFilename = "source.ccf",
        SourceCcfSha256 = new string('A', 64),
        SourceCcfSize = 26_600,
        CreationTimestamp = FixedTime,
        LastModifiedTimestamp = FixedTime
    };

    private static RcmProfile CreateEditableProfile()
    {
        RcmProfile profile = EmptyProfile();
        RcmProfileEditor.AddConnector(profile, "J1");
        RcmProfileEditor.AddInput(profile, CompleteEdit());
        return profile;
    }

    private static RcmInputEdit CompleteEdit() => new()
    {
        Connector = "J1", Pin = "A", Function = "Throttle 1", Role = "input",
        Mio = "1", PhysicalChannel = "1", ReturnOrPair = "J1-L", Testable = true,
        SafetyClassification = "Remove voltage before changing wiring", Notes = "Controlled bench input",
        RecordA = 0, RecordB = 12, LogicalCard = 0, LogicalChannel = 0,
        RecordAText = "Throttle low", RecordAValue = "LOW", RecordBText = "Throttle high",
        RecordBValue = "HIGH", RecordType = 1, PairRelationship = "0 <-> 12"
    };

    private static void Capture(RcmPinProfile input, RcmElectricalTestState state, byte value)
    {
        var coordinator = new RcmCaptureWindowCoordinator();
        coordinator.Begin(input, state, FixedTime);
        coordinator.AddFrame(FixedTime.AddMilliseconds(10), Frame(0xFB, 0xFB, 0x38, value, 0xFF));
        coordinator.Stop(FixedTime.AddSeconds(2));
    }

    private static OtmrLiveFrame Frame(params byte[] bytes) =>
        Assert.Single(new OtmrLiveFrameAssembler().Append(bytes));

    private static (CcfDocument Document, byte[] Source) LoadCcf()
    {
        string path = FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf");
        byte[] source = File.ReadAllBytes(path);
        return (CcfParser.Load(path), source);
    }

    private static string TempJsonPath() =>
        Path.Combine(Path.GetTempPath(), $"rcm_profile_{Guid.NewGuid():N}.json");

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static string FindFromRoot(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new Xunit.Sdk.XunitException($"Fixture not found: {Path.Combine(parts)}");
    }
}
