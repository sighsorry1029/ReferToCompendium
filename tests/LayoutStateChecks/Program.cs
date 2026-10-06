using ReferToCompendium;

string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
string directory = Path.Combine(temporaryRoot, "ReferToCompendium-LayoutChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
int checks = 0;
try
{
    void Check(bool success, string name)
    {
        if (!success) throw new Exception("FAILED: " + name);
        checks++;
    }
    void Order(LayoutState state, string?[] available, params string[] expected) => Check(state.GetOrderedIds(available).SequenceEqual(expected), "Order: " + string.Join(",", expected));
    var warnings = new List<string>();
    var state = new LayoutState();
    string[] all = { "a", "b", "c" };
    Order(state, all, "a", "b", "c");
    Check(state.Move("c", -1, all), "Move up");
    Order(state, new[] { "c", "a", "b" }, "a", "c", "b");
    Order(state, new[] { "b", "a", "new" }, "a", "b", "new");
    Order(state, new[] { "b", "c", "a", "new" }, "a", "c", "b", "new");
    Check(state.SetCollapsed("c", true), "Collapse");
    Check(!state.SetCollapsed("c", true), "Collapse idempotent");
    Check(!state.Move("c", -1, all), "Only entry in collapsed bucket does not move");
    Check(state.Move("b", -1, all), "Move expanded across collapsed");
    Order(state, all, "b", "c", "a");
    Check(state.IsCollapsed("c"), "Collapsed state retained");
    state.Reset(new[] { "a", "b" });
    Order(state, new[] { "b", "a" }, "a", "b");
    Check(state.IsCollapsed("c"), "Reset preserves absent collapse");
    Order(state, all, "a", "c", "b");
    state.Reset(all);
    Order(state, all, "a", "b", "c");
    Check(!state.IsCollapsed("c"), "Reset expands present IDs");
    Check(state.Move("c", int.MinValue, all), "Large negative move is bounded");
    Check(state.Move("c", int.MaxValue, all), "Large positive move is bounded");
    Order(state, new[] { "a", "", "a", null, "b", "c" }, "a", "b", "c");
    var absent = new LayoutState();
    absent.GetOrderedIds(all);
    absent.SetCollapsed("b", true);
    absent.Move("c", -1, new[] { "a", "c" });
    Order(absent, all, "c", "b", "a");
    Check(absent.IsCollapsed("b"), "Absent state survives movement of other entries");
    absent.SetCollapsed("c", true);
    Check(absent.Move("b", -1, all), "Collapsed entries reorder within their own bucket");
    Order(absent, all, "b", "c", "a");
    Check(!absent.Move("unknown", 1, all), "Unknown entry does not move");

    // A drag can cross several visible rows while unavailable mods and the other
    // collapse bucket remain interleaved in the saved order.
    string dragPath = Path.Combine(directory, "drag.json");
    var dragStore = new LayoutStore(dragPath, warnings.Add);
    string[] dragAll = { "a", "absent-expanded", "folded-a", "b", "folded-b", "absent-collapsed", "c", "folded-c", "d" };
    string[] dragAvailable = { "d", "folded-c", "c", "folded-b", "b", "folded-a", "a" };
    dragStore.State.GetOrderedIds(dragAll);
    foreach (string id in new[] { "folded-a", "folded-b", "absent-collapsed", "folded-c" })
        dragStore.State.SetCollapsed(id, true);
    Check(dragStore.State.Move("a", 3, dragAvailable), "Drag first expanded row to last available slot");
    Order(dragStore.State, dragAll, "b", "absent-expanded", "folded-a", "c", "folded-b", "absent-collapsed", "d", "folded-c", "a");
    Check(dragStore.State.Move("folded-c", -2, dragAvailable), "Drag last collapsed row to first available slot");
    string[] dragExpected = { "b", "absent-expanded", "folded-c", "c", "folded-a", "absent-collapsed", "d", "folded-b", "a" };
    Order(dragStore.State, dragAll, dragExpected);
    Check(!dragStore.State.Move("c", 0, dragAvailable), "Dropping onto the same slot does not change state");
    Check(!dragStore.State.Move("absent-expanded", 2, dragAvailable), "An unavailable drag source does not move");
    Order(dragStore.State, dragAll, dragExpected);
    Check(dragStore.Save(), "Save long-distance drag order");
    var dragReloaded = new LayoutStore(dragPath, warnings.Add);
    Order(dragReloaded.State, dragAll, dragExpected);
    Check(new[] { "folded-a", "folded-b", "absent-collapsed", "folded-c" }.All(dragReloaded.State.IsCollapsed)
        && new[] { "a", "absent-expanded", "b", "c", "d" }.All(id => !dragReloaded.State.IsCollapsed(id)),
        "Drag roundtrip retains both present and unavailable collapse states");
    Check(dragReloaded.State.Move("a", -3, dragAvailable), "Drag expanded row back up across three available slots");
    Order(dragReloaded.State, dragAll, "a", "absent-expanded", "folded-c", "b", "folded-a", "absent-collapsed", "c", "folded-b", "d");
    Check(dragReloaded.State.Move("folded-c", 2, dragAvailable), "Drag collapsed row back down across two available slots");
    Order(dragReloaded.State, dragAll, dragAll);

    string path = Path.Combine(directory, "layout.json");
    var store = new LayoutStore(path, warnings.Add);
    store.State.GetOrderedIds(all);
    store.State.SetCollapsed("b", true);
    Check(store.State.Move("c", -1, all), "Persistent reorder");
    Check(store.Save(), "Initial save");
    var loaded = new LayoutStore(path, warnings.Add);
    Order(loaded.State, all, "c", "b", "a");
    Check(loaded.State.IsCollapsed("b"), "Roundtrip collapsed");
    loaded.State.SetCollapsed("a", true);
    Check(loaded.Save(), "Replace save creates backup");
    byte[] goodBackup = File.ReadAllBytes(path + ".bak");
    File.WriteAllText(path, "{ broken");
    var recovered = new LayoutStore(path, warnings.Add);
    Check(recovered.State.IsCollapsed("b") && !recovered.State.IsCollapsed("a"), "Recover previous valid backup");
    Check(Directory.GetFiles(directory, "layout.json.corrupt-*").Length == 1, "Preserve corrupt primary");
    Check(recovered.Save(), "Save recovered state");
    Check(File.ReadAllBytes(path + ".bak").SequenceEqual(goodBackup), "Recovery must not replace good backup with corruption");
    Check(Directory.GetFiles(directory, "layout.json.corrupt-*").Length == 1, "Do not duplicate preservation on save");
    Check(!Directory.GetFiles(directory, "*.tmp").Any(), "No stale temporary files");

    string futurePath = Path.Combine(directory, "future.json");
    string futureJson = "{\"version\":2,\"entries\":{\"different\":\"shape\"}}";
    File.WriteAllText(futurePath, futureJson);
    var future = new LayoutStore(futurePath, warnings.Add);
    future.State.SetCollapsed("a", true);
    Check(!future.Save() && File.ReadAllText(futurePath) == futureJson, "Future format is never overwritten");
    string latePath = Path.Combine(directory, "late.json");
    var late = new LayoutStore(latePath, warnings.Add);
    File.WriteAllText(latePath, futureJson);
    Check(!late.Save() && File.ReadAllText(latePath) == futureJson, "Future file written after load is preserved");
    string futureBackupPath = Path.Combine(directory, "future-backup.json");
    File.WriteAllText(futureBackupPath, "broken");
    File.WriteAllText(futureBackupPath + ".bak", futureJson);
    Check(!new LayoutStore(futureBackupPath, warnings.Add).Save(), "Future backup also blocks replacement");
    Check(File.ReadAllText(futureBackupPath + ".bak") == futureJson, "Future backup remains intact");

    string[] invalid = { "{}", "null", "{\"version\":1,\"entries\":null}", "{\"version\":1,\"entries\":[{\"id\":\"a\"},{\"id\":\"a\"}]}", "{\"version\":1,\"entries\":[]} trailing" };
    for (int i = 0; i < invalid.Length; ++i)
    {
        string invalidPath = Path.Combine(directory, "invalid-" + i + ".json");
        File.WriteAllText(invalidPath, invalid[i]);
        var malformed = new LayoutStore(invalidPath, warnings.Add);
        Check(Directory.GetFiles(directory, Path.GetFileName(invalidPath) + ".corrupt-*").Length == 1, "Malformed file preserved " + i);
        Check(malformed.Save(), "Malformed file can be replaced after preservation " + i);
    }
    string oversizedPath = Path.Combine(directory, "oversized.json");
    File.WriteAllText(oversizedPath, new string(' ', 2 * 1024 * 1024 + 1));
    Check(!new LayoutStore(oversizedPath, warnings.Add).Save(), "Oversized file is read-limited and preserved");
    string missingPath = Path.Combine(directory, "missing.json");
    File.Copy(path + ".bak", missingPath + ".bak");
    var missing = new LayoutStore(missingPath, warnings.Add);
    Check(missing.State.IsCollapsed("b") && missing.Save(), "Missing primary recovers backup");
    string unicodePath = Path.Combine(directory, "unicode.json");
    var unicode = new LayoutStore(unicodePath, warnings.Add);
    unicode.State.SetCollapsed("mod|도감\n\"\\💬", true);
    Check(unicode.Save() && new LayoutStore(unicodePath, warnings.Add).State.IsCollapsed("mod|도감\n\"\\💬"), "Unicode and escaped IDs roundtrip");
    Console.WriteLine($"PASS: {checks} layout state and storage checks.");
}
finally
{
    // Verify the resolved target before recursively deleting the test-owned directory.
    if (!Path.GetFullPath(directory).StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(directory).StartsWith("ReferToCompendium-LayoutChecks-", StringComparison.Ordinal))
        throw new InvalidOperationException("Refusing to clean a directory outside the test temporary root.");
    Directory.Delete(directory, true);
}
