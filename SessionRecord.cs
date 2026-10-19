using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ContinueGame
{
    [DataContract]
    public sealed class SessionRecord
    {
        [DataMember] public int Version = 1;
        [DataMember] public string ServerKind;
        [DataMember] public string Address;
        [DataMember] public int Port;
        [DataMember] public string ServerOwner;
        [DataMember(EmitDefaultValue = false)] public string ServerName;
        [DataMember] public string CharacterFilename;
        [DataMember(EmitDefaultValue = false)] public string CharacterName;
        [DataMember] public long CharacterId;
        [DataMember] public string ProtectedPassword;
        [DataMember(EmitDefaultValue = false)] public string WorldName;
        [DataMember(EmitDefaultValue = false)] public long WorldId;
        [DataMember(EmitDefaultValue = false)] public bool StartServer;
        [DataMember(EmitDefaultValue = false)] public bool CommunityServer;
        [DataMember(EmitDefaultValue = false)] public bool Crossplay;

        public bool IsLocalWorld => ServerKind == "LocalWorld";
        public bool IsHostedWorld => ServerKind == "HostedWorld";
        public bool IsWorld => IsLocalWorld || IsHostedWorld;

        public bool MatchesServer(SessionRecord other)
        {
            return other != null && !IsWorld && !other.IsWorld &&
                ServerKind == other.ServerKind && Port == other.Port &&
                string.Equals(Address, other.Address, StringComparison.Ordinal) &&
                string.Equals(ServerOwner, other.ServerOwner, StringComparison.Ordinal);
        }

        public bool MatchesWorld(string name, long id)
        {
            return IsWorld && WorldId != 0 && WorldId == id &&
                string.Equals(WorldName, name, StringComparison.Ordinal);
        }

        public bool IsValid()
        {
            if (Version != 1 || string.IsNullOrWhiteSpace(CharacterFilename) || ProtectedPassword == null)
                return false;
            if (IsWorld)
            {
                if (string.IsNullOrWhiteSpace(WorldName) || WorldId == 0) return false;
                if (IsHostedWorld) return StartServer;
                return !StartServer && !CommunityServer && !Crossplay && ProtectedPassword.Length == 0;
            }
            if (StartServer || CommunityServer || Crossplay) return false;
            if (string.IsNullOrWhiteSpace(Address)) return false;
            if (ServerKind == "Dedicated") return Port > 0 && Port <= ushort.MaxValue;
            if (ServerKind == "PlayFab") return true;
            ulong id;
            return ServerKind == "Steam" && ulong.TryParse(Address, out id) && id != 0;
        }
    }

    public sealed class SessionStore
    {
        private readonly string _path;
        public SessionStore(string path) { _path = path; }

        public SessionRecord Load()
        {
            if (!File.Exists(_path)) return null;
            if (new FileInfo(_path).Length > 65536) throw new InvalidDataException("Saved session is too large.");
            using (var stream = File.OpenRead(_path))
            {
                var record = (SessionRecord)new DataContractJsonSerializer(typeof(SessionRecord)).ReadObject(stream);
                if (record == null || !record.IsValid()) throw new InvalidDataException("Invalid saved session.");
                return record;
            }
        }

        public void Save(SessionRecord record)
        {
            if (record == null || !record.IsValid()) throw new InvalidDataException("Invalid session.");
            string directory = Path.GetDirectoryName(Path.GetFullPath(_path));
            Directory.CreateDirectory(directory);
            string temporary = _path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(SessionRecord)).WriteObject(stream, record);
                    stream.Flush(true);
                }
                if (File.Exists(_path)) File.Replace(temporary, _path, null);
                else File.Move(temporary, _path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
