using ContinueGame;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
bool Rejects(Action action)
{
    try { action(); return false; }
    catch { return true; }
}

string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
string folder = Path.Combine(project, "tests", "scratch-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    bool windows = OperatingSystem.IsWindows();
    var platformProtection = new CrossPlatformPasswordProtection(Path.Combine(folder, "platform-keys"));
    string testPassword = "Synthetic-check-" + Guid.NewGuid().ToString("N") + "-пароль";
    string encrypted = platformProtection.Protect(testPassword);
    Check(encrypted != testPassword && encrypted.Length > 0, "Password encrypted");
    Check(platformProtection.Unprotect(encrypted) == testPassword, windows ? "Windows DPAPI Unicode round trip" : "Linux platform AES/HMAC Unicode round trip");
    Check(platformProtection.Protect(testPassword) != encrypted, "Encryption uses fresh randomness");
    string envelopePrefix = windows ? "" : PortablePasswordProtection.Prefix;
    byte[] modified = Convert.FromBase64String(encrypted.Substring(envelopePrefix.Length));
    modified[modified.Length - 1] ^= 0x40;
    Check(Rejects(() => platformProtection.Unprotect(envelopePrefix + Convert.ToBase64String(modified))), "Tampered password rejected");
    Check(Rejects(() => platformProtection.Unprotect(envelopePrefix + "not-base64")), "Malformed ciphertext rejected");
    Check(platformProtection.Unprotect(platformProtection.Protect("")) == "", "Passwordless server supported");

    string keyDirectory = Path.Combine(folder, "keys");
    var portable = new PortablePasswordProtection(keyDirectory);
    Check(portable.Protect("") == "" && !Directory.Exists(keyDirectory), "Passwordless portable login creates no key");
    string portableEncrypted = portable.Protect(testPassword);
    Check(portableEncrypted.StartsWith(PortablePasswordProtection.Prefix) && !portableEncrypted.Contains(testPassword), "Portable password is versioned and encrypted");
    Check(portable.Unprotect(portableEncrypted) == testPassword, "Portable AES and HMAC Unicode round trip");
    Check(portable.Protect(testPassword) != portableEncrypted, "Portable encryption uses fresh IV");
    Check(new PortablePasswordProtection(keyDirectory).Unprotect(portableEncrypted) == testPassword, "Portable key survives provider restart");
    Check(new FileInfo(Path.Combine(keyDirectory, "password.key")).Length == 64, "Independent encryption and authentication keys persisted");
    foreach (int offset in new[] { 0, 1, 17, Convert.FromBase64String(portableEncrypted.Substring(PortablePasswordProtection.Prefix.Length)).Length - 1 })
    {
        byte[] tampered = Convert.FromBase64String(portableEncrypted.Substring(PortablePasswordProtection.Prefix.Length));
        tampered[offset] ^= 0x40;
        Check(Rejects(() => portable.Unprotect(PortablePasswordProtection.Prefix + Convert.ToBase64String(tampered))), "Portable envelope tamper rejected at offset " + offset);
    }
    Check(Rejects(() => portable.Unprotect(PortablePasswordProtection.Prefix + "invalid")), "Portable malformed ciphertext rejected");
    Check(Rejects(() => portable.Unprotect(PortablePasswordProtection.Prefix + Convert.ToBase64String(new byte[20]))), "Portable truncated envelope rejected");
    Check(Rejects(() => portable.Unprotect("unknown-format")), "Portable unknown format rejected");
    string missingDirectory = Path.Combine(folder, "missing-key");
    Check(Rejects(() => new PortablePasswordProtection(missingDirectory).Unprotect(portableEncrypted)) && !Directory.Exists(missingDirectory), "Missing portable key rejected without regeneration");
    var wrongKey = new PortablePasswordProtection(Path.Combine(folder, "wrong-key"));
    wrongKey.Protect("create an unrelated key");
    Check(Rejects(() => wrongKey.Unprotect(portableEncrypted)), "Unrelated key rejected by authentication");
    var compatibility = new CrossPlatformPasswordProtection(keyDirectory);
    Check((windows ? compatibility.Unprotect(encrypted) : platformProtection.Unprotect(encrypted)) == testPassword,
        windows ? "Legacy Windows passwords remain compatible" : "Linux platform password survives provider use");
    Check(compatibility.Unprotect(portableEncrypted) == testPassword, "Portable password format selected by prefix");
    Check(windows ? PasswordProtection.Unprotect(compatibility.Protect(testPassword)) == testPassword :
        compatibility.Protect(testPassword).StartsWith(PortablePasswordProtection.Prefix),
        windows ? "Windows still uses DPAPI by default" : "Linux uses portable encryption by default");
    var portableStore = new SessionStore(Path.Combine(folder, "portable-session.json"));
    portableStore.Save(new SessionRecord
    {
        ServerKind = "Dedicated", Address = "test.invalid", Port = 2456,
        CharacterFilename = "portable_character", CharacterId = 23456, ProtectedPassword = portableEncrypted
    });
    Check(compatibility.Unprotect(portableStore.Load().ProtectedPassword) == testPassword, "Portable password survives session serialization and reload");
    Check(!File.ReadAllText(Path.Combine(folder, "portable-session.json")).Contains(testPassword), "Portable session contains no plaintext password");
    File.WriteAllBytes(Path.Combine(keyDirectory, "password.key"), new byte[5]);
    Check(Rejects(() => portable.Unprotect(portableEncrypted)), "Corrupted key rejected");
    Check(Rejects(() => portable.Protect(testPassword)) && new FileInfo(Path.Combine(keyDirectory, "password.key")).Length == 5, "Existing corrupted key is never silently replaced");

    SessionRecord PrivateRecord(string protectedPassword) => new SessionRecord
    {
        ServerKind = "Dedicated", Address = "private.invalid", Port = 2456,
        CharacterFilename = "private_character", CharacterId = 34567,
        ProtectedPassword = protectedPassword
    };
    string configDirectory = Path.Combine(folder, "profile", "BepInEx", "config");
    string gameSaveDirectory = Path.Combine(folder, "valheim-local-data");
    string privatePath = PrivateSessionStorage.ResolveDirectory(configDirectory, gameSaveDirectory);
    Check(!privatePath.StartsWith(Path.Combine(folder, "profile")), "Private data is outside exported profile");
    Check(privatePath.StartsWith(Path.Combine(gameSaveDirectory, "ContinueGame") + Path.DirectorySeparatorChar), "Session is inside local Valheim data, separate from character files");
    Check(privatePath == PrivateSessionStorage.ResolveDirectory(configDirectory, gameSaveDirectory), "Profile private path is stable");
    Check(privatePath != PrivateSessionStorage.ResolveDirectory(Path.Combine(folder, "other-profile", "BepInEx", "config"), gameSaveDirectory), "Profiles have independent private sessions");
    Check(Rejects(() => PrivateSessionStorage.ResolveDirectory(configDirectory, Path.Combine(folder, "profile", "data"))), "Data root inside profile rejected");
    Check(Rejects(() => PrivateSessionStorage.ResolveDirectory(configDirectory, "relative-data")), "Relative private data root rejected");
    var privateStorage = new PrivateSessionStorage(configDirectory, gameSaveDirectory);
    Check(privateStorage.Load() == null && !Directory.Exists(privatePath), "First launch has no private session and creates no files");
    string oldSessionPath = Path.Combine(configDirectory, "ContinueGame.last-session.json");
    string oldKeys = Path.Combine(configDirectory, "ContinueGame.keys");
    string oldCipher = new PortablePasswordProtection(oldKeys).Protect(testPassword);
    new SessionStore(oldSessionPath).Save(PrivateRecord(oldCipher));
    byte[] oldSessionBytes = File.ReadAllBytes(oldSessionPath);
    byte[] oldKeyBytes = File.ReadAllBytes(Path.Combine(oldKeys, "password.key"));
    Check(privateStorage.Load() == null && !Directory.Exists(privatePath), "Existing profile credentials are ignored without migration");
    var normalRecord = PrivateRecord(null!);
    privateStorage.Save(normalRecord, testPassword);
    Check(privateStorage.PasswordProtection.Unprotect(privateStorage.Load().ProtectedPassword) == testPassword, "Normal join creates a working private saved login");
    Check(!File.ReadAllText(Path.Combine(privatePath, "last-session.json")).Contains(testPassword), "Private session contains no plaintext password");
    Check(File.ReadAllBytes(oldSessionPath).SequenceEqual(oldSessionBytes) && File.ReadAllBytes(Path.Combine(oldKeys, "password.key")).SequenceEqual(oldKeyBytes), "Saving and loading never change or delete old profile credentials");
    File.WriteAllText(oldSessionPath, "invalid old data");
    Check(privateStorage.PasswordProtection.Unprotect(privateStorage.Load().ProtectedPassword) == testPassword && File.ReadAllText(oldSessionPath) == "invalid old data", "Corrupt old config cannot affect private saved login");
    privateStorage.Save(normalRecord, "");
    Check(privateStorage.Load().ProtectedPassword == "", "Passwordless normal join updates private saved session");

    var loading = new LoadingState();
    Check(loading.Stage == LoadingStage.SelectingCharacter && !loading.Completed, "Loading begins before character selection");
    loading.Advance(LoadingStage.RestoringPassword);
    loading.Advance(LoadingStage.FindingServer);
    loading.Advance(LoadingStage.RestoringPassword);
    Check(loading.Stage == LoadingStage.FindingServer, "Delayed callbacks cannot rewind the current stage");
    loading.ObserveNativeLoading(false, 1f, true, true);
    Check(!loading.Completed, "Inactive native loading group does not hide the waiting screen");
    loading.ObserveNativeLoading(true, 0f, true, true);
    Check(!loading.Completed, "Transparent native loading group does not cause an early handoff");
    loading.ObserveNativeLoading(true, 1f, false, true);
    Check(!loading.Completed, "Opaque native black screen alone does not hide connection progress");
    loading.Advance(LoadingStage.Connecting);
    loading.Advance(LoadingStage.SendingPassword);
    loading.Advance(LoadingStage.Authenticating);
    Check(!loading.Completed && loading.Stage == LoadingStage.Authenticating, "Current connection stage continues during the recorded second blank interval");
    loading.ObserveNativeLoading(true, 1f, true, false);
    Check(!loading.Completed, "World loading without visible artwork does not cause a premature handoff");
    loading.ObserveNativeLoading(true, 1f, true, true);
    Check(loading.Completed, "Waiting screen ends when native world loading and artwork are both visible");
    loading.ObserveNativeLoading(false, 0f, false, false);
    loading.Advance(LoadingStage.LoadingArea);
    Check(loading.Completed && loading.Stage == LoadingStage.Authenticating, "Native fade changes and late stages cannot resurrect the waiting screen");

    string path = Path.Combine(folder, "session.json");
    var store = new SessionStore(path);
    Check(store.Load() == null, "First launch has no session");
    var record = new SessionRecord
    {
        ServerKind = "Dedicated", Address = "test.invalid", Port = 2456,
        CharacterFilename = "test_character", CharacterId = 12345,
        ProtectedPassword = encrypted, ServerOwner = "Steam_76561198000000000"
    };
    store.Save(record);
    var loaded = store.Load();
    Check(loaded.CharacterId == record.CharacterId && loaded.Port == 2456 && loaded.ServerOwner == record.ServerOwner,
        "Server, character and owner persist");
    Check(platformProtection.Unprotect(loaded.ProtectedPassword) == testPassword, "Password survives save and reload");
    Check(!File.ReadAllText(path).Contains(testPassword), "Saved file contains no plaintext password");
    byte[] previous = File.ReadAllBytes(path);
    record.Port = 0;
    Check(Rejects(() => store.Save(record)), "Invalid server port rejected");
    Check(previous.SequenceEqual(File.ReadAllBytes(path)), "Rejected update preserves last successful session");
    record.Port = 2457;
    store.Save(record);
    Check(store.Load().Port == 2457 && !File.Exists(path + ".tmp"), "Atomic replacement and temporary cleanup");
    record.Version = 999;
    Check(Rejects(() => store.Save(record)), "Unknown schema version rejected");
    File.WriteAllText(path, "broken json");
    Check(Rejects(() => store.Load()), "Corrupted saved session rejected");
    record.Version = 1;
    record.ServerKind = "Steam";
    record.Address = "76561198000000000";
    Check(record.IsValid(), "Steam server record supported");
    record.Address = "invalid";
    Check(!record.IsValid(), "Invalid Steam identity rejected");
    record.ServerKind = "PlayFab";
    record.Address = "synthetic-playfab-id";
    Check(record.IsValid(), "Crossplay server record supported");

    var solo = new SessionRecord
    {
        ServerKind = "LocalWorld", WorldName = "Synthetic solo world", WorldId = 123456789,
        CharacterFilename = "solo_character", CharacterId = 98765, ProtectedPassword = ""
    };
    Check(solo.IsValid() && solo.Address == null, "Single-player session needs a world identity, not a server address");
    Check(solo.MatchesWorld("Synthetic solo world", 123456789), "Saved local world found by name and UID");
    Check(!solo.MatchesWorld("Synthetic solo world", 11111), "A different world with the same name is never resumed");
    Check(!solo.MatchesWorld("Another world", 123456789), "A renamed world requires selecting it normally again");
    Check(!record.MatchesWorld("Synthetic solo world", 123456789), "Remote sessions never select a local world");
    string soloData = Path.Combine(folder, "solo-game-data");
    var soloStorage = new PrivateSessionStorage(configDirectory, soloData);
    soloStorage.Save(solo, "");
    var reloadedSolo = new PrivateSessionStorage(configDirectory, soloData).Load();
    Check(reloadedSolo.IsLocalWorld && reloadedSolo.CharacterId == solo.CharacterId &&
        reloadedSolo.MatchesWorld(solo.WorldName, solo.WorldId), "Single-player world and character survive restart");
    Check(reloadedSolo.ProtectedPassword == "" && !Directory.Exists(Path.Combine(soloStorage.DirectoryPath, "keys")),
        "Single-player save creates a session file without a password key");
    byte[] goodSoloBytes = File.ReadAllBytes(Path.Combine(soloStorage.DirectoryPath, "last-session.json"));
    solo.WorldId = 0;
    Check(Rejects(() => soloStorage.Save(solo, "")), "Local world without UID cannot replace the previous session");
    Check(goodSoloBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(soloStorage.DirectoryPath, "last-session.json"))),
        "Rejected local update leaves the last successful session intact");
    solo.WorldId = 123456789;
    solo.WorldName = " ";
    Check(!solo.IsValid(), "Local world without a name is rejected");
    solo.WorldName = "Synthetic solo world";
    solo.ProtectedPassword = encrypted;
    Check(!solo.IsValid(), "Single-player records cannot carry server credentials");
    solo.ProtectedPassword = "";
    var serverAfterSolo = PrivateRecord(null!);
    soloStorage.Save(serverAfterSolo, testPassword);
    Check(!soloStorage.Load().IsLocalWorld && soloStorage.PasswordProtection.Unprotect(soloStorage.Load().ProtectedPassword) == testPassword,
        "Joining a server after solo replaces the last session and preserves server encryption");
    soloStorage.Save(solo, "");
    Check(soloStorage.Load().IsLocalWorld && soloStorage.Load().ProtectedPassword == "",
        "Starting solo after a server replaces its credentials with a passwordless world record");
    string legacyRemotePath = Path.Combine(folder, "legacy-remote.json");
    File.WriteAllText(legacyRemotePath, "{\"Version\":1,\"ServerKind\":\"Dedicated\",\"Address\":\"legacy.invalid\",\"Port\":2456,\"CharacterFilename\":\"legacy_character\",\"CharacterId\":42,\"ProtectedPassword\":\"\"}");
    Check(new SessionStore(legacyRemotePath).Load().IsValid() && !new SessionStore(legacyRemotePath).Load().IsLocalWorld,
        "Existing 1.0.2 server JSON remains readable without world fields or migration");

    string gameAssembly = args.Length > 0 ? Path.GetFullPath(args[0]) : @"G:\Games\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll";
    using (var gameStream = File.OpenRead(gameAssembly))
    using (var gamePe = new PEReader(gameStream))
    {
        MetadataReader metadata = gamePe.GetMetadataReader();
        bool HasMember(string type, string name, bool field = false)
        {
            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition definition = metadata.GetTypeDefinition(handle);
                if (metadata.GetString(definition.Name) != type) continue;
                if (field)
                {
                    foreach (FieldDefinitionHandle member in definition.GetFields())
                        if (metadata.GetString(metadata.GetFieldDefinition(member).Name) == name) return true;
                }
                else
                {
                    foreach (MethodDefinitionHandle member in definition.GetMethods())
                        if (metadata.GetString(metadata.GetMethodDefinition(member).Name) == name) return true;
                }
            }
            return false;
        }
        foreach (string method in new[] { "Start", "JoinServer", "OnStartGame", "OnWorldStart", "SelectCharacter", "TransitionToMainScene", "LoadMainScene" })
            Check(HasMember("FejdStartup", method), "Installed Valheim menu API: " + method);
        foreach (string field in new[] { "m_world", "m_profiles", "m_profileIndex", "m_openServerToggle", "m_publicServerToggle", "m_crossplayServerToggle", "m_serverPassword" })
            Check(HasMember("FejdStartup", field, true), "Installed native single-player menu state: " + field);
        Check(HasMember("ZNet", "GetWorldIfIsHost") && HasMember("ZNet", "SetServer"), "Installed single-player launch and loaded-world APIs exist");
        foreach (string field in new[] { "m_worldName", "m_uid", "m_dataError", "m_menu" })
            Check(HasMember("World", field, true), "Installed saved-world identity API: " + field);
        var netType = metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Single(type => metadata.GetString(type.Name) == "ZNet");
        var setServer = netType.GetMethods().Select(handle => metadata.GetMethodDefinition(handle))
            .Single(method => metadata.GetString(method.Name) == "SetServer");
        Check((setServer.Attributes & System.Reflection.MethodAttributes.Static) != 0 &&
            setServer.GetParameters().Select(handle => metadata.GetString(metadata.GetParameter(handle).Name))
                .SequenceEqual(new[] { "server", "openServer", "publicServer", "serverName", "password", "world" }),
            "Single-player Harmony hook matches the unique installed SetServer signature");
        Check(HasMember("FejdStartup", "<ServerPassword>k__BackingField", true), "Installed password integration exists");
        Check(HasMember("FejdStartup", "m_menuButtons", true), "Installed menu navigation integration exists");
        Check(HasMember("ZNet", "SendPeerInfo"), "Installed password capture hook exists");
        Check(HasMember("ZNet", "InPasswordDialog"), "Installed password-dialog integration exists");
        foreach (string method in new[] { "ClientConnect", "RPC_ClientHandshake", "RPC_PeerInfo" })
            Check(HasMember("ZNet", method), "Installed pre-artwork connection-stage hook: " + method);
        Check(HasMember("Hud", "UpdateBlackScreen") && HasMember("Hud", "m_loadingScreen", true)
            && HasMember("Hud", "m_loadingProgress", true) && HasMember("Hud", "m_loadingImage", true), "Native world-loading artwork visibility integration exists");
    }
    using (var utilityStream = File.OpenRead(Path.Combine(Path.GetDirectoryName(gameAssembly)!, "assembly_utils.dll")))
    using (var utilityPe = new PEReader(utilityStream))
    {
        MetadataReader utilityMetadata = utilityPe.GetMetadataReader();
        Check(utilityMetadata.TypeDefinitions.Any(handle =>
            utilityMetadata.GetString(utilityMetadata.GetTypeDefinition(handle).Name) == "Utils" &&
            utilityMetadata.GetTypeDefinition(handle).GetMethods().Any(method =>
                utilityMetadata.GetString(utilityMetadata.GetMethodDefinition(method).Name) == "GetSaveDataPath")),
            "Installed Valheim exposes local save directory API");
    }
    using (var pluginStream = File.OpenRead(Path.Combine(project, "bin/Release/ContinueGame.dll")))
    using (var pluginPe = new PEReader(pluginStream))
    {
        MetadataReader metadata = pluginPe.GetMetadataReader();
        Check(!metadata.AssemblyReferences.Any(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name) == "System.ValueTuple"),
            "Plugin has no System.ValueTuple dependency");
        Check(!metadata.AssemblyReferences.Any(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name).Contains("Jotunn", StringComparison.OrdinalIgnoreCase)),
            "Plugin has no Jotunn dependency");
        var nativeModules = metadata.MethodDefinitions
            .Where(handle => (metadata.GetMethodDefinition(handle).Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0)
            .Select(handle => metadata.GetString(metadata.GetModuleReference(metadata.GetMethodDefinition(handle).GetImport().Module).Name)).ToArray();
        Check(!nativeModules.Contains("ContinueGame.Posix"), "No custom native library alias remains in plugin");
        Check(nativeModules.All(name => new[] { "crypt32.dll", "kernel32.dll" }.Contains(name)), "Native imports are limited to Windows password protection");
        Check(!metadata.MethodDefinitions.Where(handle => (metadata.GetMethodDefinition(handle).Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0)
            .Any(handle => metadata.GetString(metadata.GetMethodDefinition(handle).GetImport().Name) == "chmod"), "No chmod import remains in the compiled DLL");
    }
    Console.WriteLine($"Completed {checks} checks.");
}
finally
{
    // Only remove the unique scratch directory created by this run inside this project's tests.
    string allowedPrefix = Path.GetFullPath(Path.Combine(project, "tests", "scratch-"));
    if (!Path.GetFullPath(folder).StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        throw new IOException("Test cleanup path escaped its workspace.");
    Directory.Delete(folder, true);
}
