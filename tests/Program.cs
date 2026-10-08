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
    string testPassword = "Synthetic-check-" + Guid.NewGuid().ToString("N") + "-пароль";
    string encrypted = PasswordProtection.Protect(testPassword);
    Check(encrypted != testPassword && encrypted.Length > 0, "Password encrypted");
    Check(PasswordProtection.Unprotect(encrypted) == testPassword, "Windows DPAPI Unicode round trip");
    Check(PasswordProtection.Protect(testPassword) != encrypted, "Encryption uses fresh randomness");
    byte[] modified = Convert.FromBase64String(encrypted);
    modified[modified.Length - 1] ^= 0x40;
    Check(Rejects(() => PasswordProtection.Unprotect(Convert.ToBase64String(modified))), "Tampered password rejected");
    Check(Rejects(() => PasswordProtection.Unprotect("not-base64")), "Malformed ciphertext rejected");
    Check(PasswordProtection.Unprotect(PasswordProtection.Protect("")) == "", "Passwordless server supported");

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
    Check(compatibility.Unprotect(encrypted) == testPassword, "Legacy Windows passwords remain compatible");
    Check(compatibility.Unprotect(portableEncrypted) == testPassword, "Portable password format selected on Windows too");
    Check(PasswordProtection.Unprotect(compatibility.Protect(testPassword)) == testPassword, "Windows still uses DPAPI by default");
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

    var loading = new LoadingState();
    Check(loading.Stage == LoadingStage.SelectingCharacter && !loading.Completed, "Loading begins before character selection");
    LoadingStage[] loginStages = {
        LoadingStage.SelectingCharacter, LoadingStage.RestoringPassword, LoadingStage.FindingServer,
        LoadingStage.LoadingScene, LoadingStage.Connecting, LoadingStage.SendingPassword,
        LoadingStage.Authenticating, LoadingStage.ReceivingWorld, LoadingStage.LoadingArea,
        LoadingStage.PreparingCharacter, LoadingStage.Ready
    };
    foreach (LoadingStage stage in loginStages.Skip(1).Take(loginStages.Length - 2)) loading.Advance(stage);
    Check(!loading.Completed && loading.Stage == LoadingStage.PreparingCharacter, "Spawn preparation does not imply a completed login");
    loading.Complete();
    Check(loading.Completed && loading.Visited.SequenceEqual(loginStages), "Fast and late stages remain visible in the full login history");
    loading.Advance(LoadingStage.RestoringPassword);
    loading.Advance(LoadingStage.Ready);
    Check(loading.Visited.SequenceEqual(loginStages), "Duplicate and late callbacks cannot rewind or duplicate a completed login");
    var passwordlessLoading = new LoadingState();
    passwordlessLoading.Advance(LoadingStage.Connecting);
    passwordlessLoading.Advance(LoadingStage.Authenticating);
    passwordlessLoading.Advance(LoadingStage.Connecting);
    Check(!passwordlessLoading.Visited.Contains(LoadingStage.SendingPassword) && passwordlessLoading.Stage == LoadingStage.Authenticating,
        "Passwordless flow does not invent a password step or rewind after a delayed callback");
    Check(Rejects(() => ((IList<LoadingStage>)loading.Visited).Add(LoadingStage.SelectingCharacter)), "Displayed history cannot be changed externally");

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
    Check(PasswordProtection.Unprotect(loaded.ProtectedPassword) == testPassword, "Password survives save and reload");
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

    string gameAssembly = @"G:\Games\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll";
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
        foreach (string method in new[] { "Start", "JoinServer", "OnStartGame", "SelectCharacter", "TransitionToMainScene", "LoadMainScene" })
            Check(HasMember("FejdStartup", method), "Installed Valheim menu API: " + method);
        Check(HasMember("FejdStartup", "<ServerPassword>k__BackingField", true), "Installed password integration exists");
        Check(HasMember("FejdStartup", "m_menuButtons", true), "Installed menu navigation integration exists");
        Check(HasMember("ZNet", "SendPeerInfo"), "Installed password capture hook exists");
        Check(HasMember("ZNet", "InPasswordDialog"), "Installed password-dialog integration exists");
        foreach (string method in new[] { "ClientConnect", "RPC_ClientHandshake", "RPC_PeerInfo" })
            Check(HasMember("ZNet", method), "Installed connection-stage hook: " + method);
        Check(HasMember("Game", "FindSpawnPoint") && HasMember("Game", "SpawnPlayer"), "Installed area-loading and character-spawn hooks exist");
        Check(HasMember("Hud", "UpdateBlackScreen") && HasMember("Hud", "m_loadingScreen", true) && HasMember("Hud", "m_loadingTip", true), "Native loading-screen handoff and caption integration exist");
    }
    using (var pluginStream = File.OpenRead(Path.Combine(project, "bin/Release/ContinueGame.dll")))
    using (var pluginPe = new PEReader(pluginStream))
    {
        MetadataReader metadata = pluginPe.GetMetadataReader();
        Check(!metadata.AssemblyReferences.Any(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name) == "System.ValueTuple"),
            "Plugin has no System.ValueTuple dependency");
        Check(!metadata.AssemblyReferences.Any(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name).Contains("Jotunn", StringComparison.OrdinalIgnoreCase)),
            "Plugin has no Jotunn dependency");
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
