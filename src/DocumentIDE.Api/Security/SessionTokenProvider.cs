using System;
using System.Security.Cryptography;
using System.Text;

namespace DocumentIDE.Api.Security;

public class SessionTokenProvider : ISessionTokenProvider
{
    public string SessionToken { get; }

    public SessionTokenProvider()
    {
        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        SessionToken = Convert.ToBase64String(bytes);
    }

    public bool ValidateToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        byte[] tokenBytes = Encoding.UTF8.GetBytes(token);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(SessionToken);

        if (tokenBytes.Length != expectedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(tokenBytes, expectedBytes);
    }
}
