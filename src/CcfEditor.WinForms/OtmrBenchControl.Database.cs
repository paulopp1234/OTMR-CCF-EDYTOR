using System.Security.Cryptography;
using System.Text;
using CcfEditor.Otmr.Rcm;
using CcfEditor.Otmr.Storage;

namespace CcfEditor.WinForms;

public partial class OtmrBenchControl
{
    private IOtmrRecordingStore? _recordingStore;
    private bool _databaseHooksInstalled;
    private Guid? _databaseCaptureInputId;
    private RcmElectricalTestState? _databaseCaptureState;

    internal void SetRecordingStore(IOtmrRecordingStore? recordingStore)
    {
        _recordingStore = recordingStore;
        if (_databaseHooksInstalled)
            return;

        _databaseHooksInstalled = true;
        captureVoltageRemovedButton.Click += DatabaseCaptureVoltageRemovedAfterClick;
        captureVoltageAppliedButton.Click += DatabaseCaptureVoltageAppliedAfterClick;
        captureWindowTimer.Tick += DatabaseCaptureWindowAfterTick;
        compareStatesButton.Click += DatabaseCompareAfterClick;
    }

    internal RcmProfileRecordingContext GetRecordingProfileContext()
    {
        if (_rcmProfile is null)
            return new RcmProfileRecordingContext(null, null, null, null, null);

        string json = RcmProfileJson.SerializeSnapshot(_rcmProfile);
        string sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        string? filename = _rcmProfilePath is null ? null : Path.GetFileName(_rcmProfilePath);
        return new RcmProfileRecordingContext(
            filename,
            sha256,
            json,
            null,
            _rcmProfile.VehicleType);
    }

    private void DatabaseCaptureVoltageRemovedAfterClick(object? sender, EventArgs e) =>
        RememberDatabaseCapture(RcmElectricalTestState.VoltageRemoved);

    private void DatabaseCaptureVoltageAppliedAfterClick(object? sender, EventArgs e) =>
        RememberDatabaseCapture(RcmElectricalTestState.VoltageApplied24V);

    private void RememberDatabaseCapture(RcmElectricalTestState state)
    {
        if (_recordingStore?.IsRecording != true || !_captureCoordinator.IsCapturing)
            return;

        _databaseCaptureInputId = _captureCoordinator.ActiveInputId;
        _databaseCaptureState = state;
    }

    private void DatabaseCaptureWindowAfterTick(object? sender, EventArgs e)
    {
        if (_recordingStore?.IsRecording != true ||
            _rcmProfile is null ||
            _databaseCaptureInputId is not Guid inputId ||
            _databaseCaptureState is not RcmElectricalTestState state)
        {
            _databaseCaptureInputId = null;
            _databaseCaptureState = null;
            return;
        }

        try
        {
            RcmPinProfile pin = _rcmProfile.GetInput(inputId);
            RcmStateEvidence evidence = state == RcmElectricalTestState.VoltageRemoved
                ? pin.VoltageRemoved
                : pin.VoltageApplied24V;
            _recordingStore.TryRecordRcmCapture(pin, state, evidence);
        }
        finally
        {
            _databaseCaptureInputId = null;
            _databaseCaptureState = null;
        }
    }

    private void DatabaseCompareAfterClick(object? sender, EventArgs e)
    {
        if (_recordingStore?.IsRecording != true || SelectedProfilePin() is not RcmPinProfile pin)
            return;
        if (pin.Comparison.ComparedAt is null)
            return;
        _recordingStore.TryRecordRcmComparison(pin);
    }
}
