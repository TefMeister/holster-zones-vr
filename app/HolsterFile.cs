// HolsterFile.cs -- the three files the Holster Zones window shares with Visceral RE8, all in the game's
// reframework\data\visceral_re8\ folder:
//   holster_zones.json        WRITTEN HERE: { "sitting": { "on": true, "fwd": 0.30 },
//                                             "spots": { "<key>": { "x": right, "y": up, "z": forward, "r": size } } }
//                             metres. x / y / z move a spot from the mod's own place, in the body's facing; r is how close the
//                             right hand must come (the four Visceral spots, and since 2026-11-16 the ammo pouch: how close the LEFT hand must come; Andyalpa's knife and medicine keep
//                             their own). "sitting" moves every spot forward together. The game re-reads it twice a second.
//   holster_zones_open.json   WRITTEN HERE: { "key": "<the open row>", "t": <unix seconds> }, about once a second; the game
//                             shows the sphere of the open row's spot (a file older than 3 s = the window is closed).
//   holster_zones_state.json  READ HERE: written by lua/visceral_re8_holsters.lua four times a second: per spot its key, how far
//                             the nearer hand is from it and its size (cm; -1 = not known).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

class LiveSpot {
    public string Key = "";
    public double HandCm = -1;   // -1 = not known
    public double RadiusCm = -1;
}

class LiveHolsters {
    public List<LiveSpot> Spots = new List<LiveSpot>();
    public LiveSpot Find(string key) { foreach (var s in Spots) if (s.Key == key) return s; return null; }
}

class HolsterFile {
    public const double STALE_SEC = 3.0;   // holster_zones_state.json older than this = the game script is not running
    public string Folder { get; private set; }
    public string DataDir { get { return Folder == null ? null : Path.Combine(Folder, "reframework", "data", "visceral_re8"); } }
    public string ZonesPath { get { return DataDir == null ? null : Path.Combine(DataDir, "holster_zones.json"); } }
    public string OpenPath { get { return DataDir == null ? null : Path.Combine(DataDir, "holster_zones_open.json"); } }
    public string StatePath { get { return DataDir == null ? null : Path.Combine(DataDir, "holster_zones_state.json"); } }

    public void Use(string folder) { Folder = folder; if (folder != null) Directory.CreateDirectory(DataDir); }

    public static bool LooksLikeVillage(string folder) {
        return folder != null && File.Exists(Path.Combine(folder, "re8.exe")) && Directory.Exists(Path.Combine(folder, "reframework"));
    }

    // the running game first, then Steam's usual places
    public static string FindVillage() {
        foreach (var p in Process.GetProcessesByName("re8")) {
            try { string d = Path.GetDirectoryName(p.MainModule.FileName); if (LooksLikeVillage(d)) return d; } catch (Exception) { }
        }
        foreach (var root in new[] { @"C:\Steam", @"C:\Program Files (x86)\Steam", @"D:\Steam", @"D:\SteamLibrary", @"E:\SteamLibrary" }) {
            string d = Path.Combine(root, "steamapps", "common", "Resident Evil Village BIOHAZARD VILLAGE");
            if (LooksLikeVillage(d)) return d;
        }
        return null;
    }

    // ---- holster_zones.json -------------------------------------------------------------------------------------------
    public Dictionary<string, object> Load() {
        try {
            if (File.Exists(ZonesPath)) { var o = Json.Obj(Json.Parse(File.ReadAllText(ZonesPath))); if (o != null) return o; }
        } catch (Exception) { }
        return new Dictionary<string, object>();
    }

    public void Save(Dictionary<string, object> all) {
        if (Folder == null) return;
        string tmp = ZonesPath + ".tmp";
        File.WriteAllText(tmp, Json.Write(all));
        if (File.Exists(ZonesPath)) File.Replace(tmp, ZonesPath, null); else File.Move(tmp, ZonesPath);
    }

    static Dictionary<string, object> Child(Dictionary<string, object> o, string key, bool make) {
        object v;
        if (o.TryGetValue(key, out v) && v is Dictionary<string, object>) return (Dictionary<string, object>)v;
        if (!make) return null;
        var c = new Dictionary<string, object>();
        o[key] = c;
        return c;
    }

    // a spot's number; NaN = not set (the mod's own applies)
    public static double Spot(Dictionary<string, object> all, string key, string axis) {
        var s = Child(all, "spots", false);
        var e = s == null ? null : Child(s, key, false);
        object v;
        return e != null && e.TryGetValue(axis, out v) && v is double ? (double)v : double.NaN;
    }
    public static void SetSpot(Dictionary<string, object> all, string key, string axis, double value) {
        Child(Child(all, "spots", true), key, true)[axis] = value;
    }
    public static void Forget(Dictionary<string, object> all, string key) {
        var s = Child(all, "spots", false);
        if (s != null) s.Remove(key);
    }

    public const double SIT_FWD_DEFAULT = 0.30;
    public static bool SittingOn(Dictionary<string, object> all) {
        var s = Child(all, "sitting", false);
        object v;
        return s == null || !s.TryGetValue("on", out v) || Json.Bool(v);   // no file yet: the game's own default, sitting on
    }
    public static double SittingFwd(Dictionary<string, object> all) {
        var s = Child(all, "sitting", false);
        object v;
        return s != null && s.TryGetValue("fwd", out v) && v is double ? (double)v : SIT_FWD_DEFAULT;
    }
    public static void SetSitting(Dictionary<string, object> all, bool on, double fwd) {
        var s = Child(all, "sitting", true);
        s["on"] = on; s["fwd"] = fwd;
    }

    // ---- holster_zones_open.json --------------------------------------------------------------------------------------
    public void WriteOpen(string key) {
        if (DataDir == null || !Directory.Exists(DataDir)) return;
        long t = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        string tmp = OpenPath + ".tmp";
        File.WriteAllText(tmp, "{ \"key\": \"" + (key ?? "").Replace("\"", "") + "\", \"t\": " + t + " }");
        if (File.Exists(OpenPath)) File.Replace(tmp, OpenPath, null); else File.Move(tmp, OpenPath);
    }

    // ---- holster_zones_state.json -------------------------------------------------------------------------------------
    public bool ScriptAlive() {
        try { return File.Exists(StatePath) && (DateTime.Now - File.GetLastWriteTime(StatePath)).TotalSeconds < STALE_SEC; }
        catch (IOException) { return false; }
    }

    public LiveHolsters ReadState() {
        try {
            if (!File.Exists(StatePath)) return null;
            var o = Json.Obj(Json.Parse(File.ReadAllText(StatePath)));
            if (o == null) return null;
            var st = new LiveHolsters();
            object v;
            if (o.TryGetValue("spots", out v) && Json.Arr(v) != null)
                foreach (var item in Json.Arr(v)) {
                    var z = Json.Obj(item);
                    if (z == null) continue;
                    object k, h, r;
                    st.Spots.Add(new LiveSpot {
                        Key = z.TryGetValue("key", out k) ? Json.Str(k) : "",
                        HandCm = z.TryGetValue("hand_cm", out h) ? Json.Num(h, -1) : -1,
                        RadiusCm = z.TryGetValue("r_cm", out r) ? Json.Num(r, -1) : -1,
                    });
                }
            return st;
        } catch (Exception) { return null; }   // caught mid-write: the next read gets it
    }
}
