using CcfEditor.Core;
using CcfEditor.Otmr.Live;

namespace CcfEditor.WinForms;

public partial class MainForm
{
    internal CcfDocument? GetCurrentCcfForBench() => _document;

    internal void ReportOtmrLiveFrame(DateTimeOffset timestamp, OtmrLiveFrame frame) =>
        otmrBenchControl.ReportRawLiveFrame(timestamp, frame);
}
