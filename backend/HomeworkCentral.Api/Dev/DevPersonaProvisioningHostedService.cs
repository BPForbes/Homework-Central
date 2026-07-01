namespace HomeworkCentral.Api.Dev;

/// <summary>Provisions persona tenant databases in the background so Kestrel can start immediately.</summary>
public sealed class DevPersonaProvisioningHostedService(IDevPersonaProvisioner provisioner) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        provisioner.ProvisionAllRemainingAsync(stoppingToken);
}
