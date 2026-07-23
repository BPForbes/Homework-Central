using System.Reflection;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;

namespace HomeworkCentral.Api.Tickets.Preface;

/// <summary>
/// Tutor-application preface check: maps free-text subjects (biology → Science, rust → ComputerScience)
/// with strict verify-or-reenter. Inherits the shared vocabulary engine.
/// </summary>
public sealed class TutorSubjectPrefaceCheck : VocabularyTicketPrefaceCheck
{
    public const string QuestionIdValue = "tutor-subjects";
    public const string CheckIdValue = "tutor-subjects";

    public static TutorSubjectPrefaceCheck Instance { get; } = new();

    public override string CheckId => CheckIdValue;
    public override string QuestionId => QuestionIdValue;
    public override string? FilterName => DefaultTicketPortalPresets.TutorFilterName;
    public override TicketPrefaceMode Mode => TicketPrefaceMode.Strict;
    public override bool RewriteAnswerOnSuccess => true;

    protected override string CategoryNoun => "subject";
    protected override string ReenterExamples => "Biology, Rust, Mathematics";

    protected override string DisplayNameForCategory(string category) => category switch
    {
        SubjectMaskNames.ComputerScience => "Computer Science",
        SubjectMaskNames.Mathematics => "Mathematics",
        SubjectMaskNames.Science => "Science",
        SubjectMaskNames.Languages => "Languages",
        SubjectMaskNames.History => "History",
        SubjectMaskNames.Business => "Business",
        SubjectMaskNames.Art => "Art",
        SubjectMaskNames.Music => "Music",
        SubjectMaskNames.Engineering => "Engineering",
        SubjectMaskNames.Medicine => "Medicine",
        SubjectMaskNames.Finance => "Finance",
        SubjectMaskNames.Economics => "Economics",
        SubjectMaskNames.Education => "Education",
        _ => category,
    };

    protected override void RegisterVocabulary(VocabularyBuilder builder)
    {
        builder.Add("mathematics", SubjectMaskNames.Mathematics, "Mathematics");
        builder.Add("math", SubjectMaskNames.Mathematics, "Mathematics");
        builder.Add("maths", SubjectMaskNames.Mathematics, "Mathematics");

        builder.Add("science", SubjectMaskNames.Science, "Science");

        builder.Add("computer science", SubjectMaskNames.ComputerScience, "Computer Science");
        builder.Add("computerscience", SubjectMaskNames.ComputerScience, "Computer Science");
        builder.Add("comp sci", SubjectMaskNames.ComputerScience, "Computer Science");
        builder.Add("compsci", SubjectMaskNames.ComputerScience, "Computer Science");
        builder.Add("cs", SubjectMaskNames.ComputerScience, "Computer Science");
        builder.Add("coding", SubjectMaskNames.ComputerScience, "Computer Science");
        builder.Add("programming", SubjectMaskNames.ComputerScience, "Computer Science");

        builder.Add("languages", SubjectMaskNames.Languages, "Languages");
        builder.Add("language", SubjectMaskNames.Languages, "Languages");
        builder.Add("foreign language", SubjectMaskNames.Languages, "Languages");

        builder.Add("history", SubjectMaskNames.History, "History");
        builder.Add("business", SubjectMaskNames.Business, "Business");
        builder.Add("art", SubjectMaskNames.Art, "Art");
        builder.Add("music", SubjectMaskNames.Music, "Music");
        builder.Add("engineering", SubjectMaskNames.Engineering, "Engineering");
        builder.Add("medicine", SubjectMaskNames.Medicine, "Medicine");
        builder.Add("medical", SubjectMaskNames.Medicine, "Medicine");
        builder.Add("finance", SubjectMaskNames.Finance, "Finance");
        builder.Add("economics", SubjectMaskNames.Economics, "Economics");
        builder.Add("education", SubjectMaskNames.Education, "Education");
        builder.Add("teaching", SubjectMaskNames.Education, "Education");

        foreach ((string generalMask, Type expertiseType) in ExpertiseTypes())
        {
            foreach (FieldInfo field in expertiseType
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(short)))
            {
                string display = ExpertiseDisplayName(field.Name);
                builder.Add(field.Name, generalMask, display, isSpecific: true);
                builder.Add(display, generalMask, display, isSpecific: true);
                builder.Add(ChatRoomCatalog.ToDisplayName(field.Name), generalMask, display, isSpecific: true);
            }
        }

