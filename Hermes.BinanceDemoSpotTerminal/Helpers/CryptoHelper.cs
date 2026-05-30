using System;
using System.Security.Cryptography;
using System.Text;

namespace Hermes.BinanceDemoSpotTerminal.Helpers
{
    public static class CryptoHelper
    {
        public static string GenerateSignature(string totalParams, string secretKey)
        {
            if (string.IsNullOrEmpty(secretKey))
                return string.Empty;

            byte[] keyBytes = Encoding.UTF8.GetBytes(secretKey);
            byte[] messageBytes = Encoding.UTF8.GetBytes(totalParams);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                byte[] hashBytes = hmac.ComputeHash(messageBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }
    }
}
