using CcfEditor.Core;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.WinForms;

public partial class MainForm
{
    internal CcfDocument? GetCurrentCcfForBench() => _document;

    internal void ReportOtmrLiveFrame(DateTimeOffset timestamp, OtmrLiveFrame frame) =>
        otmrBenchControl.ReportRawLiveFrame(timestamp, frame);

    internal void ReportOtmrLiveState(OtmrLiveState state) =>
        otmrBenchControl.SetOtmrLiveState(state);

    internal void ReportActiveRcmProfileChanged(RcmProfile? profile) =>
        otmrLiveControl.SetActiveRcmProfile(profile);
}
