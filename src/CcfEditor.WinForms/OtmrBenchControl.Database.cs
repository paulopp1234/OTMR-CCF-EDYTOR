using System.Security.Cryptography;
using System.Text;
using CcfEditor.Otmr.Rcm;
using CcfEditor.Otmr.Storage;

namespace CcfEditor.WinForms;

public partial class OtmrBenchControl
{
    private IOtmrRecordingStore? _recordingStore;
    private bool _databaseHooksInstalled;

    internal void SetRecordingStore(IOtmrRecordingStore? recordingStore)
    {
        _recordingStore = recordingStore;
        if (_databaseHooksInstalled)
            return;

        _databaseHooksInstalled = true;
        _captureCoordinator.CaptureCompleted += DatabaseCaptureCompleted;
        _captureCoordinator.ComparisonCompleted += DatabaseComparisonCompleted;
        Disposed += OtmrBenchControl_DatabaseDisposed;
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

    private void DatabaseCaptureCompleted(object? sender, RcmCaptureCompletedEventArgs e)
    {
        if (_recordingStore?.IsRecording != true)
            return;
        _recordingStore.TryRecordRcmCapture(e.Pin, e.State, e.Evidence);
    }

    private void DatabaseComparisonCompleted(object? sender, RcmComparisonCompletedEventArgs e)
    {
        if (_recordingStore?.IsRecording != true || e.Pin.Comparison.ComparedAt is null)
            return;
        _recordingStore.TryRecordRcmComparison(e.Pin);
    }

    private void OtmrBenchControl_DatabaseDisposed(object? sender, EventArgs e)
    {
        _captureCoordinator.CaptureCompleted -= DatabaseCaptureCompleted;
        _captureCoordinator.ComparisonCompleted -= DatabaseComparisonCompleted;
    }
}