        builder.Add("js", SubjectMaskNames.ComputerScience, "JavaScript", isSpecific: true);
        builder.Add("ts", SubjectMaskNames.ComputerScience, "TypeScript", isSpecific: true);
        builder.Add("cpp", SubjectMaskNames.ComputerScience, "C++", isSpecific: true);
        builder.Add("c plus plus", SubjectMaskNames.ComputerScience, "C++", isSpecific: true);
        builder.Add("golang", SubjectMaskNames.ComputerScience, "Go", isSpecific: true);
        builder.Add("postgres", SubjectMaskNames.ComputerScience, "PostgreSQL", isSpecific: true);
        builder.Add("k8s", SubjectMaskNames.ComputerScience, "Kubernetes", isSpecific: true);
        builder.Add("cyber security", SubjectMaskNames.ComputerScience, "Cyber Security", isSpecific: true);
        builder.Add("cybersecurity", SubjectMaskNames.ComputerScience, "Cyber Security", isSpecific: true);
        builder.Add("opsys", SubjectMaskNames.ComputerScience, "Operating Systems", isSpecific: true);
        builder.Add("os", SubjectMaskNames.ComputerScience, "Operating Systems", isSpecific: true);
        builder.Add("frontend", SubjectMaskNames.ComputerScience, "Frontend", isSpecific: true);
        builder.Add("front end", SubjectMaskNames.ComputerScience, "Frontend", isSpecific: true);
        builder.Add("back end", SubjectMaskNames.ComputerScience, "Backend", isSpecific: true);
        builder.Add("apis", SubjectMaskNames.ComputerScience, "APIs", isSpecific: true);
        builder.Add("rest", SubjectMaskNames.ComputerScience, "APIs", isSpecific: true);
        builder.Add("sql", SubjectMaskNames.ComputerScience, "PostgreSQL", isSpecific: true);
        builder.Add("ml", SubjectMaskNames.ComputerScience, "Computer Science");
        builder.Add("ai", SubjectMaskNames.ComputerScience, "Computer Science");
        builder.Add("machine learning", SubjectMaskNames.ComputerScience, "Computer Science");
        builder.Add("data structures", SubjectMaskNames.ComputerScience, "Computer Science");
        builder.Add("algorithms", SubjectMaskNames.ComputerScience, "Computer Science");

        builder.Add("bio", SubjectMaskNames.Science, "Biology", isSpecific: true);
        builder.Add("chem", SubjectMaskNames.Science, "Chemistry", isSpecific: true);
        builder.Add("phys", SubjectMaskNames.Science, "Physics", isSpecific: true);
        builder.Add("psych", SubjectMaskNames.Science, "Psychology", isSpecific: true);

        builder.Add("calc", SubjectMaskNames.Mathematics, "Calculus", isSpecific: true);
        builder.Add("trig", SubjectMaskNames.Mathematics, "Trigonometry", isSpecific: true);
        builder.Add("stats", SubjectMaskNames.Mathematics, "Statistics", isSpecific: true);
        builder.Add("lin alg", SubjectMaskNames.Mathematics, "Linear Algebra", isSpecific: true);
        builder.Add("linear alg", SubjectMaskNames.Mathematics, "Linear Algebra", isSpecific: true);
        builder.Add("diff eq", SubjectMaskNames.Mathematics, "Differential Equations", isSpecific: true);
        builder.Add("ode", SubjectMaskNames.Mathematics, "Differential Equations", isSpecific: true);

        builder.Add("asl", SubjectMaskNames.Languages, "American Sign Language", isSpecific: true);
        builder.Add("mandarin chinese", SubjectMaskNames.Languages, "Mandarin", isSpecific: true);
        builder.Add("chinese", SubjectMaskNames.Languages, "Mandarin", isSpecific: true);

