namespace CcfEditor.Otmr.Rcm;

public static class RcmInputFilter
{
    public const string All = "ALL";
    public const string Unassigned = "UNASSIGNED";

    public static IEnumerable<RcmPinProfile> Apply(RcmProfile profile, string? filter)
    {
        ArgumentNullException.ThrowIfNull(profile);
        filter = string.IsNullOrWhiteSpace(filter) ? All : filter.Trim();
        if (string.Equals(filter, All, StringComparison.OrdinalIgnoreCase))
            return profile.Pins;
        if (string.Equals(filter, Unassigned, StringComparison.OrdinalIgnoreCase))
            return profile.Pins.Where(pin => !pin.PhysicalMappingAssigned);
        return profile.Pins.Where(pin =>
            string.Equals(pin.Connector, filter, StringComparison.OrdinalIgnoreCase));
    }
}
