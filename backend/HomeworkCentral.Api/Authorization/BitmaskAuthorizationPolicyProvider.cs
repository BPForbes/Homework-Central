using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Resolves bitmask policies on demand so every <see cref="AuthorizationPolicyNames.For"/> name works
/// without registering each bit individually at startup.
/// </summary>
public sealed class BitmaskAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (TryParseBitmaskPolicy(policyName, out MaskType maskType, out short bit, out string? subjectCategory))
        {
            AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new BitmaskRequirement(maskType, bit, subjectCategory))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    internal static bool TryParseBitmaskPolicy(
        string policyName,
        out MaskType maskType,
        out short bit,
        out string? subjectCategory)
    {
        maskType = default;
        bit = 0;
        subjectCategory = null;

        if (!policyName.StartsWith("mask:", StringComparison.Ordinal))
            return false;

        string[] parts = policyName.Split(':', StringSplitOptions.None);
        if (parts.Length is not (3 or 4))
            return false;

        if (!Enum.TryParse(parts[1], ignoreCase: false, out maskType))
            return false;

        if (parts.Length == 3)
            return short.TryParse(parts[2], out bit);

        subjectCategory = parts[2];
        return short.TryParse(parts[3], out bit);
    }
}
