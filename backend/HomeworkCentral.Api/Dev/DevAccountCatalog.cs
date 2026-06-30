namespace HomeworkCentral.Api.Dev;

/// <summary>A fictional user persona seeded for local dev impersonation.</summary>
public sealed class DevPersonaDefinition
{
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required string[] Roles { get; init; }
}

/// <summary>A subject-area developer account and its impersonation personas.</summary>
public sealed class DevAccountDefinition
{
    public required string DeveloperUsername { get; init; }
    public required string DeveloperEmail { get; init; }
    public required string TenantSlug { get; init; }
    public required DevPersonaDefinition[] Personas { get; init; }

    /// <summary>Isolated Postgres database for this developer group (e.g. tenant_math).</summary>
    public string TenantDatabaseName => $"tenant_{TenantSlug}";
}

/// <summary>
/// Static catalog of dev developer accounts and personas. Emails/usernames must remain
/// unique because they map directly to rows in the Users table.
/// </summary>
public static class DevAccountCatalog
{
    public static IReadOnlyList<DevAccountDefinition> All { get; } =
    [
        ScienceAccount(),
        MathematicsAccount(),
        ComputerScienceAccount(),
        LanguagesAccount(),
        HistoryAccount(),
        BusinessAccount(),
        ArtAccount(),
        MusicAccount(),
        EngineeringAccount(),
        MedicineAccount(),
        FinanceAccount(),
        EconomicsAccount(),
        EducationAccount(),
    ];

    public static DevAccountDefinition? FindByDeveloperEmail(string email) =>
        All.FirstOrDefault(account =>
            string.Equals(account.DeveloperEmail, email, StringComparison.OrdinalIgnoreCase));

    public static DevAccountDefinition? FindByTenantDatabaseName(string databaseName) =>
        All.FirstOrDefault(account =>
            string.Equals(account.TenantDatabaseName, databaseName, StringComparison.OrdinalIgnoreCase));

    public static bool PersonaBelongsToAccount(DevAccountDefinition account, string personaEmail) =>
        account.Personas.Any(persona =>
            string.Equals(persona.Email, personaEmail, StringComparison.OrdinalIgnoreCase));

    /// <summary>Throws if any developer or persona email/username is duplicated in the catalog.</summary>
    public static void ValidateUniquePersonas()
    {
        HashSet<string> emails = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> usernames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> tenantSlugs = new(StringComparer.OrdinalIgnoreCase);

        foreach (DevAccountDefinition account in All)
        {
            if (!tenantSlugs.Add(account.TenantSlug))
                throw new InvalidOperationException($"Duplicate tenant slug in dev catalog: {account.TenantSlug}");

            if (!emails.Add(account.DeveloperEmail))
                throw new InvalidOperationException($"Duplicate developer email in dev catalog: {account.DeveloperEmail}");

            if (!usernames.Add(account.DeveloperUsername))
                throw new InvalidOperationException($"Duplicate developer username in dev catalog: {account.DeveloperUsername}");

            foreach (DevPersonaDefinition persona in account.Personas)
            {
                if (!emails.Add(persona.Email))
                    throw new InvalidOperationException($"Duplicate persona email in dev catalog: {persona.Email}");

                if (!usernames.Add(persona.Username))
                    throw new InvalidOperationException($"Duplicate persona username in dev catalog: {persona.Username}");
            }
        }
    }

    private static DevAccountDefinition ScienceAccount() => new()
    {
        DeveloperUsername = "Science Developer",
        DeveloperEmail = "science.developer@dev.local",
        TenantSlug = "science",
        Personas =
        [
            Persona("Dr. Emmett Brown", "doc.brown@science.dev", "Tutor"),
            Persona("Sir Isaac Newton", "isaac.newton@science.dev", "SeniorTutor"),
            Persona("Marie Curie", "marie.curie@science.dev", "VerifiedEducator"),
            Persona("Albert Einstein", "albert.einstein@science.dev", "Student"),
            Persona("Nikola Tesla", "nikola.tesla@science.dev", "Tutor", "SeminarHost"),
            Persona("Charles Darwin", "charles.darwin@science.dev", "Moderator"),
        ],
    };

