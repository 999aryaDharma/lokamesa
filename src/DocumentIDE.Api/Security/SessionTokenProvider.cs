using System;
using System.Security.Cryptography;
using System.Text;

namespace DocumentIDE.Api.Security;

public class SessionTokenProvider : ISessionTokenProvider
{
    public string SessionToken { get; }
    public string ControlToken { get; }

    public SessionTokenProvider()
    {
        SessionToken = GenerateToken();
        ControlToken = GenerateToken();
    }

    public bool ValidateToken(string? token) => ConstantTimeEquals(token, SessionToken);

    public bool ValidateControlToken(string? token) => ConstantTimeEquals(token, ControlToken);

    private static string GenerateToken()
    {
        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static bool ConstantTimeEquals(string? candidate, string expected)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        byte[] candidateBytes = Encoding.UTF8.GetBytes(candidate);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);

        if (candidateBytes.Length != expectedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes);
    }
}
