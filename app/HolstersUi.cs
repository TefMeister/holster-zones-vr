// HolstersUi.cs -- the Holster Zones window. Top: the game, the SITTING switch (every holster forward together, with its
// distance) and the controller-button note. Then one row per holster spot with an arrow: clicking one opens its box (and closes
// the one that was open) and the game shows that spot's sphere; clicking it again closes it. A box has the live line ("hand
// 14 cm away, needs 20", green when the hand is inside), three sliders (back / forward, down / up, left / right) and, on the
// four Visceral spots, the size, each with - and + buttons for half-centimetre steps, and RESET. Every change goes into
// holster_zones.json at once; the game re-reads it twice a second. Large type, so Window View can hang it in the headset.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

class HolstersUi : Form {
    const int POLL_MS = 250, SAVE_DEBOUNCE_MS = 60, FIND_GAME_EVERY_MS = 5000;
    const int OFFSET_MM = 400, SIZE_MIN_MM = 50, SIZE_MAX_MM = 400, SIT_MAX_MM = 700, NUDGE_MM = 5;
    const int OWN_SIZE_MM = 200;   // the Visceral spots' own size (visceral_re8_holsters.lua ENTER_M)
    const double OPEN_EVERY_S = 1.0;
    static readonly Color Back = Color.FromArgb(28, 30, 38), Panel2 = Color.FromArgb(40, 43, 55), Ink = Color.FromArgb(235, 235, 240);
    static readonly Color Dim = Color.FromArgb(155, 160, 175), Good = Color.FromArgb(90, 200, 120), Bad = Color.FromArgb(225, 120, 95);
    static readonly Color Warn = Color.FromArgb(230, 190, 90), ButtonBack = Color.FromArgb(62, 67, 86);

    // the spots, top to bottom: key (the game's name), title, what the hand does there, and whether its size can be set here
    static readonly string[][] SPOTS = {
        new[] { "right-shoulder", "RIGHT SHOULDER", "quick slot UP: RG takes that gun out, or puts it away", "size" },
        new[] { "right-hip", "RIGHT HIP", "quick slot RIGHT: RG takes that gun out, or puts it away", "size" },
        new[] { "left-hip", "LEFT HIP", "quick slot DOWN: RG takes that gun out, or puts it away", "size" },
        new[] { "left-shoulder", "LEFT SHOULDER", "quick slot LEFT: RG takes that gun out, or puts it away", "size" },
        new[] { "knife", "KNIFE  (chest)", "the knife (Andyalpa's holster; its size is the mod's own)", "" },
        new[] { "medicine", "MEDICINE", "a healing item (Andyalpa's holster; its size is the mod's own)", "" },
        new[] { "ammo", "AMMO POUCH", "spare rounds for a reload (Andyalpa's; its size is the mod's own)", "" },
    };

    readonly Settings settings = new Settings();
    readonly HolsterFile file = new HolsterFile();
    readonly Timer poll = new Timer(), saveTimer = new Timer(), nudgeTimer = new Timer();
    readonly Nudge nudge = new Nudge();
    Label status, nudgeNote;
    SkinPanel scroll;
    CheckBox sitOn;
    Slider sitFwd;
    bool sitTouched, syncing;
    DateTime lastFind = DateTime.MinValue, lastZonesWrite = DateTime.MinValue, lastOpenWrite = DateTime.MinValue;
    LiveHolsters live;
    readonly List<SpotBox> boxes = new List<SpotBox>();
    readonly Dictionary<string, Button> heads = new Dictionary<string, Button>();
    string openKey = "", lastOpenKey = null, layoutSig = "";
    const int HEAD_H = 40;

    public HolstersUi(string gameArg) {
        Text = "Holster Zones";
        BackColor = Back; ForeColor = Ink;
        Font = new Font("Segoe UI", 11f);
        ClientSize = new Size(760, 900);   // 2026-11-14: wider for the bigger slider names
        MinimumSize = new Size(520, 560);
        StartPosition = FormStartPosition.CenterScreen;
        string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "holster_zones.ico");
        try { Icon = new Icon(ico); } catch (Exception) { }
        DoubleBuffered = true;
        Backdrop.Init(this, ico, Back);

