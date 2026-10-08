using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ContinueGame
{
    public sealed class PrivateSessionStorage
    {
        private readonly SessionStore _store;
        public readonly CrossPlatformPasswordProtection PasswordProtection;
        public readonly string DirectoryPath;

        public static string ResolveDirectory(string configDirectory, string dataRoot)
        {
            if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathRooted(dataRoot))
                throw new IOException("The local Valheim save directory is unavailable.");
            string configPath = Path.GetFullPath(configDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string identity = Environment.OSVersion.Platform == PlatformID.Win32NT ? configPath.ToUpperInvariant() : configPath;
            string profileId;
            using (var sha = SHA256.Create())
                profileId = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").ToLowerInvariant();
            string destination = Path.GetFullPath(Path.Combine(dataRoot, "ContinueGame", profileId));
            // Reject a custom data root that would put secrets back inside the profile.
            string profileRoot = Path.GetDirectoryName(Path.GetDirectoryName(configPath));
            string prefix = profileRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = Environment.OSVersion.Platform == PlatformID.Win32NT ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (destination.StartsWith(prefix, comparison)) throw new IOException("Private data must be outside the mod profile.");
            return destination;
        }

        public PrivateSessionStorage(string configDirectory, string dataRoot)
        {
            DirectoryPath = ResolveDirectory(configDirectory, dataRoot);
            _store = new SessionStore(Path.Combine(DirectoryPath, "last-session.json"));
            PasswordProtection = new CrossPlatformPasswordProtection(Path.Combine(DirectoryPath, "keys"));
        }

        public SessionRecord Load()
        {
            var existing = _store.Load();
            if (existing != null)
            {
                PasswordProtection.Unprotect(existing.ProtectedPassword);
            }
            return existing;
        }

        public void Save(SessionRecord record, string password)
        {
            if (record == null) throw new InvalidDataException("Invalid session.");
            password = password ?? "";
            Directory.CreateDirectory(DirectoryPath);
            record.ProtectedPassword = PasswordProtection.Protect(password);
            _store.Save(record);
            var verified = _store.Load();
            if (verified == null || verified.ProtectedPassword != record.ProtectedPassword ||
                PasswordProtection.Unprotect(verified.ProtectedPassword) != password)
                throw new InvalidDataException("Private session verification failed.");
        }
    }
}
