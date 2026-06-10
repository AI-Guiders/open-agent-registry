namespace OpenAgentRegistry;

public sealed class RegistryApiException(int statusCode, string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
}
