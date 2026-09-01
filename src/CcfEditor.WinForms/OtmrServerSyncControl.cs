using CcfEditor.Otmr.Storage;
using CcfEditor.Otmr.Sync;

namespace CcfEditor.WinForms;

/// <summary>
/// Deliberate, operator-driven server synchronization UI. Constructing or
/// displaying this control performs local settings/status reads only; network
/// access is restricted to the two explicit operator buttons.
/// </summary>
public partial class OtmrServerSyncControl : UserControl
{
    private IOtmrRecordingStore? _recordingStore;
    private OtmrSyncUserSettingsStore? _syncSettingsStore;
    private OtmrSyncUserSettings _syncSettings = new();
    private OtmrManualSyncService? _manualSyncService;
    private HttpClient? _syncHttpClient;
    private HttpClient? _realtimeHttpClient;
    private OtmrRealtimePublisher _realtimePublisher;
    private bool _syncBusy;
    private bool _closing;
    private OtmrRealtimeSourceDiagnostics? _latestRealtimeSourceDiagnostics;

    public OtmrServerSyncControl()
    {
        InitializeComponent();
        _realtimeHttpClient = new HttpClient();
        _realtimePublisher = new OtmrRealtimePublisher(_realtimeHttpClient);
        _realtimePublisher.StatusChanged += RealtimePublisher_StatusChanged;
        InitializeManualSyncUi();
        Disposed += OtmrServerSyncControl_Disposed;
    }

    internal void SetRecordingStore(IOtmrRecordingStore? recordingStore)
    {
        if (ReferenceEquals(_recordingStore, recordingStore))
            return;

        if (_recordingStore is not null)
            _recordingStore.StatusChanged -= RecordingStore_StatusChanged;

        _recordingStore = recordingStore;
        if (_recordingStore is not null)
            _recordingStore.StatusChanged += RecordingStore_StatusChanged;

        ConfigureManualSyncService();
    }

    private void InitializeManualSyncUi()
    {
        _syncSettingsStore = new OtmrSyncUserSettingsStore();
        try
        {
            _syncSettings = _syncSettingsStore.Load();
            ApplySavedSyncSettingsToUi();
        }
        catch (Exception ex)
        {
            _syncSettings = new OtmrSyncUserSettings();
            ApplySavedSyncSettingsToUi();
            syncLastErrorLabel.Text = $"Last error: User settings could not be loaded: {ex.Message}";
        }
        UpdateManualSyncButtons();
    }

    private void ConfigureManualSyncService()
    {
        _syncHttpClient?.Dispose();
        _syncHttpClient = null;
        _manualSyncService = null;
        if (_recordingStore is null)
        {
            UpdateManualSyncButtons();
            return;
        }

        _syncHttpClient = new HttpClient();
        _manualSyncService = new OtmrManualSyncService(_recordingStore, _syncHttpClient);
        UpdateManualSyncButtons();
        _ = RefreshManualSyncStatusAsync(); // Local SQLite status only; never contacts the server.
    }

    private void ApplySavedSyncSettingsToUi()
    {
        syncServerUrlTextBox.Text = _syncSettings.ServerUrl;
        syncEnabledCheckBox.Checked = _syncSettings.SyncEnabled;
        realtimePublishingCheckBox.Checked = _syncSettings.RealtimePublishingEnabled;
        syncAllowHttpTestServerCheckBox.Checked = _syncSettings.AllowInsecureKnownTestServer;
        syncApiTokenTextBox.Clear();
        syncApiTokenTextBox.PlaceholderText = _syncSettings.HasApiToken
            ? "Token saved securely - enter a new token to replace it"
            : "Enter token to save";
        _realtimePublisher.Configure(_syncSettings.ToRealtimeConfiguration());
    }

    private OtmrSyncUserSettings SaveSyncSettingsFromUi()
    {
        string replacementToken = syncApiTokenTextBox.Text;
        var proposed = new OtmrSyncUserSettings
        {
            ServerUrl = syncServerUrlTextBox.Text.Trim(),
            ApiToken = string.IsNullOrEmpty(replacementToken) ? _syncSettings.ApiToken : replacementToken,
            SyncEnabled = syncEnabledCheckBox.Checked,
            RealtimePublishingEnabled = realtimePublishingCheckBox.Checked,
            AllowInsecureKnownTestServer = syncAllowHttpTestServerCheckBox.Checked
        };

        proposed.ToManualConfiguration().ValidateServerUri();
        _syncSettingsStore!.Save(proposed);
        _syncSettings = proposed;
        _realtimePublisher.Configure(proposed.ToRealtimeConfiguration());
        syncApiTokenTextBox.Clear();
        syncApiTokenTextBox.PlaceholderText = proposed.HasApiToken
            ? "Token saved securely - enter a new token to replace it"
            : "Enter token to save";
        UpdateManualSyncButtons();
        return proposed;
    }

