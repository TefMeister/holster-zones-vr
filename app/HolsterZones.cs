// HolsterZones.cs -- start-up, settings and the self-test of the Holster Zones window (Visceral RE8).
//
//   HolsterZones.exe                      the window
//   HolsterZones.exe --game <folder>      use this game folder instead of looking for Village
//   HolsterZones.exe --selftest <folder>  no window: exercise the holster file in <folder> (a stand-in game folder), print PASS/FAIL
//
// Tefa 2026-11-14, sitting down in the headset: "for testing purposes can you bring all holsters forward and have them marked
// with spheres, so i can reach them sitting down", then "can you make it into an app called holster zones?". Grip Zones' sister:
// the same window parts (Skin, Ticks, Nudge, Json), one row per holster spot instead of one per grip zone.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

static class Program {
    public static readonly string AppData =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HolsterZonesVR");

    [STAThread]
    static int Main(string[] args) {
        string gameArg = null;
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--game" && i + 1 < args.Length) gameArg = args[++i];
            else if (args[i] == "--selftest" && i + 1 < args.Length) return SelfTest(args[++i]) ? 0 : 1;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new HolstersUi(gameArg));
        return 0;
    }

    // ---- self-test: the file rules the game script depends on, in a stand-in game folder -------------------------------
    static bool SelfTest(string folder) {
        bool ok = true;
        Action<bool, string> check = (c, what) => { Console.WriteLine((c ? "PASS " : "FAIL ") + what); if (!c) ok = false; };
        Directory.CreateDirectory(Path.Combine(folder, "reframework"));
        var f = new HolsterFile();
        f.Use(folder);
        foreach (var p in new[] { f.ZonesPath, f.OpenPath, f.StatePath }) if (File.Exists(p)) File.Delete(p);

        var all = f.Load();
        check(all.Count == 0, "no holster_zones.json = an empty set");
        check(HolsterFile.SittingOn(all), "no file: sitting counts as on (the game's own default)");
        check(Math.Abs(HolsterFile.SittingFwd(all) - 0.30) < 1e-9, "no file: sitting forward 30 cm");
        check(double.IsNaN(HolsterFile.Spot(all, "right-hip", "z")), "nothing set = the mod's own place (NaN here)");
        HolsterFile.SetSpot(all, "right-hip", "z", 0.05);
        HolsterFile.SetSpot(all, "right-hip", "r", 0.25);
        HolsterFile.SetSitting(all, false, 0.4);
        f.Save(all);

        var back = f.Load();
        check(Math.Abs(HolsterFile.Spot(back, "right-hip", "z") - 0.05) < 1e-9, "a spot's forward is kept");
        check(Math.Abs(HolsterFile.Spot(back, "right-hip", "r") - 0.25) < 1e-9, "a spot's size is kept");
        check(!HolsterFile.SittingOn(back) && Math.Abs(HolsterFile.SittingFwd(back) - 0.4) < 1e-9, "sitting off and its distance kept");
        HolsterFile.Forget(back, "right-hip");
        check(double.IsNaN(HolsterFile.Spot(back, "right-hip", "z")), "reset drops the spot's own numbers");

        f.WriteOpen("knife");
        var open = Json.Obj(Json.Parse(File.ReadAllText(f.OpenPath)));
        check(open != null && Json.Str(open["key"]) == "knife" && Json.Num(open["t"], 0) > 1e9, "the open row is written with the time");

        File.WriteAllText(f.StatePath, "{\"t\":1,\"spots\":[{\"key\":\"right-hip\",\"hand_cm\":14.5,\"r_cm\":20},{\"key\":\"knife\",\"hand_cm\":30,\"r_cm\":-1}]}");
        var st = f.ReadState();
        check(st != null && st.Find("right-hip") != null && Math.Abs(st.Find("right-hip").HandCm - 14.5) < 1e-9, "the hand distance is read");
        check(st != null && st.Find("knife") != null && st.Find("knife").RadiusCm < 0, "an unknown size reads as unknown");
        check(f.ScriptAlive(), "a fresh state file counts as the game script running");
        File.WriteAllText(f.StatePath, "{\"t\":1,\"spots\":[{\"key");
        check(f.ReadState() == null, "a half-written state file is skipped, not a crash");
        Console.WriteLine(ok ? "SELFTEST OK" : "SELFTEST FAILED");
        return ok;
    }
}

// settings.txt: one `key=value` per line, in %APPDATA%\HolsterZonesVR
class Settings {
    readonly string file = Path.Combine(Program.AppData, "settings.txt");
    readonly Dictionary<string, string> values = new Dictionary<string, string>();

    public Settings() {
        try {
            if (File.Exists(file))
                foreach (var line in File.ReadAllLines(file)) {
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
        } catch (IOException) { }
    }

    public string Get(string key, string dflt) { string v; return values.TryGetValue(key, out v) ? v : dflt; }

    public void Set(string key, string value) {
        values[key] = value;
        try {
            Directory.CreateDirectory(Program.AppData);
            File.WriteAllLines(file, values.Select(kv => kv.Key + "=" + kv.Value).ToArray());
        } catch (IOException) { }
    }
}
