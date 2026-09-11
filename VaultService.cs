using System;
using System.Security.Cryptography;
using System.Text;

namespace StickyNotes__
{
    public static class VaultService
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int Iterations = 300_000;

        private static byte[]? _sessionKey;

        public static bool IsConfigured =>
            !string.IsNullOrEmpty(SettingsService.Current.VaultSalt) && !string.IsNullOrEmpty(SettingsService.Current.VaultVerifier);

        public static bool IsUnlocked => _sessionKey != null;

        public static void SetupVault(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = DeriveKey(password, salt);
            byte[] verifier = SHA256.HashData(key);

            var config = SettingsService.Current;
            config.VaultSalt = Convert.ToBase64String(salt);
            config.VaultVerifier = Convert.ToBase64String(verifier);
            SettingsService.Save(config);

            _sessionKey = key;
        }

        public static bool TryUnlock(string password)
        {
            var config = SettingsService.Current;
            if (string.IsNullOrEmpty(config.VaultSalt) || string.IsNullOrEmpty(config.VaultVerifier))
                return false;

            byte[] salt = Convert.FromBase64String(config.VaultSalt);
            byte[] key = DeriveKey(password, salt);
            byte[] verifier = SHA256.HashData(key);
            byte[] expected = Convert.FromBase64String(config.VaultVerifier);

            if (!CryptographicOperations.FixedTimeEquals(verifier, expected))
                return false;

            _sessionKey = key;
            return true;
        }

        public static void Lock() => _sessionKey = null;

        public static byte[] ChangePassword(string newPassword)
        {
            if (_sessionKey == null) throw new InvalidOperationException("Vault is locked.");
            byte[] oldKey = _sessionKey;

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] newKey = DeriveKey(newPassword, salt);
            byte[] verifier = SHA256.HashData(newKey);

            var config = SettingsService.Current;
            config.VaultSalt = Convert.ToBase64String(salt);
            config.VaultVerifier = Convert.ToBase64String(verifier);
            SettingsService.Save(config);

            _sessionKey = newKey;
            return oldKey;
        }

        private static string DecryptWithKey(string ciphertextBase64, byte[] key)
        {
            if (string.IsNullOrEmpty(ciphertextBase64)) return string.Empty;

            byte[] combined = Convert.FromBase64String(ciphertextBase64);
            if (combined.Length < NonceSize + TagSize)
                throw new CryptographicException("Ciphertext payload is invalid or truncated.");

            byte[] nonce = new byte[NonceSize];
            byte[] tag = new byte[TagSize];
            byte[] cipherBytes = new byte[combined.Length - NonceSize - TagSize];

            Buffer.BlockCopy(combined, 0, nonce, 0, nonce.Length);
            Buffer.BlockCopy(combined, nonce.Length, tag, 0, tag.Length);
            Buffer.BlockCopy(combined, nonce.Length + tag.Length, cipherBytes, 0, cipherBytes.Length);

            byte[] plainBytes = new byte[cipherBytes.Length];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }

        public static string ReEncrypt(string ciphertextBase64, byte[] oldKey)
        {
            string plaintext = DecryptWithKey(ciphertextBase64, oldKey);
            return Encrypt(plaintext);
        }

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        }

        public static string Encrypt(string plaintext)
        {
            if (_sessionKey == null) throw new InvalidOperationException("Vault is locked.");

            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] cipherBytes = new byte[plainBytes.Length];
            byte[] tag = new byte[TagSize];

            using (var aes = new AesGcm(_sessionKey, TagSize))
            {
                aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
            }

            byte[] combined = new byte[nonce.Length + tag.Length + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipherBytes, 0, combined, nonce.Length + tag.Length, cipherBytes.Length);

            return Convert.ToBase64String(combined);
        }

        public static string Decrypt(string ciphertextBase64)
        {
            if (_sessionKey == null) throw new InvalidOperationException("Vault is locked.");
            return DecryptWithKey(ciphertextBase64, _sessionKey);
        }
    }
}
