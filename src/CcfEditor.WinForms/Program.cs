namespace CcfEditor.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Fail closed: the main form is never constructed unless this app's
        // own remote control line is exactly: OTMR CCF EDYTOR - ALLOW_START
        StartupGateResult gate = StartupGate.Check();
        if (!gate.IsAllowed)
        {
            MessageBox.Show(
                $"OTMR CCF Editor start is not authorised.{Environment.NewLine}{Environment.NewLine}" +
                $"Status: {gate.Status}{Environment.NewLine}" +
                gate.Detail,
                "Start blocked",
                MessageBoxButtons.OK,
                MessageBoxIcon.Stop);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
