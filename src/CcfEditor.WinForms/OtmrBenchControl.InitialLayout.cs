using System.ComponentModel;

namespace CcfEditor.WinForms;

public partial class OtmrBenchControl
{
    private bool _finalInitialSplitterScheduled;
    private bool _finalInitialSplitterApplied;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime ||
            _finalInitialSplitterApplied ||
            _finalInitialSplitterScheduled ||
            !Visible ||
            !IsHandleCreated ||
            IsDisposed)
        {
            return;
        }

        _finalInitialSplitterScheduled = true;
        BeginInvoke((Action)(() =>
        {
            _finalInitialSplitterScheduled = false;
            if (IsDisposed || !Visible || mainSplit.Width <= 0)
                return;

            // OnLoad can run while the parent is still at an intermediate size.
            // Reapply the intended initial 70/30 split once after visible layout
            // has settled. After this one correction the splitter remains fully
            // user-adjustable and is never forced again.
            _initialSplitterPositionApplied = false;
            ApplyInitialSplitterPosition();
            _finalInitialSplitterApplied = true;
        }));
    }
}
