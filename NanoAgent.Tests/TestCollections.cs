namespace NanoAgent.Tests;

[CollectionDefinition(TestCollections.SecretRedactorState, DisableParallelization = true)]
public sealed class SecretRedactorStateCollectionDefinition;

public static class TestCollections
{
    public const string SecretRedactorState = "SecretRedactor state";
}