        builder.Add("us history", SubjectMaskNames.History, "US History", isSpecific: true);
        builder.Add("u.s. history", SubjectMaskNames.History, "US History", isSpecific: true);
        builder.Add("american history", SubjectMaskNames.History, "US History", isSpecific: true);
        builder.Add("world hist", SubjectMaskNames.History, "World History", isSpecific: true);

        builder.Add("mech eng", SubjectMaskNames.Engineering, "Mechanical Engineering", isSpecific: true);
        builder.Add("electrical eng", SubjectMaskNames.Engineering, "Electrical Engineering", isSpecific: true);
        builder.Add("civil eng", SubjectMaskNames.Engineering, "Civil Engineering", isSpecific: true);
        builder.Add("chem eng", SubjectMaskNames.Engineering, "Chemical Engineering", isSpecific: true);
        builder.Add("aero", SubjectMaskNames.Engineering, "Aerospace Engineering", isSpecific: true);

        builder.Add("micro econ", SubjectMaskNames.Economics, "Microeconomics", isSpecific: true);
        builder.Add("macro econ", SubjectMaskNames.Economics, "Macroeconomics", isSpecific: true);
        builder.Add("micro", SubjectMaskNames.Economics, "Microeconomics", isSpecific: true);
        builder.Add("macro", SubjectMaskNames.Economics, "Macroeconomics", isSpecific: true);
    }

    private static IEnumerable<(string GeneralMask, Type ExpertiseType)> ExpertiseTypes()
    {
        yield return (SubjectMaskNames.Mathematics, typeof(MathematicsExpertise));
        yield return (SubjectMaskNames.Science, typeof(ScienceExpertise));
        yield return (SubjectMaskNames.ComputerScience, typeof(ComputerScienceExpertise));
        yield return (SubjectMaskNames.Languages, typeof(LanguageExpertise));
        yield return (SubjectMaskNames.History, typeof(HistoryExpertise));
        yield return (SubjectMaskNames.Business, typeof(BusinessExpertise));
        yield return (SubjectMaskNames.Art, typeof(ArtExpertise));
        yield return (SubjectMaskNames.Music, typeof(MusicExpertise));
        yield return (SubjectMaskNames.Engineering, typeof(EngineeringExpertise));
        yield return (SubjectMaskNames.Medicine, typeof(MedicineExpertise));
        yield return (SubjectMaskNames.Finance, typeof(FinanceExpertise));
        yield return (SubjectMaskNames.Economics, typeof(EconomicsExpertise));
        yield return (SubjectMaskNames.Education, typeof(EducationExpertise));
    }

    private static string ExpertiseDisplayName(string fieldName) => fieldName switch
    {
        "CPlusPlus" => "C++",
        "CSharp" => "C#",
        "Css" => "CSS",
        "Html" => "HTML",
        "RestApis" => "APIs",
        "PostgreSql" => "PostgreSQL",
        "MySql" => "MySQL",
        "MongoDb" => "MongoDB",
        "GraphQl" => "GraphQL",
        "Aws" => "AWS",
        "CyberSecurity" => "Cyber Security",
        "OperatingSystems" => "Operating Systems",
        "AmericanSignLanguage" => "American Sign Language",
        "UsHistory" => "US History",
        "DigitalArt" => "Digital Art",
        "ArtHistory" => "Art History",
        "MusicTheory" => "Music Theory",
        "MusicProduction" => "Music Production",
        "PublicHealth" => "Public Health",
        "PersonalFinance" => "Personal Finance",
        "CorporateFinance" => "Corporate Finance",
        "InternationalEconomics" => "International Economics",
        "CurriculumDesign" => "Curriculum Design",
        "SpecialEducation" => "Special Education",
        "EarlyChildhood" => "Early Childhood",
        "EducationalTechnology" => "Educational Technology",
        "LinearAlgebra" => "Linear Algebra",
        "DifferentialEquations" => "Differential Equations",
        "DiscreteMathematics" => "Discrete Mathematics",
        "NumericalMethods" => "Numerical Methods",
        "Mechanical" => "Mechanical Engineering",
        "Electrical" => "Electrical Engineering",
        "Civil" => "Civil Engineering",
        "Chemical" => "Chemical Engineering",
        "Aerospace" => "Aerospace Engineering",
        _ => ChatRoomCatalog.ToDisplayName(fieldName),
    };
}
