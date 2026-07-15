namespace HomeworkCentral.Api.Tests.Chat;

[CollectionDefinition(nameof(ChatPostgresTestCollection))]
public sealed class ChatPostgresTestCollection : ICollectionFixture<ChatPostgresTestFixture>;

/// <summary>
/// Serializes real-Postgres chat tests that call <c>EnsureDeletedAsync</c> on the shared
/// <c>homework_central_test_chat</c> database so parallel test classes cannot drop it mid-run.
/// </summary>
public sealed class ChatPostgresTestFixture;
