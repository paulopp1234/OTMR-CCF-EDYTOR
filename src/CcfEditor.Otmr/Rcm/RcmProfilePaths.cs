namespace CcfEditor.Otmr.Rcm;

public static class RcmProfilePaths
{
    public const string DefaultFolder = @"C:\OTMR_CCF\RCM PROFILES";

    public static string EnsureDefaultFolder()
    {
        Directory.CreateDirectory(DefaultFolder);
        return DefaultFolder;
    }
}
