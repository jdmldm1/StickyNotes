using System;
using System.Security.Cryptography;
using System.Text;

namespace StickyNotes__
{
    // Handles the master "vault" password used to encrypt/decrypt Secure Notes.
    // The password itself is never stored on disk - only a random salt and a verifier
    // hash derived from it, so a stolen settings file or database backup can't be used
    // to recover it or the notes it protects. Forgetting the password means the notes
    // it protects cannot be recovered; there is deliberately no backdoor.
    public static class VaultService
    {
        private const int SaltSize = 16;
        private const int KeySize = 32; // AES-256
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int Iterations = 300_000;

        // Derived key is cached in memory only after a successful unlock this session,
        // and cleared on explicit lock or app exit - never written to disk.
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

        // Re-derives the key from the new password and returns the old key so the
        // caller can re-encrypt existing secure notes before the old key is gone.
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
            byte[] combined = Convert.FromBase64String(ciphertextBase64);
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

            byte[] combined = Convert.FromBase64String(ciphertextBase64);
            byte[] nonce = new byte[NonceSize];
            byte[] tag = new byte[TagSize];
            byte[] cipherBytes = new byte[combined.Length - NonceSize - TagSize];

            Buffer.BlockCopy(combined, 0, nonce, 0, nonce.Length);
            Buffer.BlockCopy(combined, nonce.Length, tag, 0, tag.Length);
            Buffer.BlockCopy(combined, nonce.Length + tag.Length, cipherBytes, 0, cipherBytes.Length);

            byte[] plainBytes = new byte[cipherBytes.Length];
            using (var aes = new AesGcm(_sessionKey, TagSize))
            {
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
