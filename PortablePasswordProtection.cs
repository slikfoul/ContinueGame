using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ContinueGame
{
    public sealed class CrossPlatformPasswordProtection
    {
        private readonly PortablePasswordProtection _portable;
        public CrossPlatformPasswordProtection(string keyDirectory)
        { _portable = new PortablePasswordProtection(keyDirectory); }

        public string Protect(string password)
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT
                ? PasswordProtection.Protect(password) : _portable.Protect(password);
        }

        public string Unprotect(string password)
        {
            return password != null && password.StartsWith(PortablePasswordProtection.Prefix, StringComparison.Ordinal)
                ? _portable.Unprotect(password) : PasswordProtection.Unprotect(password);
        }
    }

    // The local random key is protected by Unix account permissions, not by a desktop keyring.
    // AES-CBC uses a fresh IV; a separate key authenticates the entire envelope before decryption.
    public sealed class PortablePasswordProtection
    {
        public const string Prefix = "portable-v1:";
        private readonly string _directory;
        private readonly string _keyPath;

        [DllImport("ContinueGame.Posix", EntryPoint = "chmod", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int Chmod(string path, uint mode);

        public PortablePasswordProtection(string directory)
        {
            _directory = Path.GetFullPath(directory);
            _keyPath = Path.Combine(_directory, "password.key");
        }

        private static void Restrict(string path, uint mode)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT && Chmod(path, mode) != 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private byte[] ReadKey(bool create)
        {
            if (!Directory.Exists(_directory))
            {
                if (!create) throw new FileNotFoundException("Password key is missing.");
                Directory.CreateDirectory(_directory);
            }
            Restrict(_directory, 448); // 0700, owner only
            if (!File.Exists(_keyPath) && create)
            {
                byte[] key = new byte[64];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(key);
                bool created = false;
                try
                {
                    using (var stream = new FileStream(_keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        created = true;
                        // Restrict the empty file before writing any secret bytes.
                        Restrict(_keyPath, 384); // 0600, owner read/write
                        stream.Write(key, 0, key.Length);
                        stream.Flush(true);
                    }
                }
                catch
                {
                    // Do not leave a broken key behind after a failed first save.
                    // Never delete a pre-existing key if CreateNew itself failed.
                    if (created) { try { File.Delete(_keyPath); } catch (IOException) { } }
                    throw;
                }
                finally { Array.Clear(key, 0, key.Length); }
            }
            if (!File.Exists(_keyPath)) throw new FileNotFoundException("Password key is missing.");
            Restrict(_keyPath, 384);
            if (new FileInfo(_keyPath).Length != 64) throw new CryptographicException("Invalid password key.");
            return File.ReadAllBytes(_keyPath);
        }

        public string Protect(string password)
        {
            if (string.IsNullOrEmpty(password)) return "";
            byte[] key = ReadKey(true);
            byte[] plaintext = Encoding.UTF8.GetBytes(password);
            byte[] encryptionKey = new byte[32];
            byte[] authenticationKey = new byte[32];
            Buffer.BlockCopy(key, 0, encryptionKey, 0, 32);
            Buffer.BlockCopy(key, 32, authenticationKey, 0, 32);
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = encryptionKey;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.GenerateIV();
                    byte[] encrypted;
                    using (var encryptor = aes.CreateEncryptor())
                        encrypted = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
                    // Version byte, 16-byte IV, ciphertext, 32-byte HMAC.
                    byte[] envelope = new byte[1 + 16 + encrypted.Length + 32];
                    envelope[0] = 1;
                    Buffer.BlockCopy(aes.IV, 0, envelope, 1, 16);
                    Buffer.BlockCopy(encrypted, 0, envelope, 17, encrypted.Length);
                    using (var mac = new HMACSHA256(authenticationKey))
                    {
                        byte[] tag = mac.ComputeHash(envelope, 0, envelope.Length - 32);
                        Buffer.BlockCopy(tag, 0, envelope, envelope.Length - 32, 32);
                    }
                    return Prefix + Convert.ToBase64String(envelope);
                }
            }
            finally
            {
                Array.Clear(plaintext, 0, plaintext.Length);
                Array.Clear(key, 0, key.Length);
                Array.Clear(encryptionKey, 0, encryptionKey.Length);
                Array.Clear(authenticationKey, 0, authenticationKey.Length);
            }
        }

        public string Unprotect(string password)
        {
            if (string.IsNullOrEmpty(password)) return "";
            if (!password.StartsWith(Prefix, StringComparison.Ordinal)) throw new CryptographicException("Unknown password format.");
            byte[] envelope = Convert.FromBase64String(password.Substring(Prefix.Length));
            if (envelope.Length < 65 || envelope[0] != 1 || (envelope.Length - 49) % 16 != 0)
                throw new CryptographicException("Invalid encrypted password.");
            byte[] key = ReadKey(false);
            byte[] encryptionKey = new byte[32];
            byte[] authenticationKey = new byte[32];
            Buffer.BlockCopy(key, 0, encryptionKey, 0, 32);
            Buffer.BlockCopy(key, 32, authenticationKey, 0, 32);
            try
            {
                using (var mac = new HMACSHA256(authenticationKey))
                {
                    byte[] expected = mac.ComputeHash(envelope, 0, envelope.Length - 32);
                    int difference = 0;
                    for (int i = 0; i < 32; i++) difference |= expected[i] ^ envelope[envelope.Length - 32 + i];
                    if (difference != 0) throw new CryptographicException("Password authentication failed.");
                }
                using (var aes = Aes.Create())
                {
                    aes.Key = encryptionKey;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    byte[] iv = new byte[16];
                    Buffer.BlockCopy(envelope, 1, iv, 0, 16);
                    aes.IV = iv;
                    byte[] plaintext;
                    using (var decryptor = aes.CreateDecryptor())
                        plaintext = decryptor.TransformFinalBlock(envelope, 17, envelope.Length - 49);
                    try { return Encoding.UTF8.GetString(plaintext); }
                    finally { Array.Clear(plaintext, 0, plaintext.Length); }
                }
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                Array.Clear(encryptionKey, 0, encryptionKey.Length);
                Array.Clear(authenticationKey, 0, authenticationKey.Length);
            }
        }
    }
}