        scroll = new SkinPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        Controls.Add(scroll);
        Ticks.NameSection = (c, n) => nudge.Section(c, n);
        foreach (var s in SPOTS) boxes.Add(new SpotBox(this, s[0], s[1], s[2], s[3] == "size"));
        for (int i = boxes.Count - 1; i >= 0; i--) scroll.Controls.Add(boxes[i]);
        openKey = settings.Get("open", "");
        BuildTop();

        string folder = gameArg ?? settings.Get("game", null);
        if (!HolsterFile.LooksLikeVillage(folder) && gameArg == null) folder = HolsterFile.FindVillage();
        if (folder != null && (gameArg != null || HolsterFile.LooksLikeVillage(folder))) UseFolder(folder, gameArg == null);

        Shown += (s, e) => { ActiveControl = null; scroll.AutoScrollPosition = new Point(0, 0); };
        poll.Interval = POLL_MS; poll.Tick += (s, e) => Poll(); poll.Start();
        saveTimer.Interval = SAVE_DEBOUNCE_MS; saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveAll(); };
        nudgeTimer.Interval = 15; nudgeTimer.Tick += (s, e) => NudgeTick(); nudgeTimer.Start();
        Ticks.Changed += ShowNudge;
        ShowNudge();
        Poll();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.PaintUnder(this, e.Graphics, e.ClipRectangle); }
    protected override CreateParams CreateParams { get { return Backdrop.Composited(base.CreateParams); } }

    // ---- building -------------------------------------------------------------------------------------------------------
    internal static Button Btn(string text, EventHandler onClick, int width = 0) {
        var b = new SkinButton { Text = text, FlatStyle = FlatStyle.Flat, BackColor = ButtonBack, ForeColor = Ink, Height = 34, AutoSize = width == 0, Margin = new Padding(3) };
        b.FlatAppearance.BorderSize = 0;
        if (width > 0) b.Width = width;
        b.Click += onClick;
        return b;
    }

    static FlowLayoutPanel Row(int height) {
        return new FlowLayoutPanel { Dock = DockStyle.Top, Height = height, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(6, 2, 6, 0) };
    }

    void BuildTop() {
        var top = new SkinPanel { Dock = DockStyle.Top, Height = 214, Tint = Color.FromArgb(120, Back) };
        Controls.Add(top);
        status = new SkinLabel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(10, 8, 0, 0), ForeColor = Dim };
        var one = Row(42);
        one.Controls.Add(Btn("GAME FOLDER...", (s, e) => PickFolder()));
        var two = Row(42);
        sitOn = new SkinCheckBox { Text = "SITTING: every holster forward together", AutoSize = true, ForeColor = Ink, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };
        sitOn.CheckedChanged += (s, e) => { if (!syncing) { sitTouched = true; Changed(); } };
        two.Controls.Add(sitOn);
        var sitRow = new SkinPanel { Dock = DockStyle.Top, Height = 46, Tint = Color.FromArgb(0, Back), Padding = new Padding(8, 2, 8, 2) };
        sitFwd = new Slider(() => { if (!syncing) { sitTouched = true; Changed(); } }, "sitting: forward", 0, SIT_MAX_MM, false);
        sitRow.Controls.Add(sitFwd);
        var three = Row(42);
        three.Controls.Add(Btn("CLEAR ALL TICKS", (s, e) => Ticks.ClearAll()));
        nudgeNote = new SkinLabel { AutoSize = true, ForeColor = Dim, Padding = new Padding(4, 8, 0, 0) };
        three.Controls.Add(nudgeNote);
        top.Controls.Add(three);
        top.Controls.Add(sitRow);
        top.Controls.Add(two);
        top.Controls.Add(one);
        top.Controls.Add(status);
    }

    // ---- one row open at a time ---------------------------------------------------------------------------------------------
    Button Head(string key) {
        Button b;
        if (heads.TryGetValue(key, out b)) return b;
        b = Btn("", (s, ev) => PickSpot(openKey == key ? "" : key));
        b.Dock = DockStyle.Top; b.AutoSize = false; b.Height = HEAD_H; b.TextAlign = ContentAlignment.MiddleLeft;
        b.Font = new Font("Segoe UI Semibold", 12f);
        heads[key] = b;
        scroll.Controls.Add(b);
        return b;
    }

    void ShowRows() {
        var order = new List<Control>();
        foreach (var box in boxes) {
            var h = Head(box.Key);
            bool open = box.Key == openKey;
            string t = (open ? "▼   " : "▶   ") + box.Title + box.HeadNote(live);
            if (h.Text != t) h.Text = t;
            h.BackColor = open ? Color.FromArgb(70, 90, 110) : ButtonBack;
            h.ForeColor = box.Inside(live) ? Good : Ink;
            order.Add(h);
            if (open) order.Add(box);
        }
        var shown = new HashSet<Control>(order);
        foreach (Control c in scroll.Controls) if (c.Visible != shown.Contains(c)) c.Visible = shown.Contains(c);
        var sig = new System.Text.StringBuilder();
        foreach (var c in order) sig.Append(c.GetHashCode()).Append(',');
        if (sig.ToString() == layoutSig) return;
        layoutSig = sig.ToString();
        scroll.SuspendLayout();
        int n = scroll.Controls.Count;
        for (int k = 0; k < order.Count; ++k) scroll.Controls.SetChildIndex(order[k], n - 1 - k);
        scroll.ResumeLayout();
    }

    void PickSpot(string key) {
        openKey = key;
        settings.Set("open", key);
        ShowRows();
        WriteOpen(true);
    }

    // ---- the controller nudge (Nudge.cs, the same buttons as Hand Poser and Grip Zones) ------------------------------------
    void ShowNudge() {
        if (nudgeNote == null) return;
        int n = Ticks.Count;
        nudgeNote.Text = n == 0 ? "tick sliders: right stick: left / right -1 / +1, up / down +5 / -5; Y clears the ticks" : !nudge.Linked ? n + " ticked: start Window View, click the left stick in"
            : n + " ticked: right stick -1 / +1 / +5 / -5 (held repeats), Y clears";
        nudgeNote.ForeColor = n > 0 && nudge.Linked ? Good : Dim;
    }
    void NudgeTick() {
        bool started;
        double step = nudge.Poll(Ticks.Count > 0, out started);
        if (step == 0) return;
        Ticks.StepAll((int)step, nudge.Allows);
    }

    // ---- the game ---------------------------------------------------------------------------------------------------------
    void UseFolder(string folder, bool remember) {
        try { file.Use(folder); } catch (Exception ex) { status.Text = "cannot use that folder: " + ex.Message; return; }
        if (remember) settings.Set("game", folder);
        lastZonesWrite = DateTime.MinValue;
        RefreshFromFile();
    }

    void PickFolder() {
        using (var d = new FolderBrowserDialog { Description = "Pick Resident Evil Village's install folder (the one with re8.exe)" }) {
            if (d.ShowDialog(this) == DialogResult.OK) UseFolder(d.SelectedPath, true);
        }
    }

    void Poll() {
        if (file.Folder == null) {
            if ((DateTime.Now - lastFind).TotalMilliseconds > FIND_GAME_EVERY_MS) {
                lastFind = DateTime.Now;
                string f = HolsterFile.FindVillage();
                if (f != null) UseFolder(f, true);
            }
            status.Text = "Village not found: press GAME FOLDER...";
            status.ForeColor = Bad;
            return;
        }
        bool alive = file.ScriptAlive();
        live = alive ? (file.ReadState() ?? live) : null;
        if (!alive) {
            status.Text = "Village found, but the game is not in play (or the holster script is not in it)";
            status.ForeColor = Warn;
        } else {
            status.Text = "connected · open a row to see its sphere in the game";
            status.ForeColor = Good;
        }
        DateTime w = DateTime.MinValue;
        try { if (File.Exists(file.ZonesPath)) w = File.GetLastWriteTime(file.ZonesPath); } catch (IOException) { }
        if (!saveTimer.Enabled && w != lastZonesWrite) RefreshFromFile();
        foreach (var b in boxes) b.ShowLive(live);
        ShowRows();
        WriteOpen(false);
    }

    void RefreshFromFile() {
        if (file.Folder == null) return;
        try { lastZonesWrite = File.Exists(file.ZonesPath) ? File.GetLastWriteTime(file.ZonesPath) : DateTime.MinValue; } catch (IOException) { }
        var all = file.Load();
        syncing = true;
        sitOn.Checked = HolsterFile.SittingOn(all);
        sitFwd.Set(HolsterFile.SittingFwd(all) * 1000);
        foreach (var b in boxes) b.Load(all);
        syncing = false;
    }

    internal void Changed() { if (!syncing) { saveTimer.Stop(); saveTimer.Start(); } }

    void SaveAll() {
        if (file.Folder == null) return;
        var all = file.Load();
        if (sitTouched || !all.ContainsKey("sitting")) HolsterFile.SetSitting(all, sitOn.Checked, sitFwd.Value / 1000.0);
        sitTouched = false;
        foreach (var b in boxes) b.Store(all);
        try { file.Save(all); lastZonesWrite = File.GetLastWriteTime(file.ZonesPath); }
        catch (Exception ex) { status.Text = "could not save: " + ex.Message; status.ForeColor = Bad; }
    }

    // the open row for the game, which shows that spot's sphere; written on every change and once a second (a file older than
    // 3 s means the window is closed)
    void WriteOpen(bool now) {
        if (!now && openKey == lastOpenKey && (DateTime.Now - lastOpenWrite).TotalSeconds < OPEN_EVERY_S) return;
        try { file.WriteOpen(openKey); lastOpenKey = openKey; lastOpenWrite = DateTime.Now; } catch (Exception) { }
    }

    internal void Forget(string key) {
        if (file.Folder == null) return;
        var all = file.Load();
        HolsterFile.Forget(all, key);
        try { file.Save(all); } catch (Exception) { }
        RefreshFromFile();
    }

    // ---- one holster spot -------------------------------------------------------------------------------------------------
    class SpotBox : Panel {
        readonly HolstersUi ui;
        public readonly string Key, Title;
        readonly bool hasSize;
        readonly Slider fwd, up, right, size;
        bool userSize;

        public SpotBox(HolstersUi ui, string key, string title, string what, bool hasSize) {
            this.ui = ui; Key = key; Title = title; this.hasSize = hasSize;
            Dock = DockStyle.Top; Height = hasSize ? 252 : 212; BackColor = Color.Transparent; Padding = new Padding(0, 6, 0, 6);
            var inner = new SkinPanel { Dock = DockStyle.Fill, Tint = Color.FromArgb(150, Panel2), Padding = new Padding(8, 4, 8, 4) };
            Controls.Add(inner);
            if (Ticks.NameSection != null) Ticks.NameSection(inner, "holster:" + key);
            var note = new SkinLabel { Dock = DockStyle.Top, Height = 26, ForeColor = Dim, Text = what };
            Action changed = () => ui.Changed();
            fwd = new Slider(changed, "back / forward", -OFFSET_MM, OFFSET_MM, true);
            up = new Slider(changed, "down / up", -OFFSET_MM, OFFSET_MM, true);
            right = new Slider(changed, "left / right", -OFFSET_MM, OFFSET_MM, true);
            size = new Slider(changed, "size", SIZE_MIN_MM, SIZE_MAX_MM, false);
            var reset = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent, FlowDirection = FlowDirection.RightToLeft };
            reset.Controls.Add(Btn("RESET", (s, e) => ui.Forget(Key)));
            var order = new List<Control> { reset };
            if (hasSize) order.Add(size);
            order.AddRange(new Control[] { right, up, fwd, note });
            foreach (var c in order) inner.Controls.Add(c);
        }

        public void Load(Dictionary<string, object> all) {
            Func<string, double> mm = a => { double v = HolsterFile.Spot(all, Key, a); return double.IsNaN(v) ? 0 : v * 1000; };
            fwd.Set(mm("z")); up.Set(mm("y")); right.Set(mm("x"));
            double r = HolsterFile.Spot(all, Key, "r");
            userSize = !double.IsNaN(r);
            size.Set(userSize ? r * 1000 : OWN_SIZE_MM);
        }

        public void Store(Dictionary<string, object> all) {
            if (fwd.Touched || up.Touched || right.Touched) {
                HolsterFile.SetSpot(all, Key, "x", right.Value / 1000.0);
                HolsterFile.SetSpot(all, Key, "y", up.Value / 1000.0);
                HolsterFile.SetSpot(all, Key, "z", fwd.Value / 1000.0);
            }
            if (hasSize && (size.Touched || userSize)) { HolsterFile.SetSpot(all, Key, "r", size.Value / 1000.0); userSize = true; }
            fwd.Touched = up.Touched = right.Touched = size.Touched = false;
        }

        public bool Inside(LiveHolsters live) {
            var s = live == null ? null : live.Find(Key);
            return s != null && s.HandCm >= 0 && s.RadiusCm > 0 && s.HandCm <= s.RadiusCm;
        }

        public string HeadNote(LiveHolsters live) {
            var s = live == null ? null : live.Find(Key);
            if (s == null || s.HandCm < 0) return "";
            return s.RadiusCm > 0 ? string.Format("  ·  hand {0:0} cm, needs {1:0}", s.HandCm, s.RadiusCm)
                                  : string.Format("  ·  hand {0:0} cm", s.HandCm);
        }

        public void ShowLive(LiveHolsters live) { }
    }

    // a labelled slider in millimetres, shown in centimetres, with - / + buttons
    class Slider : Panel, ITickable {
        readonly Action changed;
        readonly CheckBox tick;
        public bool Ticked { get { return tick.Checked; } set { tick.Checked = value; } }
        public void Step(int presses) { Nudge(presses * NUDGE_MM); }
        readonly FancySlider bar;
        readonly Label value;
        readonly bool signed;
        bool setting;
        public bool Touched;
        public int Value { get { return bar.Value; } }

        public Slider(Action changed, string label, int min, int max, bool signed) {
            this.changed = changed; this.signed = signed;
            Dock = DockStyle.Top; Height = 40; BackColor = Color.Transparent;
            // 2026-11-14 (Tefa: "make the forward/backward, right/left headlines bigger"): bigger, brighter slider names
            var name = new SkinLabel { Text = label, Dock = DockStyle.Left, Width = 190, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 12.5f), Padding = new Padding(0, 7, 0, 0) };
            value = new SkinLabel { Dock = DockStyle.Right, Width = 82, ForeColor = Ink, TextAlign = ContentAlignment.MiddleRight };
            var minus = Btn("−", (s, e) => Nudge(-NUDGE_MM), 40); minus.Dock = DockStyle.Right;
            var plus = Btn("+", (s, e) => Nudge(NUDGE_MM), 40); plus.Dock = DockStyle.Right;
            minus.Font = plus.Font = new Font("Segoe UI Semibold", 13f);
            var gap = new Panel { Dock = DockStyle.Right, Width = 4, BackColor = Color.Transparent };
            bar = new FancySlider { Dock = DockStyle.Fill, Minimum = min, Maximum = max, SmallChange = 1, LargeChange = 10, Signed = signed };
            bar.EndsFrom(label);
            bar.ValueChanged += (s, e) => { ShowValue(); if (!setting) { Touched = true; changed(); } };
            Controls.Add(bar); Controls.Add(minus); Controls.Add(gap); Controls.Add(plus); Controls.Add(value); Controls.Add(name);
            tick = Ticks.Box(this); Controls.Add(tick); Ticks.All.Add(this);
            ShowValue();
        }

        void Nudge(int mm) { bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, bar.Value + mm)); }

        public void Set(double mm) {
            setting = true;
            bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, (int)Math.Round(mm)));
            setting = false;
            ShowValue();
        }

        void ShowValue() {
            double cm = bar.Value / 10.0;
            value.Text = (signed && cm > 0 ? "+" : "") + cm.ToString("0.0") + " cm";
        }
    }
}
