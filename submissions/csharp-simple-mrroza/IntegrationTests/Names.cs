namespace IntegrationTests;

public static class Names
{
    public static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}";
}
