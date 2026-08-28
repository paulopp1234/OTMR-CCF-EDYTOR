using System.Diagnostics;
using CcfEditor.Otmr.Storage;

namespace CcfEditor.WinForms;

public partial class OtmrLiveControl
{
    private IOtmrRecordingStore? _recordingStore;

    internal void SetRecordingStore(IOtmrRecordingStore? recordingStore)
    {
        if (ReferenceEquals(_recordingStore, recordingStore))
            return;

        if (_recordingStore is not null)
            _recordingStore.StatusChanged -= RecordingStore_StatusChanged;

        _recordingStore = recordingStore;
        if (_recordingStore is not null)
            _recordingStore.StatusChanged += RecordingStore_StatusChanged;

        _liveService.SetRecordingStore(recordingStore);
        UpdateDatabaseRecordingUi();
        Disposed -= OtmrLiveControl_Disposed;
        Disposed += OtmrLiveControl_Disposed;
    }

    private async void StartDatabaseRecordingButton_Click(object? sender, EventArgs e)
    {
        if (_recordingStore is null)
        {
            MessageBox.Show(this, "The local recording database is not available.", "Database recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (portComboBox.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
        {
            MessageBox.Show(this, "Select the OTMR COM port before starting database recording.", "Database recording", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            OtmrRecordingSessionContext context = (FindForm() as MainForm)?.CreateRecordingSessionContext(portName)
                ?? new OtmrRecordingSessionContext
                {
                    SoftwareVersion = Application.ProductVersion,
                    ComPort = portName,
                    SerialSettings = "38400/8/N/1"
                };
            Guid sessionId = await _recordingStore.StartSessionAsync(context);
            databaseMessageLabel.Text = $"Recording session {sessionId:D}. ALL front RS232 RX/TX chunks and complete FB FB … FF frames are retained.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to start database recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateDatabaseRecordingUi();
        }
    }

    private async void StopDatabaseRecordingButton_Click(object? sender, EventArgs e)
    {
        if (_recordingStore is null)
            return;

        try
        {
            await _recordingStore.StopSessionAsync(DateTimeOffset.UtcNow);
            databaseMessageLabel.Text = "Recording stopped and flushed to SQLite. Session queued as PENDING_UPLOAD; use the SERVER SYNC tab when ready.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to stop database recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateDatabaseRecordingUi();
        }
    }

    private void OpenDatabaseFolderButton_Click(object? sender, EventArgs e)
    {
        if (_recordingStore is null)
            return;

        try
        {
            string path = _recordingStore.DatabasePath;
            string directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
            Directory.CreateDirectory(directory);
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to open database folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RecordingStore_StatusChanged(object? sender, OtmrRecordingStatusChangedEventArgs e)
    {
        if (_closing || IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke((Action)UpdateDatabaseRecordingUi);
            return;
        }
        UpdateDatabaseRecordingUi();
    }

    private void UpdateDatabaseRecordingUi()
    {
        if (databaseRecordingGroupBox is null)
            return;

        OtmrRecordingStatus? status = _recordingStore?.GetStatus();
        bool recording = status?.IsRecording == true;
        startDatabaseRecordingButton.Enabled = _recordingStore is not null && !recording;
        stopDatabaseRecordingButton.Enabled = recording;

        databaseRecordingStateLabel.Text = recording ? "RECORDING" : "STOPPED";
        databaseRecordingStateLabel.ForeColor = recording ? Color.DarkGreen : Color.DimGray;
        databaseSessionLabel.Text = status?.SessionId is Guid id ? $"Session: {id:D}" : "Session: -";
        databaseCountsLabel.Text = status is null
            ? "Raw entries: 0 | Complete frames: 0"
            : $"Raw entries: {status.RawEntryCount:N0} | Complete frames: {status.CompleteFrameCount:N0}";
        databasePathLabel.Text = status?.DatabasePath ?? OtmrDatabasePaths.DefaultDatabasePath;
    }

    private void OtmrLiveControl_Disposed(object? sender, EventArgs e)
    {
        if (_recordingStore is not null)
            _recordingStore.StatusChanged -= RecordingStore_StatusChanged;
    }
}
