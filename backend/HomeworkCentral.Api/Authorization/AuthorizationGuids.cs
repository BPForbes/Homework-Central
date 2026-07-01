using System.Security.Cryptography;
using System.Text;

namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Deterministic GUIDs for seeded authorization entities so every tenant database
/// shares the same role and subject identifiers.
/// </summary>
public static class AuthorizationGuids
{
    // Homework Central namespace: 6ba7b811-9dad-11d1-80b4-00c04fd430c8
    private static readonly Guid Namespace = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    public static Guid Role(string roleName) =>
        Create("role", roleName);

    public static Guid Subject(string subjectMask, short bitIndex) =>
        Create("subject", $"{subjectMask}:{bitIndex}");

    public static Guid DevUser(string email) =>
        Create("dev-user", email.ToLowerInvariant());

    private static Guid Create(string kind, string name) =>
        CreateVersion5(Namespace, $"{kind}:{name}");

    private static Guid CreateVersion5(Guid namespaceId, string name)
    {
        byte[] namespaceBytes = namespaceId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);

        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] hash = SHA1.HashData(namespaceBytes.Concat(nameBytes).ToArray());

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        byte[] guidBytes = hash.Take(16).ToArray();
        SwapGuidByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    private static void SwapGuidByteOrder(byte[] guidBytes)
    {
        Array.Reverse(guidBytes, 0, 4);
        Array.Reverse(guidBytes, 4, 2);
        Array.Reverse(guidBytes, 6, 2);
    }
}
