using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace HomeworkCentral.Api.Legacy;

/// <summary>Transitional helpers retained from the .NET 8 stack. Do not use in new code.</summary>
public static class LegacyForwardedHeaders
{
    [Obsolete("NET 10: use ForwardedHeadersOptions.KnownIPNetworks instead of KnownNetworks.")]
    public static void ClearKnownNetworks(ForwardedHeadersOptions options) =>
        options.KnownNetworks.Clear();
}
