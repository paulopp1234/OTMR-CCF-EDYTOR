namespace CcfEditor.Otmr.Server;

public sealed class OtmrServerOptions
{
    public const string SectionName = "OtmrServer";

    public string DatabasePath { get; set; } = "/var/lib/otmr/OTMR_RCM.db";
    public string BearerToken { get; set; } = string.Empty;
    public string ListenUrl { get; set; } = "http://127.0.0.1:5080";
    public long MaximumUploadBodyBytes { get; set; } = 134_217_728;
    public int MaximumQueryResultCount { get; set; } = 1_000;
    public int MaximumSessionResultCount { get; set; } = 100;
    public int MaximumRecordRangeDays { get; set; } = 31;
    public int MaximumRealtimeSignalUpdates { get; set; } = 1_000;
    public int LiveStaleAfterSeconds { get; set; } = 30;
    public int WindowsAppOfflineAfterSeconds { get; set; } = 30;
}
