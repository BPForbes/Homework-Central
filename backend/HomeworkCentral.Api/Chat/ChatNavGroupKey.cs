using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Chat;

public static class ChatNavGroupKey
{
    public static string Build(AccountClass accountClass) =>
        accountClass == AccountClass.RealAccount ? "nav:real" : "nav:dev";
}
