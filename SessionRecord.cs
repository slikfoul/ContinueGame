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
        [DataMember] public string CharacterFilename;
        [DataMember] public long CharacterId;
        [DataMember] public string ProtectedPassword;

        public bool IsValid()
        {
            if (Version != 1 || string.IsNullOrWhiteSpace(CharacterFilename) ||
                string.IsNullOrWhiteSpace(Address) || ProtectedPassword == null)
                return false;
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
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
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