    internal bool PublishVerifiedLiveState(OtmrRealtimeDecodedState decodedState) =>
        _realtimePublisher.TryPublish(decodedState);

    internal bool StartRealtimeLiveSession(OtmrRealtimeSessionStart sessionStart) =>
        _realtimePublisher.TryStartSession(sessionStart);

    internal OtmrRealtimePublisherStatus RealtimePublisherStatus => _realtimePublisher.Status;

    internal OtmrRealtimePublisherDiagnostics RealtimePublisherDiagnostics => _realtimePublisher.Diagnostics;

    internal void ReportRealtimeSourceDiagnostics(OtmrRealtimeSourceDiagnostics diagnostics)
    {
        _latestRealtimeSourceDiagnostics = diagnostics;
        ApplyRealtimeSourceDiagnostics(diagnostics);
    }

    private void ApplyRealtimeSourceDiagnostics(OtmrRealtimeSourceDiagnostics diagnostics)
    {
        OtmrRealtimePublisherDiagnostics publisher = _realtimePublisher.Diagnostics;
        realtimeDiagnosticsLabel.Text =
            $"Frames received: {diagnostics.FramesReceived:N0} | Frames decoded: {diagnostics.FramesDecoded:N0} | " +
            $"Verified state changes: {publisher.VerifiedStateChanges:N0} | " +
            $"Publish attempts/successes/failures: {publisher.PublishAttempts:N0}/" +
            $"{publisher.PublishSuccesses:N0}/{publisher.PublishFailures:N0}\r\n" +
            $"RCM: {diagnostics.RcmFilename ?? "-"} | Verified mappings: {diagnostics.VerifiedMappingCount:N0} | " +
            $"Vehicle ID: {diagnostics.VehicleIdentifier ?? "UNKNOWN"} | " +
            $"Decoded states: {diagnostics.DecodedStateCount:N0} | " +
            $"Publisher instance enabled: {_realtimePublisher.Status.Enabled}";
    }

    internal void ConfigureRealtimePublisherForTests(
        OtmrRealtimePublisher publisher,
        OtmrRealtimePublisherConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(configuration);
        _realtimePublisher.StatusChanged -= RealtimePublisher_StatusChanged;
        _realtimePublisher.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _realtimeHttpClient?.Dispose();
        _realtimeHttpClient = null;
        _realtimePublisher = publisher;
        _realtimePublisher.StatusChanged += RealtimePublisher_StatusChanged;
        _realtimePublisher.Configure(configuration);
        realtimePublishingCheckBox.Checked = configuration.Enabled;
        ApplyRealtimePublisherStatus(_realtimePublisher.Status);
    }

