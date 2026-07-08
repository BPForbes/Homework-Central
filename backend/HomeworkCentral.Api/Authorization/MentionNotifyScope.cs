namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Who may receive a mention notification from a given sender. Real production accounts
/// may only notify other real accounts. Developer accounts may only notify dev-side users
/// within their tenant relationship; DevAdmin may notify any developer persona.
/// </summary>
public static class MentionNotifyScope
{
    public static bool CanNotify(
        AccountClass senderAccountClass,
        string? senderTenantDatabaseName,
        AccountClass recipientAccountClass,
        string? recipientTenantDatabaseName)
    {
        if (senderAccountClass == AccountClass.RealAccount)
            return recipientAccountClass == AccountClass.RealAccount;

        if (recipientAccountClass == AccountClass.RealAccount)
            return false;

        if (senderAccountClass == AccountClass.DevAdmin)
            return recipientAccountClass is AccountClass.DeveloperAccount or AccountClass.DevAdmin;

        if (senderAccountClass == AccountClass.DeveloperAccount)
        {
            if (recipientAccountClass == AccountClass.DevAdmin)
                return true;

            return recipientAccountClass == AccountClass.DeveloperAccount
                && string.Equals(senderTenantDatabaseName, recipientTenantDatabaseName, StringComparison.Ordinal);
        }

        return false;
    }
}