    private static DevAccountDefinition MathematicsAccount() => new()
    {
        DeveloperUsername = "Math Developer",
        DeveloperEmail = "math.developer@dev.local",
        TenantSlug = "math",
        Personas =
        [
            Persona("Fibonacci", "fibonacci@math.dev", "Tutor"),
            Persona("Pythagoras", "pythagoras@math.dev", "SeniorTutor"),
            Persona("Ada Lovelace", "ada.lovelace@math.dev", "HeadTutor"),
            Persona("Alan Turing", "alan.turing@math.dev", "Administrator"),
            Persona("Euclid", "euclid@math.dev", "Student"),
            Persona("Katherine Johnson", "katherine.johnson@math.dev", "SeniorTutor", "EventOrganizer"),
        ],
    };

    private static DevAccountDefinition ComputerScienceAccount() => new()
    {
        DeveloperUsername = "Computer Science Developer",
        DeveloperEmail = "cs.developer@dev.local",
        TenantSlug = "cs",
        Personas =
        [
            Persona("Grace Hopper", "grace.hopper@cs.dev", "SeniorTutor"),
            Persona("Dennis Ritchie", "dennis.ritchie@cs.dev", "Tutor"),
            Persona("Margaret Hamilton", "margaret.hamilton@cs.dev", "Administrator"),
            Persona("Guido van Rossum", "guido.vanrossum@cs.dev", "Student"),
            Persona("Linus Torvalds", "linus.torvalds@cs.dev", "Tutor", "BetaTester"),
            Persona("Edsger Dijkstra", "edsger.dijkstra@cs.dev", "HeadTutor"),
        ],
    };

    private static DevAccountDefinition LanguagesAccount() => new()
    {
        DeveloperUsername = "Languages Developer",
        DeveloperEmail = "languages.developer@dev.local",
        TenantSlug = "languages",
        Personas =
        [
            Persona("William Shakespeare", "william.shakespeare@languages.dev", "VerifiedEducator"),
            Persona("Jane Austen", "jane.austen@languages.dev", "Tutor"),
            Persona("Mark Twain", "mark.twain@languages.dev", "Student"),
            Persona("Homer", "homer@languages.dev", "SeniorTutor"),
            Persona("Miguel de Cervantes", "miguel.cervantes@languages.dev", "Tutor", "SeminarHost"),
            Persona("Virginia Woolf", "virginia.woolf@languages.dev", "Moderator"),
        ],
    };

    private static DevAccountDefinition HistoryAccount() => new()
    {
        DeveloperUsername = "History Developer",
        DeveloperEmail = "history.developer@dev.local",
        TenantSlug = "history",
        Personas =
        [
            Persona("Cleopatra", "cleopatra@history.dev", "Student"),
            Persona("Leonardo da Vinci", "leonardo.davinci@history.dev", "Tutor"),
            Persona("Winston Churchill", "winston.churchill@history.dev", "Administrator"),
            Persona("Joan of Arc", "joan.ofarc@history.dev", "Moderator"),
            Persona("Genghis Khan", "genghis.khan@history.dev", "SeniorModerator"),
            Persona("Harriet Tubman", "harriet.tubman@history.dev", "EventOrganizer", "VerifiedEducator"),
        ],
    };

    private static DevAccountDefinition BusinessAccount() => new()
    {
        DeveloperUsername = "Business Developer",
        DeveloperEmail = "business.developer@dev.local",
        TenantSlug = "business",
        Personas =
        [
            Persona("Warren Buffett", "warren.buffett@business.dev", "Tutor"),
            Persona("Steve Jobs", "steve.jobs@business.dev", "EventOrganizer"),
            Persona("Oprah Winfrey", "oprah.winfrey@business.dev", "CommunityManager"),
            Persona("Indra Nooyi", "indra.nooyi@business.dev", "Administrator"),
            Persona("Elon Musk", "elon.musk@business.dev", "Student", "BetaTester"),
        ],
    };

    private static DevAccountDefinition ArtAccount() => new()
    {
        DeveloperUsername = "Art Developer",
        DeveloperEmail = "art.developer@dev.local",
        TenantSlug = "art",
        Personas =
        [
            Persona("Vincent van Gogh", "vincent.vangogh@art.dev", "Student"),
            Persona("Frida Kahlo", "frida.kahlo@art.dev", "Tutor"),
            Persona("Pablo Picasso", "pablo.picasso@art.dev", "SeniorTutor"),
            Persona("Georgia O'Keeffe", "georgia.okeeffe@art.dev", "VerifiedEducator"),
            Persona("Banksy", "banksy@art.dev", "Moderator"),
        ],
    };

