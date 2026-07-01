using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Dev;

/// <summary>Provisions persona tenant databases in the background so Kestrel can start immediately.</summary>
public sealed class DevPersonaProvisioningHostedService(
    IDevPersonaProvisioner provisioner,
    ILogger<DevPersonaProvisioningHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await provisioner.ProvisionAllRemainingAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            // ProvisionPersonaCoreAsync already logs+rethrows per-persona failures; if that (or
            // anything else) escapes here, contain it rather than letting an unhandled
            // BackgroundService exception take down the whole host — dev bypass logins retry
            // provisioning on demand (see AuthService.DevLoginAsync), so the API stays usable
            // even if background pre-provisioning didn't fully finish.
            logger.LogError(ex, "Background persona provisioning failed; it will be retried on demand at login.");
        }
    }
}
