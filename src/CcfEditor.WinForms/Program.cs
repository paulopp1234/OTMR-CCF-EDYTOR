namespace CcfEditor.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Fail closed: the main form is never constructed unless this app's
        // dedicated OTMR_RCM control file contains only ALLOW_START after trimming.
        StartupGateResult gate = StartupGate.Check();
        if (!gate.IsAllowed)
        {
            MessageBox.Show(
                $"OTMR RCM start is not authorised.{Environment.NewLine}{Environment.NewLine}" +
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