    private static DevAccountDefinition MusicAccount() => new()
    {
        DeveloperUsername = "Music Developer",
        DeveloperEmail = "music.developer@dev.local",
        TenantSlug = "music",
        Personas =
        [
            Persona("Ludwig van Beethoven", "ludwig.beethoven@music.dev", "Tutor"),
            Persona("Wolfgang Amadeus Mozart", "wolfgang.mozart@music.dev", "Student"),
            Persona("Ella Fitzgerald", "ella.fitzgerald@music.dev", "SeminarHost"),
            Persona("Prince", "prince@music.dev", "EventOrganizer"),
            Persona("Johann Sebastian Bach", "johann.bach@music.dev", "SeniorTutor"),
        ],
    };

    private static DevAccountDefinition EngineeringAccount() => new()
    {
        DeveloperUsername = "Engineering Developer",
        DeveloperEmail = "engineering.developer@dev.local",
        TenantSlug = "engineering",
        Personas =
        [
            Persona("Tony Stark", "tony.stark@engineering.dev", "Tutor", "Developer"),
            Persona("Thomas Edison", "thomas.edison@engineering.dev", "SeniorTutor"),
            Persona("Isambard Kingdom Brunel", "isambard.brunel@engineering.dev", "Student"),
            Persona("Rosalind Franklin", "rosalind.franklin@engineering.dev", "HeadTutor"),
            Persona("James Watt", "james.watt@engineering.dev", "Tutor", "SeminarHost"),
        ],
    };

    private static DevAccountDefinition MedicineAccount() => new()
    {
        DeveloperUsername = "Medicine Developer",
        DeveloperEmail = "medicine.developer@dev.local",
        TenantSlug = "medicine",
        Personas =
        [
            Persona("Dr. Gregory House", "gregory.house@medicine.dev", "Tutor"),
            Persona("Florence Nightingale", "florence.nightingale@medicine.dev", "SeniorTutor"),
            Persona("Hippocrates", "hippocrates@medicine.dev", "VerifiedEducator"),
            Persona("Dr. Meredith Grey", "meredith.grey@medicine.dev", "Student"),
            Persona("Jonas Salk", "jonas.salk@medicine.dev", "Administrator"),
        ],
    };

    private static DevAccountDefinition FinanceAccount() => new()
    {
        DeveloperUsername = "Finance Developer",
        DeveloperEmail = "finance.developer@dev.local",
        TenantSlug = "finance",
        Personas =
        [
            Persona("Janet Yellen", "janet.yellen@finance.dev", "Administrator"),
            Persona("Charlie Munger", "charlie.munger@finance.dev", "Tutor"),
            Persona("Jordan Belfort", "jordan.belfort@finance.dev", "Student"),
            Persona("Alexander Hamilton", "alexander.hamilton@finance.dev", "BoardMember"),
            Persona("Molly Bloom", "molly.bloom@finance.dev", "Moderator"),
        ],
    };

    private static DevAccountDefinition EconomicsAccount() => new()
    {
        DeveloperUsername = "Economics Developer",
        DeveloperEmail = "economics.developer@dev.local",
        TenantSlug = "economics",
        Personas =
        [
            Persona("Adam Smith", "adam.smith@economics.dev", "Tutor"),
            Persona("John Maynard Keynes", "john.keynes@economics.dev", "SeniorTutor"),
            Persona("Milton Friedman", "milton.friedman@economics.dev", "Student"),
            Persona("Thomas Piketty", "thomas.piketty@economics.dev", "VerifiedEducator"),
            Persona("Dambisa Moyo", "dambisa.moyo@economics.dev", "EventOrganizer"),
        ],
    };

    private static DevAccountDefinition EducationAccount() => new()
    {
        DeveloperUsername = "Education Developer",
        DeveloperEmail = "education.developer@dev.local",
        TenantSlug = "education",
        Personas =
        [
            Persona("Ms. Frizzle", "ms.frizzle@education.dev", "Tutor", "SeminarHost"),
            Persona("John Dewey", "john.dewey@education.dev", "SeniorTutor"),
            Persona("Maria Montessori", "maria.montessori@education.dev", "VerifiedEducator"),
            Persona("Jaime Escalante", "jaime.escalante@education.dev", "HeadTutor"),
            Persona("Anne Sullivan", "anne.sullivan@education.dev", "Student"),
        ],
    };

    private static DevPersonaDefinition Persona(string username, string email, params string[] roles) => new()
    {
        Username = username,
        Email = email,
        Roles = roles,
    };
}
