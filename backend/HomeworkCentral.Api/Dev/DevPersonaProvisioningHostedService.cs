using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Dev;

/// <summary>
/// Pre-provisions every persona tenant database in the background so Kestrel can start
/// immediately. Runs only when <see cref="DevPersonaEagerProvisioning"/> is opted in; by
/// default personas provision on demand at dev login, so only the ones actually being used
/// pay the migrate/seed cost.
/// </summary>
public sealed class DevPersonaProvisioningHostedService(
    IDevPersonaProvisioner provisioner,
    IConfiguration configuration,
    ILogger<DevPersonaProvisioningHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!DevPersonaEagerProvisioning.IsEnabled(configuration))
        {
            logger.LogInformation(
                "Persona databases provision on demand at dev login. Set {Flag}=1 to pre-provision all {Total} in the background.",
                DevPersonaEagerProvisioning.EnvVarName,
                provisioner.TotalPersonaCount);
            return;
        }

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