    private void RealtimePublisher_StatusChanged(object? sender, OtmrRealtimePublisherStatusChangedEventArgs e)
    {
        if (_closing || IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => ApplyRealtimePublisherStatus(e.Status)));
            return;
        }
        ApplyRealtimePublisherStatus(e.Status);
    }

    private void ApplyRealtimePublisherStatus(OtmrRealtimePublisherStatus status)
    {
        realtimePublishingStateLabel.Text =
            $"Realtime publishing: {(status.Enabled ? "ENABLED" : "DISABLED")}";
        realtimeLastSendLabel.Text = status.LastSendUtc is null
            ? "Last realtime send: Never"
            : $"Last realtime send: {status.LastSendUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        realtimeLastResultLabel.Text = $"Last realtime result: {status.LastResult}";
        realtimeLastErrorLabel.Text = string.IsNullOrWhiteSpace(status.LastError)
            ? "Last realtime error: -"
            : $"Last realtime error: {status.LastError}";
        realtimeReasonLabel.Text = string.IsNullOrWhiteSpace(status.StatusReason)
            ? "Only genuine state changes from explicitly verified mappings are published."
            : status.StatusReason;
        if (_latestRealtimeSourceDiagnostics is not null)
            ApplyRealtimeSourceDiagnostics(_latestRealtimeSourceDiagnostics);
    }

    private void SaveSyncSettingsButton_Click(object? sender, EventArgs e)
    {
        try
        {
            SaveSyncSettingsFromUi();
            syncLastResultLabel.Text = "Last result: Settings saved in the current Windows user profile";
            syncLastErrorLabel.Text = "Last error: -";
        }
        catch (Exception ex)
        {
            syncLastErrorLabel.Text = $"Last error: {ex.Message}";
        }
    }

    private async void TestServerButton_Click(object? sender, EventArgs e)
    {
        if (_manualSyncService is null)
            return;

        SetManualSyncBusy(true);
        try
        {
            OtmrSyncUserSettings settings = SaveSyncSettingsFromUi();
            OtmrServerConnectivityResult result = await _manualSyncService.TestServerAsync(settings.ToManualConfiguration());
            syncLastResultLabel.Text = $"Last result: {result.Message}";
            syncLastErrorLabel.Text = result.IsAvailable ? "Last error: -" : $"Last error: {result.Message}";
            if (result.IsAvailable)
                MessageBox.Show(this, "SERVER OK", "OTMR server test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            syncLastResultLabel.Text = "Last result: SERVER TEST FAILED";
            syncLastErrorLabel.Text = $"Last error: {ex.Message}";
        }
        finally
        {
            SetManualSyncBusy(false);
        }
    }

    private async void SyncPendingRecordingsButton_Click(object? sender, EventArgs e)
    {
        if (_manualSyncService is null)
            return;

        SetManualSyncBusy(true);
        try
        {
            OtmrSyncUserSettings settings = SaveSyncSettingsFromUi();
            IReadOnlyList<OtmrSyncItemResult> results = await _manualSyncService
                .SynchronizePendingAsync(settings.ToManualConfiguration());
            await RefreshManualSyncStatusAsync();
            int uploaded = results.Count(x => x.Uploaded);
            int failed = results.Count - uploaded;
            syncLastResultLabel.Text = results.Count == 0
                ? "Last result: No pending recordings; local SQLite evidence is unchanged."
                : $"Last result: {uploaded} uploaded, {failed} failed; local SQLite evidence remains intact.";
        }
        catch (Exception ex)
        {
            syncLastResultLabel.Text = "Last result: UPLOAD_FAILED";
            syncLastErrorLabel.Text = $"Last error: {ex.Message}";
            await RefreshManualSyncStatusAsync();
        }
        finally
        {
            SetManualSyncBusy(false);
        }
    }

    private async Task RefreshManualSyncStatusAsync()
    {
        if (_manualSyncService is null || _closing || IsDisposed)
            return;
        try
        {
            OtmrManualSyncStatus status = await _manualSyncService.GetStatusAsync();
            if (_closing || IsDisposed)
                return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => ApplyManualSyncStatus(status)));
                return;
            }
            ApplyManualSyncStatus(status);
        }
        catch (ObjectDisposedException)
        {
            // Normal during form shutdown.
        }
        catch (Exception ex)
        {
            if (!_closing && !IsDisposed)
                syncLastErrorLabel.Text = $"Last error: Unable to read local sync status: {ex.Message}";
        }
    }

    private void ApplyManualSyncStatus(OtmrManualSyncStatus status)
    {
        syncPendingCountLabel.Text = $"Pending recordings: {status.PendingUploadCount:N0}";
        syncCurrentStateLabel.Text = $"Current: {status.CurrentSessionSyncState}";
        syncLastAttemptLabel.Text = status.LastSyncAttemptUtc is null
            ? "Last attempt: Never"
            : $"Last attempt: {status.LastSyncAttemptUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        syncLastResultLabel.Text = $"Last result: {status.LastSyncResult}";
        syncLastErrorLabel.Text = string.IsNullOrWhiteSpace(status.LastError)
            ? "Last error: -"
            : $"Last error: {status.LastError}";
    }

    private void RecordingStore_StatusChanged(object? sender, OtmrRecordingStatusChangedEventArgs e)
    {
        if (!_closing && !IsDisposed)
            _ = RefreshManualSyncStatusAsync();
    }

    private void SyncEnabledCheckBox_CheckedChanged(object? sender, EventArgs e) => UpdateManualSyncButtons();

    private void RealtimePublishingCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (realtimePublishingCheckBox.Checked != _syncSettings.RealtimePublishingEnabled)
            realtimeReasonLabel.Text = "Click SAVE SETTINGS to apply this realtime publishing choice.";
        else
            ApplyRealtimePublisherStatus(_realtimePublisher.Status);
    }

    private void SetManualSyncBusy(bool busy)
    {
        _syncBusy = busy;
        UpdateManualSyncButtons();
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void UpdateManualSyncButtons()
    {
        if (syncPendingRecordingsButton is null)
            return;
        saveSyncSettingsButton.Enabled = !_syncBusy;
        testServerButton.Enabled = !_syncBusy && _manualSyncService is not null;
        syncPendingRecordingsButton.Enabled =
            !_syncBusy && _manualSyncService is not null && syncEnabledCheckBox.Checked;
    }

    private void OtmrServerSyncControl_Disposed(object? sender, EventArgs e)
    {
        _closing = true;
        if (_recordingStore is not null)
            _recordingStore.StatusChanged -= RecordingStore_StatusChanged;
        _manualSyncService = null;
        _syncHttpClient?.Dispose();
        _syncHttpClient = null;
        _realtimePublisher.StatusChanged -= RealtimePublisher_StatusChanged;
        _realtimePublisher.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _realtimeHttpClient?.Dispose();
    }
}

internal sealed record OtmrRealtimeSourceDiagnostics(
    long FramesReceived,
    long FramesDecoded,
    string? RcmFilename,
    int VerifiedMappingCount,
    string? VehicleIdentifier,
    int DecodedStateCount);
