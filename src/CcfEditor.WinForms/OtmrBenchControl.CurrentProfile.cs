using CcfEditor.Otmr.Rcm;

namespace CcfEditor.WinForms;

internal sealed class CurrentRcmProfileChangedEventArgs(
    RcmProfile? profile,
    string? profilePath) : EventArgs
{
    public RcmProfile? Profile { get; } = profile;
    public string? ProfilePath { get; } = profilePath;
    public bool IsLoadedJson => Profile is not null && !string.IsNullOrWhiteSpace(ProfilePath);
}

public partial class OtmrBenchControl
{
    internal event EventHandler<CurrentRcmProfileChangedEventArgs>? CurrentRcmProfileChanged;

    internal RcmProfile? CurrentRcmProfile => _rcmProfile;
    internal string? CurrentRcmProfilePath => _rcmProfilePath;

    internal async Task LoadRcmProfileAsync(string path, CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        RcmProfile loaded = await RcmProfileJson.LoadAsync(fullPath, cancellationToken);
        SetCurrentRcmProfile(loaded, fullPath);
        PopulateConnectors(resetToAll: true);
        RenderTable();
        UpdateCcfStatus();
    }

    private void SetCurrentRcmProfile(RcmProfile? profile, string? profilePath)
    {
        _rcmProfile = profile;
        _rcmProfilePath = profilePath is null ? null : Path.GetFullPath(profilePath);
        NotifyCurrentRcmProfileChanged();
    }

    private void NotifyCurrentRcmProfileChanged() =>
        CurrentRcmProfileChanged?.Invoke(
            this,
            new CurrentRcmProfileChangedEventArgs(_rcmProfile, _rcmProfilePath));
}
