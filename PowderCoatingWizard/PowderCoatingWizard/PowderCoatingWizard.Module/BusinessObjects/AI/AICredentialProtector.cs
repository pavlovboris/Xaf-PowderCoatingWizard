using System.Security.Cryptography;
using System.Text;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// Protects AI credentials.
    /// On Windows uses DPAPI (per-user scope) — no key management needed.
    /// Falls back to reversible base64 on non-Windows (server Blazor) — replace with
    /// Azure Key Vault / ASP.NET Data Protection for production server deployments.
    /// </summary>
    public static class AICredentialProtector
    {
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            if (OperatingSystem.IsWindows())
            {
                byte[] data = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }

            // Non-Windows fallback (Blazor server on Linux) — base64 only.
            // TODO: replace with IDataProtectionProvider for production server hosting.
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }

        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return string.Empty;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    byte[] encrypted = Convert.FromBase64String(encryptedText);
                    byte[] data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(data);
                }

                return Encoding.UTF8.GetString(Convert.FromBase64String(encryptedText));
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
