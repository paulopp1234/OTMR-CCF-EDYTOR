using System.Security.Cryptography;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.WinForms;

/// <summary>
/// Read-only presentation of explicitly verified RCM mappings decoded from
/// the single OTMR live stream owned by <see cref="OtmrLiveControl"/>.
/// This control never opens, closes, starts, or restarts serial transport.
/// </summary>
public partial class OtmrRcmLiveControl : UserControl
{
    private readonly RcmVerifiedLiveDecoder _verifiedLiveDecoder = new();
    private RcmProfile? _activeRcmProfile;
    private string? _activeRcmProfilePath;
    private OtmrLiveState _liveState = OtmrLiveState.Disconnected;
    private readonly Dictionary<Guid, CurrentObservedSignal> _currentSessionStates = new();
    private string? _sourceConnectionId;
    private int _verifiedMappingCount;
    private int _latestFrameDecodedCount;
    private DateTimeOffset? _latestFrameTimestamp;
    private string? _activeRcmProfileSha256;

    internal event EventHandler<VerifiedLiveStateDecodedEventArgs>? VerifiedLiveStateDecoded;

    public OtmrRcmLiveControl()
    {
        InitializeComponent();
        RefreshStatusAndGrid();
    }

    internal void SetActiveRcmProfile(RcmProfile? profile, string? profilePath)
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => SetActiveRcmProfile(profile, profilePath)));
            return;
        }

        bool hasLoadedJson = profile is not null && !string.IsNullOrWhiteSpace(profilePath);
        _activeRcmProfile = hasLoadedJson ? profile : null;
        _activeRcmProfilePath = hasLoadedJson ? Path.GetFullPath(profilePath!) : null;
        _activeRcmProfileSha256 = _activeRcmProfilePath is not null && File.Exists(_activeRcmProfilePath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_activeRcmProfilePath)))
            : null;
        _verifiedMappingCount = _verifiedLiveDecoder.DescribeVerifiedMappings(_activeRcmProfile).Count;
        ClearCurrentSessionStates();
        RefreshStatusAndGrid();
    }

    internal void SetSourceConnectionId(string? sourceConnectionId)
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => SetSourceConnectionId(sourceConnectionId)));
            return;
        }

        string? normalized = string.IsNullOrWhiteSpace(sourceConnectionId)
            ? null
            : sourceConnectionId.Trim();
        if (string.Equals(_sourceConnectionId, normalized, StringComparison.Ordinal))
            return;

        _sourceConnectionId = normalized;
        ClearCurrentSessionStates();
        RefreshStatusAndGrid();
    }

    internal void SetOtmrLiveState(OtmrLiveState state)
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => SetOtmrLiveState(state)));
            return;
        }

        _liveState = state;
        RefreshHeaderStatus();
    }

    internal void ReportRawLiveFrame(
        DateTimeOffset timestamp,
        OtmrLiveFrame frame,
        CcfEditor.Otmr.Capture.OtmrCaptureEntry? completingCaptureEntry = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => ReportRawLiveFrame(timestamp, frame, completingCaptureEntry)));
            return;
        }

        IReadOnlyList<RcmVerifiedLiveSignal> decodedSignals =
            _verifiedLiveDecoder.Decode(frame, _activeRcmProfile);
        _latestFrameDecodedCount = 0;
        foreach (RcmVerifiedLiveSignal signal in decodedSignals.Where(IsCurrentObservedState))
        {
            _latestFrameDecodedCount++;
            if (_sourceConnectionId is not null)
            {
                _currentSessionStates[signal.PinId] = new CurrentObservedSignal(
                    signal,
                    timestamp.ToUniversalTime());
            }
        }
        _latestFrameTimestamp = timestamp;
        RefreshStatusAndGrid();
        VerifiedLiveStateDecoded?.Invoke(this, new VerifiedLiveStateDecodedEventArgs(
            timestamp,
            _activeRcmProfilePath is null ? null : Path.GetFileName(_activeRcmProfilePath),
            _activeRcmProfileSha256,
            decodedSignals.ToArray(),
            frame,
            completingCaptureEntry));
    }

    internal IReadOnlyList<RcmVerifiedLiveSignal> GetDecodedSignalSnapshot() =>
        _currentSessionStates.Values.Select(current => current.Signal).ToArray();

    internal (string? Filename, string? Sha256) GetActiveProfileIdentity() =>
        (_activeRcmProfilePath is null ? null : Path.GetFileName(_activeRcmProfilePath),
            _activeRcmProfileSha256);

    internal int GetVerifiedMappingCount() => _verifiedMappingCount;

    private void RefreshStatusAndGrid()
    {
        RefreshDecodedSignalsGrid();
        RefreshHeaderStatus();
    }

    private void RefreshHeaderStatus()
    {
        rcmProfileStatusLabel.Text = _activeRcmProfile is null
            ? "RCM profile: No RCM profile loaded"
            : $"RCM profile: {Path.GetFileName(_activeRcmProfilePath)}";
        sourceCcfStatusLabel.Text = _activeRcmProfile is null
            ? "Source CCF: -"
            : $"Source CCF: {_activeRcmProfile.SourceCcfFilename}";
        verifiedMappingsStatusLabel.Text = $"Verified mappings: {_verifiedMappingCount:N0}" +
            (_latestFrameTimestamp is null
                ? string.Empty
                : $" | frame {_latestFrameTimestamp.Value.ToLocalTime():HH:mm:ss.fff}");
        liveStateStatusLabel.Text = _liveState switch
        {
            OtmrLiveState.LiveReady => "Live state: OTMR LIVE READY - waiting for input events",
            OtmrLiveState.LiveActive => "Live state: OTMR LIVE STREAM ACTIVE",
            OtmrLiveState.Disconnected => "Live state: DISCONNECTED",
            _ => $"Live state: {_liveState}"
        };
    }

    private void RefreshDecodedSignalsGrid()
    {
        decodedSignalsGrid.SuspendLayout();
        try
        {
            decodedSignalsGrid.Rows.Clear();
            foreach (CurrentObservedSignal current in _currentSessionStates.Values
                         .OrderBy(item => item.Signal.Connector, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Signal.Pin, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Signal.PinId))
            {
                RcmVerifiedLiveSignal signal = current.Signal;
                string physical = $"{signal.Connector}-{signal.Pin}";
                string logical = signal.LogicalCard.HasValue && signal.LogicalChannel.HasValue
                    ? $"Card {signal.LogicalCard} / Ch {signal.LogicalChannel}"
                    : "-";
                string state = signal.State switch
                {
                    RcmDecodedElectricalState.Active => "ACTIVE",
                    RcmDecodedElectricalState.Inactive => "INACTIVE",
                    _ => "UNKNOWN"
                };
                string raw = signal.RawObservedValue.HasValue
                    ? signal.BitIndex.HasValue
                        ? $"pos {signal.RawPosition} bit {signal.BitIndex} = {signal.ObservedBitValue} (raw {signal.RawObservedValue:X2})"
                        : signal.ObservedFramePosition.HasValue &&
                          signal.ObservedFramePosition.Value != signal.RawPosition
                            ? $"event pos {signal.ObservedFramePosition} = {signal.RawObservedValue:X2} " +
                              $"(verified record pos {signal.RawPosition})"
                            : $"pos {signal.RawPosition} = {signal.RawObservedValue:X2}"
                    : signal.Detail;
                int rowIndex = decodedSignalsGrid.Rows.Add(
                    physical,
                    signal.Function,
                    logical,
                    state,
                    raw,
                    signal.VerificationStatus);
                DataGridViewRow row = decodedSignalsGrid.Rows[rowIndex];
                row.DefaultCellStyle.ForeColor = signal.State switch
                {
                    RcmDecodedElectricalState.Active => Color.DarkGreen,
                    RcmDecodedElectricalState.Inactive => SystemColors.ControlText,
                    _ => Color.DarkRed
                };
                row.Cells[decodedRawColumn.Index].ToolTipText =
                    $"{signal.Detail}\r\nLast observed: {current.ObservedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}";
            }
        }
        finally
        {
            decodedSignalsGrid.ResumeLayout();
        }

        decodedSignalsStatusLabel.Text = _activeRcmProfile is null
            ? "No RCM profile loaded."
            : $"Observed this session: {_currentSessionStates.Count:N0} | " +
              $"Verified mappings available: {_verifiedMappingCount:N0} | " +
              $"Latest frame decoded: {_latestFrameDecodedCount:N0}";
    }

    private void ClearCurrentSessionStates()
    {
        _currentSessionStates.Clear();
        _latestFrameDecodedCount = 0;
        _latestFrameTimestamp = null;
    }

    private static bool IsCurrentObservedState(RcmVerifiedLiveSignal signal) =>
        signal.PinId != Guid.Empty &&
        string.Equals(signal.VerificationStatus, RcmVerificationStates.Verified, StringComparison.Ordinal) &&
        signal.State is RcmDecodedElectricalState.Active or RcmDecodedElectricalState.Inactive &&
        signal.RawObservedValue is >= byte.MinValue and <= byte.MaxValue;

    private sealed record CurrentObservedSignal(
        RcmVerifiedLiveSignal Signal,
        DateTimeOffset ObservedUtc);
}

internal sealed class VerifiedLiveStateDecodedEventArgs(
    DateTimeOffset timestampUtc,
    string? profileFilename,
    string? profileSha256,
    IReadOnlyList<RcmVerifiedLiveSignal> signals,
    OtmrLiveFrame frame,
    CcfEditor.Otmr.Capture.OtmrCaptureEntry? completingCaptureEntry) : EventArgs
{
    public DateTimeOffset TimestampUtc { get; } = timestampUtc.ToUniversalTime();
    public string? ProfileFilename { get; } = profileFilename;
    public string? ProfileSha256 { get; } = profileSha256;
    public IReadOnlyList<RcmVerifiedLiveSignal> Signals { get; } = signals;
    public OtmrLiveFrame Frame { get; } = frame;
    public CcfEditor.Otmr.Capture.OtmrCaptureEntry? CompletingCaptureEntry { get; } = completingCaptureEntry;
}
