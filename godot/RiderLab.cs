using Godot;
using System;

// Pre-game RiderLab (owner's ask, after the MW4/MW5 labs in his
// screenshots): drag weapons from the catalog onto the rider's
// hardpoints. Slot type and slot weight capacity rule what fits — a
// light rider cannot carry a heavy rider's gun. Click-to-select +
// click-to-assign works alongside drag-and-drop. The screen owns no
// game state: it edits the shared LoadoutState and reports START.
public partial class RiderLab : Control
{
    public RiderSpec Spec;
    public LoadoutState Loadout;
    public Action OnStart;

    // Rider selection (round 22): which chassis each side fields.
    // Changing one reports through OnSelectionChanged; the duel
    // rebuilds that side and recreates this lab against the new
    // player spec (slots, tonnage, jump capability all follow).
    public string PlayerSpecId = "enyo";
    public string EnemySpecId = "hector";
    public Action OnSelectionChanged;

    private Label _hint;
    private Label _tonnage;
    private Button _jetsBtn;
    private readonly System.Collections.Generic.Dictionary<string, SlotPanel> _slots = new();
    private string _selectedWeapon; // click-to-assign path
    // Live slot anchors (owner): viewport position of each
    // hardpoint's part on the rider, projected from the 3D view
    // every frame — the paperdoll dots track the real parts at
    // any window size, instead of fixed fractions that drifted
    // when the screen was resized.
    public System.Func<string, Vector2?> AnchorProjector;
    public System.Func<Vector2?> CenterProjector;
    private PaperdollArea _paperdoll;

    public override void _Process(double delta)
    {
        if (_paperdoll != null && AnchorProjector != null)
        {
            _paperdoll.Projector = AnchorProjector;
            _paperdoll.CenterProj = CenterProjector;
            _paperdoll.Layout();
        }
    }

    // The weapon TILE (owner): the small square icon is the drag
    // handle and the click target — the row's text is inert.
    private sealed partial class DragTile : Control
    {
        public string WeaponId;
        public RiderLab Lab;
        public override Variant _GetDragData(Vector2 atPosition)
        {
            var w = WeaponCatalog.ById(WeaponId);
            var preview = new PanelContainer();
            var sb = new StyleBoxFlat { BgColor = new Color(0.1f, 0.13f, 0.17f, 0.95f), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6 };
            sb.SetCornerRadiusAll(4);
            preview.AddThemeStyleboxOverride("panel", sb);
            var pl = new Label { Text = Abbrev(w) };
            pl.AddThemeFontSizeOverride("font_size", 15);
            pl.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.6f));
            preview.AddChild(pl);
            SetDragPreview(preview);
            return WeaponId;
        }
        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                Lab?.SelectWeapon(WeaponId);
        }
    }

    public void SelectWeapon(string id)
    {
        _selectedWeapon = id;
        var w = WeaponCatalog.ById(id);
        SetHint($"{w.Name} selected — click a hardpoint to install it, or drag its icon.");
    }

    // Short tile text per weapon.
    public static string Abbrev(WeaponDef w) => w.Id switch
    {
        "small_laser" => "SL",
        "medium_laser" => "ML",
        "large_laser" => "LL",
        "ppc" => "PPC",
        "machine_gun" => "MG",
        "autocannon" => "AC",
        "heavy_cannon" => "HC",
        "srm" => "SRM",
        "lrm" => "LRM",
        "hector_ac20" => "AC20",
        _ => w.Name.Length >= 2 ? w.Name.Substring(0, 2).ToUpperInvariant() : w.Name,
    };

    private static readonly System.Collections.Generic.Dictionary<string, ImageTexture> _icons = new();

    // Procedural 48x48 weapon icons: a type silhouette (bolt /
    // shell / rocket) in an accent color on a dark tile.
    public static ImageTexture Icon(WeaponDef w)
    {
        if (_icons.TryGetValue(w.Id, out var cached)) return cached;
        var accent = w.Id == "ppc" ? new Color(0.45f, 0.72f, 1f)
            : w.Type == WeaponSlotType.Energy ? new Color(1f, 0.45f, 0.32f)
            : w.Type == WeaponSlotType.Ballistic ? new Color(1f, 0.8f, 0.35f)
            : new Color(0.95f, 0.6f, 0.3f);
        var img = Image.CreateEmpty(48, 48, false, Image.Format.Rgba8);
        img.Fill(new Color(0.07f, 0.09f, 0.12f, 1f));
        var border = accent * 0.55f; border.A = 1f;
        for (int i = 0; i < 48; i++)
        {
            img.SetPixel(i, 0, border); img.SetPixel(i, 47, border);
            img.SetPixel(0, i, border); img.SetPixel(47, i, border);
        }
        void Poly(Color c, params Vector2[] pts)
        {
            // Artwork lives in the top ~70% of the tile: the
            // abbreviation strip owns the base, and silhouettes
            // drawn full-height disappeared behind it (the
            // 'cut image' in the owner's screenshots).
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new Vector2(24f + (pts[i].X - 24f) * 0.82f, 1f + pts[i].Y * 0.68f);
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                {
                    var pt = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = false;
                    for (int a = 0, b = pts.Length - 1; a < pts.Length; b = a++)
                    {
                        if ((pts[a].Y > pt.Y) != (pts[b].Y > pt.Y) &&
                            pt.X < (pts[b].X - pts[a].X) * (pt.Y - pts[a].Y) / (pts[b].Y - pts[a].Y) + pts[a].X)
                            inside = !inside;
                    }
                    if (inside) img.SetPixel(x, y, c);
                }
        }
        switch (w.Type)
        {
            case WeaponSlotType.Energy: // lightning bolt
                Poly(accent, new Vector2(28, 3), new Vector2(14, 27), new Vector2(22, 27), new Vector2(17, 45), new Vector2(34, 20), new Vector2(26, 20));
                break;
            case WeaponSlotType.Ballistic: // shell: tip + body + base band
                Poly(accent, new Vector2(19, 16), new Vector2(29, 16), new Vector2(24, 4));
                Poly(accent, new Vector2(19, 16), new Vector2(29, 16), new Vector2(29, 41), new Vector2(19, 41));
                Poly(accent * 0.55f, new Vector2(19, 35), new Vector2(29, 35), new Vector2(29, 41), new Vector2(19, 41));
                break;
            default: // Missile: rocket with fins + flame
                Poly(accent, new Vector2(24, 3), new Vector2(30, 13), new Vector2(30, 30), new Vector2(18, 30), new Vector2(18, 13));
                Poly(accent, new Vector2(18, 25), new Vector2(11, 38), new Vector2(18, 38));
                Poly(accent, new Vector2(30, 25), new Vector2(37, 38), new Vector2(30, 38));
                Poly(new Color(1f, 0.55f, 0.2f), new Vector2(21, 39), new Vector2(27, 39), new Vector2(24, 45));
                break;
        }
        // Contrast band (owner): the abbreviation sits on this
        // dark strip instead of over the bright silhouette.
        var band = new Color(0.04f, 0.05f, 0.07f, 1f);
        for (int y = 37; y < 47; y++)
            for (int x = 1; x < 47; x++)
                img.SetPixel(x, y, band);
        for (int i = 0; i < 48; i++)
        {
            img.SetPixel(i, 0, border); img.SetPixel(i, 47, border);
            img.SetPixel(0, i, border); img.SetPixel(47, i, border);
        }
        var tex = ImageTexture.CreateFromImage(img);
        _icons[w.Id] = tex;
        return tex;
    }

    // Builds the visual tile (icon + abbreviation) at a size; the
    // children ignore the mouse so the tile itself takes the input.
    private static Control TileVisual(WeaponDef w, float size, DragTile drag = null)
    {
        Control root = drag != null ? drag : new Control();
        root.CustomMinimumSize = new Vector2(size, size);
        root.SizeFlagsVertical = SizeFlags.ShrinkCenter; // else the row stretches the tile and the bottom-anchored abbreviation falls outside the icon (owner's closeup)
        // ExpandMode IgnoreSize is load-bearing: KeepSize makes
        // the TextureRect's minimum size the texture's 48px, which
        // inflates the whole tile past the row (measured live).
        var tex = new TextureRect { Texture = Icon(w), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore };
        tex.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(tex);
        // Backdrop strip behind the abbreviation (owner): a real
        // UI element, not just the baked texture band.
        var strip = new ColorRect { Color = new Color(0f, 0f, 0f, 0.62f), MouseFilter = MouseFilterEnum.Ignore };
        strip.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        strip.OffsetLeft = 2; strip.OffsetRight = -2; // inset: the strip must not cover the tile's border (owner)
        strip.OffsetTop = -(size >= 44 ? 18 : 16) - 2;
        strip.OffsetBottom = -2;
        root.AddChild(strip);
        var ab = new Label { Text = Abbrev(w), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        ab.AddThemeFontSizeOverride("font_size", size >= 44 ? 12 : 10);
        ab.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 1f));
        ab.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        ab.AddThemeConstantOverride("shadow_offset_x", 1);
        ab.AddThemeConstantOverride("shadow_offset_y", 1);
        // The label fills the STRIP's rect (not the whole tile)
        // with the text centered in it: the abbreviation gets a
        // couple of pixels of breathing room at the top and the
        // bottom (owner). Bottom-aligned in the full tile it sat
        // flush on the tile's bottom edge, below the strip.
        ab.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        ab.OffsetLeft = 2; ab.OffsetRight = -2;
        ab.OffsetTop = strip.OffsetTop; ab.OffsetBottom = -2;
        root.AddChild(ab);
        return root;
    }

    // MW4-style paperdoll (owner): the hardpoint panels float at
    // rider-schematic positions — missile pods up at the shoulders,
    // arms out at the sides — with connector lines to a center
    // node, over the orbiting rider behind the lab. Panels are
    // positioned by slot id whenever the area resizes.
    private sealed partial class PaperdollArea : Control
    {
        private readonly System.Collections.Generic.Dictionary<string, Control> _panels = new();
        private readonly System.Collections.Generic.Dictionary<string, Vector2> _live = new();
        // Each live panel's side and stacking order come from its
        // DOT (anchor vs the body center, and the dot's height) —
        // never from the placed panel's own center (owner, round
        // 29: on a window resize a wide panel whose dot sits near
        // the body's midline had its center cross the middle, the
        // overlap sweep then grouped it with the WRONG side's
        // stack and dragged it down it — Enyo's right missile
        // panel ended up flipped under the energy panel).
        private readonly System.Collections.Generic.Dictionary<string, int> _side = new();
        private readonly System.Collections.Generic.Dictionary<string, float> _anchorY = new();
        // Viewport position of a slot's part on the rider (owner):
        // set by the duel; null entries fall back to the fractions.
        public System.Func<string, Vector2?> Projector;
        public System.Func<Vector2?> CenterProj;

        public Vector2? LiveAnchor(string id) =>
            _live.TryGetValue(id, out var v) ? v + GetGlobalPosition() : null;

        public int LiveCount => _live.Count;
        public void AddPanel(string slotId, Control panel) { _panels[slotId] = panel; AddChild(panel); }
        public override void _Ready() { Resized += Layout; }
        // Fractional dot positions, used only when a slot has
        // no live projected anchor (off-screen / missing part).
        private static readonly System.Collections.Generic.Dictionary<string, Vector2> Anchors = new()
        {
            ["pod_L"] = new Vector2(0.30f, 0.27f),
            ["pod_R"] = new Vector2(0.64f, 0.27f),
            ["arm_L"] = new Vector2(0.16f, 0.47f),
            ["arm_R"] = new Vector2(0.76f, 0.45f),
            ["torso"] = new Vector2(0.47f, 0.38f),
            ["gun"] = new Vector2(0.44f, 0.55f),
        };

        public void Layout()
        {
            // Live anchors first: panel placement follows them.
            _live.Clear();
            _side.Clear();
            _anchorY.Clear();
            if (Projector != null)
                foreach (var id in _panels.Keys)
                {
                    var vp = Projector(id);
                    if (vp.HasValue) _live[id] = vp.Value - GetGlobalPosition();
                }

            // Panels WITHOUT a live anchor keep the fractional
            // layout (fallback for off-screen or missing parts).
            var centers = new System.Collections.Generic.Dictionary<string, Vector2>
            {
                ["pod_L"] = new Vector2(0.24f, 0.11f),
                ["pod_R"] = new Vector2(0.76f, 0.11f),
                ["arm_L"] = new Vector2(0.15f, 0.60f),
                ["arm_R"] = new Vector2(0.85f, 0.60f),
                ["torso"] = new Vector2(0.5f, 0.30f),
                ["gun"] = new Vector2(0.5f, 0.88f),
            };
            foreach (var (id, panel) in _panels)
            {
                if (_live.ContainsKey(id)) continue;
                if (!centers.TryGetValue(id, out var c)) c = new Vector2(0.5f, 0.5f);
                var want = c * Size - panel.Size * 0.5f;
                panel.Position = new Vector2(
                    Mathf.Clamp(want.X, 0f, Mathf.Max(0f, Size.X - panel.Size.X)),
                    Mathf.Clamp(want.Y, 0f, Mathf.Max(0f, Size.Y - panel.Size.Y)));
            }

            // Panels WITH a live anchor sit ALONG THE BODY
            // (owner, 2026-10-31): each panel is placed outward
            // from the rider's center through its own dot, so it
            // hugs the silhouette near the part it names — pods
            // up by the shoulders, arms out at arm height, the
            // waist gun below — instead of the two ruler-straight
            // columns the flip fix used. The direction comes from
            // the measured anchors themselves (the torso anchor
            // is the center when present, else the centroid), so
            // a panel always lands on its dot's own side; a short
            // relaxation pass then separates any overlaps
            // vertically. Lines stay short and uncrossed.
            if (_live.Count > 0)
            {
                // The body's center first: panel sides and the
                // stacking order are read off the dots relative
                // to it (round 29), and the fixed panel height
                // below needs the side counts.
                Vector2 center;
                var bc = CenterProj?.Invoke();
                if (bc.HasValue) center = bc.Value - GetGlobalPosition();
                else if (_live.TryGetValue("torso", out var tc)) center = tc;
                else
                {
                    center = Vector2.Zero;
                    foreach (var v in _live.Values) center += v;
                    center /= _live.Count;
                }
                foreach (var (id, panel) in _panels)
                    if (_live.TryGetValue(id, out var an0))
                    {
                        _side[id] = an0.X >= center.X ? 1 : 0;
                        _anchorY[id] = an0.Y;
                    }
                // Fixed panel height (owner, round 30): every
                // live panel gets the SAME height — the header
                // plus a scroll viewport of two weapon rows,
                // dropping to one row when a side's stack would
                // not otherwise fit the area. Width still pins
                // to content (unpinned Sizes drift well past
                // their minimums — 227-321px tall against
                // 133-180 minimums measured in round 28 — and
                // the packing math goes phantom). Layout runs
                // every frame, so the pin holds.
                int maxSide = 0;
                {
                    int l = 0, r = 0;
                    foreach (var (id, _) in _panels)
                        if (_live.ContainsKey(id))
                            if ((_side.TryGetValue(id, out var sd) ? sd : 0) == 0) l++; else r++;
                    maxSide = Mathf.Max(1, Mathf.Max(l, r));
                }
                float avail = Mathf.Max(60f, (Size.Y - (maxSide - 1) * 8f) / maxSide);
                int rowsVisible = avail >= 133f ? 2 : 1;
                float scrollH = rowsVisible * 44f + (rowsVisible - 1) * 4f;
                float panelH = rowsVisible == 2 ? 133f : Mathf.Min(85f, avail);
                if (rowsVisible == 1 && panelH < 85f) scrollH = Mathf.Max(20f, panelH - 41f);
                foreach (var (id, panel) in _panels)
                    if (_live.ContainsKey(id))
                    {
                        float capW = Mathf.Max(200f, Size.X * 0.5f - 6f);
                        float needW = panel.GetCombinedMinimumSize().X;
                        if (panel is SlotPanel spn) needW = Mathf.Max(needW, spn.NeedW);
                        panel.Size = new Vector2(Mathf.Min(needW, capW), panelH);
                        if (panel is SlotPanel sp && sp.Scroll != null)
                            sp.Scroll.CustomMinimumSize = new Vector2(0f, scrollH);
                    }
                foreach (var (id, panel) in _panels)
                {
                    if (!_live.TryGetValue(id, out var an)) continue;
                    var dir = an - center;
                    if (dir.Length() < 14f)
                    {
                        // The dot sits (almost) at the center —
                        // the chest: push it out along its
                        // fractional fallback direction instead.
                        dir = Anchors.TryGetValue(id, out var fr)
                            ? fr - new Vector2(0.47f, 0.42f)
                            : new Vector2(0.3f, -0.2f);
                        if (dir.LengthSquared() < 0.001f) dir = new Vector2(0.3f, -0.2f);
                    }
                    dir = dir.Normalized();
                    float tx = Mathf.Abs(dir.X) > 0.02f
                        ? (panel.Size.X * 0.5f + 14f) / Mathf.Abs(dir.X) : float.MaxValue;
                    float ty = Mathf.Abs(dir.Y) > 0.02f
                        ? (panel.Size.Y * 0.5f + 14f) / Mathf.Abs(dir.Y) : float.MaxValue;
                    var c = an + dir * Mathf.Min(tx, ty);
                    c = new Vector2(
                        Mathf.Clamp(c.X, panel.Size.X * 0.5f, Mathf.Max(panel.Size.X * 0.5f, Size.X - panel.Size.X * 0.5f)),
                        Mathf.Clamp(c.Y, panel.Size.Y * 0.5f, Mathf.Max(panel.Size.Y * 0.5f, Size.Y - panel.Size.Y * 0.5f)));
                    panel.Position = c - panel.Size * 0.5f;
                }
                // Stacking (round 30): per dot-side, in
                // dot-height order, the panels form one
                // contiguous stack with an 8px gap, anchored at
                // the first dot's height and clamped to fit the
                // area. Heights are fixed (above) and the
                // stack's total fits by construction, so overlap
                // is impossible at any window size — the sweeps
                // this replaces could only separate panels when
                // their content heights happened to fit, and
                // Hector's five tall panels did not (owner's
                // screenshot: the missile panel drawn over the
                // energy panel's third row). X stays wherever
                // the radial placement put it: panels keep
                // hugging their own side of the silhouette;
                // this step owns only Y.
                var placed = new System.Collections.Generic.List<(string Id, Control Panel)>();
                foreach (var (id, panel) in _panels)
                    if (_live.ContainsKey(id)) placed.Add((id, panel));
                var yOf = new System.Collections.Generic.Dictionary<Control, float>();
                foreach (var (pid, p) in placed)
                    yOf[p] = _anchorY.TryGetValue(pid, out var ay) ? ay : p.Position.Y + p.Size.Y * 0.5f;
                var groups = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<(string Id, Control Panel)>>();
                foreach (var side in new[] { 0, 1 })
                {
                    var group = new System.Collections.Generic.List<(string Id, Control Panel)>();
                    foreach (var (pid, p) in placed)
                        if ((_side.TryGetValue(pid, out var sd) ? sd : (p.Position.X + p.Size.X * 0.5f < Size.X * 0.5f ? 0 : 1)) == side)
                            group.Add((pid, p));
                    group.Sort((a, b) => yOf[a.Panel].CompareTo(yOf[b.Panel]));
                    groups[side] = group;
                    if (group.Count == 0) continue;
                    float total = group.Count * panelH + (group.Count - 1) * 8f;
                    float startY = Mathf.Clamp(yOf[group[0].Panel] - panelH * 0.5f, 0f, Mathf.Max(0f, Size.Y - total));
                    for (int i = 0; i < group.Count; i++)
                    {
                        var pp = group[i].Panel;
                        pp.Position = new Vector2(pp.Position.X, startY + i * (panelH + 8f));
                    }
                }
                // Level-pair spread: at the same stack level the
                // two sides' panels can still overlap in X when
                // the area is narrower than the two panels side
                // by side (215 + 215 in a 376px area) — the
                // radial placement already gives the geometric
                // minimum overlap, but the graze lands on the
                // panels' content. Push the pair apart by the
                // overlap, half each way; the panels may spill a
                // little past the area's sides (it does not clip
                // its children) rather than cover each other's
                // text. Wide windows never trigger this (the
                // pairs sit farther apart than their widths).
                if (groups.TryGetValue(0, out var gL) && groups.TryGetValue(1, out var gR))
                {
                    int levels = Mathf.Min(gL.Count, gR.Count);
                    for (int i = 0; i < levels; i++)
                    {
                        var a = gL[i].Panel; var b = gR[i].Panel;
                        float aC = a.Position.X + a.Size.X * 0.5f;
                        float bC = b.Position.X + b.Size.X * 0.5f;
                        if (bC < aC) (a, b) = (b, a);
                        float need = (a.Size.X + b.Size.X) * 0.5f + 12f; // + a clear 12px gap: merely touching reads as overlapping
                        float dist = (b.Position.X + b.Size.X * 0.5f) - (a.Position.X + a.Size.X * 0.5f);
                        if (dist >= need) continue;
                        float push = (need - dist) * 0.5f;
                        a.Position = new Vector2(Mathf.Max(a.Position.X - push, -60f), a.Position.Y);
                        b.Position = new Vector2(Mathf.Min(b.Position.X + push, Size.X - b.Size.X + 60f), b.Position.Y);
                    }
                }
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (Size.X < 10f) return;
            var line = new Color(0.5f, 0.58f, 0.66f, 0.5f);
            var dot = new Color(0.95f, 0.8f, 0.45f, 0.9f);
            foreach (var (id, panel) in _panels)
            {
                Vector2 anchor;
                if (_live.TryGetValue(id, out var lv)) anchor = lv;
                else if (Anchors.TryGetValue(id, out var af)) anchor = af * Size;
                else continue;
                // The line runs from the panel's EDGE (not its
                // center, not a shared hub) to the slot's own
                // place on the rider (owner).
                var rect = new Rect2(panel.Position, panel.Size);
                var c = rect.GetCenter();
                var dir = anchor - c;
                float t = 1f;
                if (Mathf.Abs(dir.X) > 0.001f) t = Mathf.Min(t, (rect.Size.X * 0.5f) / Mathf.Abs(dir.X));
                if (Mathf.Abs(dir.Y) > 0.001f) t = Mathf.Min(t, (rect.Size.Y * 0.5f) / Mathf.Abs(dir.Y));
                DrawLine(c + dir * t, anchor, line, 2f);
                DrawCircle(anchor, 3.5f, dot);
            }
        }
    }

    private sealed partial class SlotPanel : PanelContainer
    {
        public string SlotId;
        public RiderLab Lab;
        public Label HeadLabel;
        public VBoxContainer ListBox;
        public ScrollContainer Scroll;
        public Control Tile;
        public float NeedW; // content width the fitted rows need at full length (set by Refresh; the inner scroll hides it from GetCombinedMinimumSize)
        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            var wid = data.AsString();
            var w = WeaponCatalog.ById(wid);
            var err = Lab.Loadout.Check(Lab.Spec, SlotId, w);
            Lab.SetHint(err ?? $"{w.Name} fits the {Lab.Spec.Slot(SlotId).Display}.");
            return err == null;
        }
        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var w = WeaponCatalog.ById(data.AsString());
            var err = Lab.Loadout.TryAssign(Lab.Spec, SlotId, w);
            Lab.SetHint(err ?? $"{w.Name} installed in the {Lab.Spec.Slot(SlotId).Display}.");
            Lab.Refresh();
        }
    }

    public override void _Ready()
    {
        // Anchors AND offsets: SetAnchorsPreset alone left this
        // code-created control at a 0x0 rect under its CanvasLayer
        // (measured live), which collapsed the backdrop ColorRect
        // to nothing while the containers overflowed visibly — the
        // lab's text sat directly on the bright scene.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = new Color(0.012f, 0.02f, 0.035f, 0.55f) }; // light dim (owner): the rider is the lab's centerpiece and must read clearly through it; panels and rows carry their own opaque backgrounds
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 10);
        root.OffsetLeft = 40; root.OffsetTop = 28; root.OffsetRight = -40; root.OffsetBottom = -24;
        AddChild(root);

        var title = new Label { Text = $"RIDER LAB — {Spec.Name}  ·  {Spec.Tonnage:F0}t CHASSIS  ·  {Spec.ClassLabel}" };
        FlowUi.Heading(title);
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", new Color(0.97f, 0.98f, 1f));
        root.AddChild(title);
        var sub = new Label { Text = "Drag a weapon's icon onto a hardpoint — or click the icon, then click a hardpoint. A hardpoint fits as many weapons of its type as its tonnage allows. Click an occupied hardpoint with nothing selected to remove its last weapon; right-click empties it." };
        sub.AddThemeColorOverride("font_color", new Color(0.82f, 0.88f, 0.94f));
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(sub);

        // -- Rider selection row --
        var selBox = new VBoxContainer();
        selBox.AddThemeConstantOverride("separation", 6);
        root.AddChild(selBox);
        // One selector only (owner, 2026-10-31): yours. The two
        // opponents are fixed — a Enyo and an Hector field
        // against you (and each other) on every map.
        selBox.AddChild(RiderSelector("RIDER", true));

        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 28);
        mid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(mid);

        // -- Catalog (left) --
        // The catalog column is FIXED (400 since the rows went
        // two-line): left to content it creeps, the rows stretch
        // to fill it, and the paperdoll's panels spill back over
        // the rows' backdrops. Anything wider clips in the scroll
        // instead of widening the column. (A CustomMaximumSize
        // here collapsed the column entirely — measured.)
        var catBox = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        catBox.AddThemeConstantOverride("separation", 4);
        mid.AddChild(catBox);
        var catTitle = new Label { Text = "WEAPONS" };
        catTitle.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.5f));
        FlowUi.Heading(catTitle, false);
        catBox.AddChild(catTitle);
        // The catalog scrolls (owner): the full list outgrows the
        // screen height once tiles made the rows taller.
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        catBox.AddChild(scroll);
        var rowsBox = new VBoxContainer();
        rowsBox.AddThemeConstantOverride("separation", 4);
        rowsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var rowsMargin = new MarginContainer();
        rowsMargin.AddThemeConstantOverride("margin_right", 14); // air between the rows and the scrollbar (owner)
        rowsMargin.AddChild(rowsBox);
        scroll.AddChild(rowsMargin);
        // The catalog is three stacked lists (owner): Lasers,
        // Cannons, Missiles. The group heading replaces the type
        // named in every row — which also buys back the width
        // the mono font took: the widest row shrinks by the
        // type segment, the paperdoll gets the room, and the
        // slot backdrops stop spilling over the catalog's.
        var catalogGroups = new (string Title, WeaponSlotType Type)[]
        {
            ("Lasers", WeaponSlotType.Energy),
            ("Cannons", WeaponSlotType.Ballistic),
            ("Missiles", WeaponSlotType.Missile),
        };
        bool firstGroup = true;
        foreach (var (gTitle, gType) in catalogGroups)
        {
            if (!firstGroup) rowsBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
            firstGroup = false;
            var gh = new Label { Text = gTitle };
            gh.AddThemeFontSizeOverride("font_size", 13);
            gh.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.5f));
            rowsBox.AddChild(gh);
            foreach (var w in WeaponCatalog.All)
            {
            if (w.Type != gType) continue;
            // Row = [draggable tile] + inert text (owner: drag and
            // click live on the square icon, not the text).
            var rowPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 52), ClipContents = true }; // row sized to fit the tile with air around it
            StyleBoxFlat RowBox()
            {
                // Same backdrop as the slot panels (owner): one
                // visual family across the lab — same fill, same
                // border, same radius.
                var b = new StyleBoxFlat { BgColor = new Color(0.06f, 0.085f, 0.12f, 0.72f), BorderColor = new Color(0.5f, 0.56f, 0.62f, 0.8f), ContentMarginLeft = 12f, ContentMarginRight = 10f, ContentMarginTop = 6f, ContentMarginBottom = 6f };
                b.SetBorderWidthAll(2);
                b.SetCornerRadiusAll(6);
                return b;
            }
            rowPanel.AddThemeStyleboxOverride("panel", RowBox());
            var hb = new HBoxContainer();
            hb.AddThemeConstantOverride("separation", 16); // clear gap between tile and name (owner)
            rowPanel.AddChild(hb);
            var drag = new DragTile { WeaponId = w.Id, Lab = this };
            drag.MouseEntered += () => drag.Modulate = new Color(1.3f, 1.3f, 1.3f);
            drag.MouseExited += () => drag.Modulate = Colors.White;
            var tileCtl = TileVisual(w, 40, drag);
            tileCtl.SizeFlagsVertical = SizeFlags.ShrinkCenter; // the tile fits INSIDE the row band (owner)
            hb.AddChild(tileCtl);
            var texts = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            texts.AddThemeConstantOverride("separation", 1);
            var nm = new Label { Text = w.Name, MouseFilter = MouseFilterEnum.Ignore };
            nm.AddThemeFontSizeOverride("font_size", 14);
            nm.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
            texts.AddChild(nm);
            var st = new Label { Text = StatsLine(w, full: true), MouseFilter = MouseFilterEnum.Ignore };
            st.AddThemeFontSizeOverride("font_size", 11);
            st.AddThemeColorOverride("font_color", new Color(0.62f, 0.68f, 0.75f));
            texts.AddChild(st);
            hb.AddChild(texts);
            rowsBox.AddChild(rowPanel);
            }
        }

        // -- Hardpoints (right): MW4-style paperdoll over the rider --
        var paperdoll = new PaperdollArea();
        _paperdoll = paperdoll;
        paperdoll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        paperdoll.SizeFlagsVertical = SizeFlags.ExpandFill;
        mid.AddChild(paperdoll);
        foreach (var slot in Spec.Slots)
        {
            var panel = new SlotPanel { SlotId = slot.Id, Lab = this, CustomMinimumSize = new Vector2(215, 64) };
            var sb = new StyleBoxFlat
            {
                BgColor = new Color(0.06f, 0.085f, 0.12f, 0.72f), // translucent (owner): the rider shows through the slot backdrops
                BorderColor = new Color(0.5f, 0.56f, 0.62f, 0.8f),
                ContentMarginLeft = 12, ContentMarginRight = 12,
                ContentMarginTop = 10, ContentMarginBottom = 10
            };
            sb.SetBorderWidthAll(2);
            sb.SetCornerRadiusAll(6);
            panel.AddThemeStyleboxOverride("panel", sb);
            var vb = new VBoxContainer();
            panel.AddChild(vb);
            var head = new Label { Text = $"{slot.Type} · max {slot.MaxWeight:F0}t" }; // slot names removed (owner): type + capacity only
            panel.HeadLabel = head;
            FlowUi.Heading(head, false);
            head.AddThemeFontSizeOverride("font_size", 13); // smaller head: the panel's width is no longer forced by its title
            head.AddThemeColorOverride("font_color", new Color(0.8f, 0.86f, 0.92f));
            vb.AddChild(head);
            // The weapon list scrolls inside the panel (owner,
            // round 30): the panel's height is fixed by the
            // layout, the list shows a viewport of one or two
            // rows and scrolls for the rest — however many
            // weapons the slot carries, the panel never grows
            // into its neighbors.
            panel.Scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            vb.AddChild(panel.Scroll);
            panel.ListBox = new VBoxContainer();
            panel.ListBox.AddThemeConstantOverride("separation", 4);
            panel.ListBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            panel.Scroll.AddChild(panel.ListBox);
            var capturedSlot = slot;
            panel.GuiInput += (InputEvent e) =>
            {
                if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && _selectedWeapon != null)
                {
                    var w = WeaponCatalog.ById(_selectedWeapon);
                    var err = Loadout.TryAssign(Spec, capturedSlot.Id, w);
                    SetHint(err ?? $"{w.Name} installed in the {capturedSlot.Display}.");
                    Refresh();
                }
                else if (e is InputEventMouseButton mb3 && mb3.Pressed && mb3.ButtonIndex == MouseButton.Left
                         && _selectedWeapon == null && Loadout.WeaponIn(capturedSlot.Id) != null)
                {
                    // Peel ONE weapon by click: left-click an occupied
                    // hardpoint with no weapon selected removes the
                    // last one fitted (right-click empties the slot).
                    if (Loadout.RemoveLast(capturedSlot.Id, out var rid))
                        SetHint($"{WeaponCatalog.ById(rid)?.Name} removed from the {capturedSlot.Display}.");
                    Refresh();
                }
                else if (e is InputEventMouseButton mb2 && mb2.Pressed && mb2.ButtonIndex == MouseButton.Right)
                {
                    Loadout.Clear(capturedSlot.Id);
                    SetHint($"{capturedSlot.Display} emptied.");
                    Refresh();
                }
            };
            _slots[slot.Id] = panel;
            paperdoll.AddPanel(slot.Id, panel);
        }
        paperdoll.Layout();

        // -- Bottom bar --
        _tonnage = new Label();
        _tonnage.AddThemeFontSizeOverride("font_size", 16);
        _tonnage.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
        root.AddChild(_tonnage);
        _hint = new Label { Text = "Right-click a hardpoint to empty it." };
        _hint.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.5f));
        root.AddChild(_hint);
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(btnRow);
        var strip = new Button { Text = "STRIP ALL" };
        strip.Pressed += () => { Loadout.Strip(); SetHint("All hardpoints emptied."); Refresh(); };
        btnRow.AddChild(strip);
        _jetsBtn = new Button();
        _jetsBtn.Pressed += () =>
        {
            Loadout.JumpJets = !Loadout.JumpJets;
            SetHint(Loadout.JumpJets ? "Jump jets fitted — hold SPACE in the duel to fly." : "Jump jets removed — this rider stays grounded.");
            Refresh();
        };
        btnRow.AddChild(_jetsBtn);
        if (!Spec.JumpCapable) _jetsBtn.Visible = false; // this chassis cannot mount thrusters
        var start = new Button { Text = "DEPLOY  ▶" };
        start.AddThemeFontSizeOverride("font_size", 18);
        start.Pressed += () => OnStart?.Invoke();
        btnRow.AddChild(start);
        PadButton(strip); PadButton(_jetsBtn); PadButton(start);

        Refresh();
    }

    // Roomier inner padding for the bottom action chips (owner,
    // 2026-11-04): duplicate the theme styleboxes so only the content
    // margins grow - colors, corners and borders stay the theme's.
    private static void PadButton(Button b)
    {
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            if (b.GetThemeStylebox(state).Duplicate() is StyleBox sb)
            {
                sb.ContentMarginLeft = 18; sb.ContentMarginRight = 18;
                sb.ContentMarginTop = 11; sb.ContentMarginBottom = 11;
                b.AddThemeStyleboxOverride(state, sb);
            }
        }
    }

    // Slideshow selector (owner): chips don't scale to tens of
    // riders — arrows cycle through the registry, wrapping around.
    private Control RiderSelector(string role, bool playerSide)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var lab = new Label { Text = role + ":", VerticalAlignment = VerticalAlignment.Center };
        lab.AddThemeColorOverride("font_color", new Color(0.82f, 0.88f, 0.94f));
        box.AddChild(lab);
        // Wider gap between the role label and the selector
        // (owner, 2026-11-04).
        box.AddChild(new Control { CustomMinimumSize = new Vector2(22, 0) });
        var name = new Label
        {
            CustomMinimumSize = new Vector2(170, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        name.AddThemeFontSizeOverride("font_size", 16);
        name.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 1f));
        int Index()
        {
            var id = playerSide ? PlayerSpecId : EnemySpecId;
            for (int i = 0; i < RiderSpecs.All.Length; i++) if (RiderSpecs.All[i].Id == id) return i;
            return 0;
        }
        void Cycle(int d)
        {
            var all = RiderSpecs.All;
            int i = (Index() + d + all.Length) % all.Length;
            if (playerSide) PlayerSpecId = all[i].Id; else EnemySpecId = all[i].Id;
            OnSelectionChanged?.Invoke();
        }
        var prev = new Button { Text = "◀" };
        prev.Pressed += () => Cycle(-1);
        var next = new Button { Text = "▶" };
        next.Pressed += () => Cycle(1);
        name.Text = RiderSpecs.All[Index()].Name;
        box.AddChild(prev);
        box.AddChild(name);
        box.AddChild(next);
        return box;
    }

    private static string StatsLine(WeaponDef d, bool full) => d.Type == WeaponSlotType.Missile
        ? $"{d.Weight:F0}t · {d.Damage} dmg ×{d.Rockets} · cd {d.Cooldown:0.00}s{(full ? $" · rng {d.Range:F0}u" : "")}"
        : $"{d.Weight:F0}t · dmg {d.Damage:F0} · cd {d.Cooldown:0.00}s{(full ? $" · rng {d.Range:F0}u" : "")}";

    private static string Metrics(WeaponDef d) => StatsLine(d, full: false);

    // Dev measurement: panel centers vs live projected anchors,
    // in viewport pixels — the slot-flip investigation record.
    public string DumpLayout()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"LABLAYOUT live={_paperdoll?.LiveCount} areaPos={_paperdoll?.GetGlobalPosition()} areaSize={_paperdoll?.Size}");
        foreach (var (id, panel) in _slots)
        {
            var c = panel.GetGlobalRect().GetCenter();
            var a = _paperdoll?.LiveAnchor(id);
            sb.AppendLine($"LABLAYOUT {id} panel=({c.X:F0},{c.Y:F0}) size=({panel.Size.X:F0},{panel.Size.Y:F0}) anchor={(a.HasValue ? $"({a.Value.X:F0},{a.Value.Y:F0})" : "none")}");
        }
        return sb.ToString();
    }

    public void SetHint(string text)
    {
        if (_hint != null) _hint.Text = text;
    }

    public void Refresh()
    {
        if (_jetsBtn != null)
        {
            _jetsBtn.Text = Spec.JumpCapable
                ? $"JUMP JETS: {(Loadout.JumpJets ? "FITTED" : "NONE")}"
                : "JUMP JETS: N/A — CHASSIS";
            _jetsBtn.Disabled = !Spec.JumpCapable;
        }
        foreach (var (id, panel) in _slots)
        {
            var ids = Loadout.WeaponsIn(id);
            var slotDef = Spec.Slot(id);
            panel.HeadLabel.Text = $"{slotDef?.Type} · {Loadout.SlotWeight(id):F0}/{slotDef?.MaxWeight:F0}t";
            // Fitted weapons as a list (owner): each row carries
            // the weapon's icon tile with its abbreviation, the
            // name, and slash-separated metrics — the same visual
            // language as the catalog. Clicking a row removes
            // exactly that weapon.
            // Panel width need (owner: a fitted weapon is two
            // lines, never clipped mid-word at this size): the
            // rows live inside the panel's scroll, whose minimum
            // size reports ~0, so measure the texts directly —
            // panel margins + scrollbar + tile + gap + the
            // longest name/metrics line.
            {
                var font = GetThemeDefaultFont();
                float textW = 0f;
                foreach (var wid in ids)
                {
                    var d = WeaponCatalog.ById(wid);
                    if (d == null) continue;
                    textW = Mathf.Max(textW, font.GetStringSize(d.Name, HorizontalAlignment.Left, -1f, 13).X);
                    textW = Mathf.Max(textW, font.GetStringSize(Metrics(d), HorizontalAlignment.Left, -1f, 11).X);
                }
                panel.NeedW = ids.Count == 0 ? 0f : 24f + 14f + 36f + 7f + textW + 4f;
            }
            foreach (var child in panel.ListBox.GetChildren()) child.QueueFree();
            if (ids.Count == 0)
            {
                var empty = new Label { Text = "— EMPTY —" };
                empty.AddThemeFontSizeOverride("font_size", 12);
                empty.AddThemeColorOverride("font_color", new Color(0.55f, 0.5f, 0.45f));
                panel.ListBox.AddChild(empty);
            }
            for (int i = 0; i < ids.Count; i++)
            {
                var def = WeaponCatalog.ById(ids[i]);
                if (def == null) continue;
                int idx = i; string sid = id;
                var rowPanel = new PanelContainer();
                var rsb = new StyleBoxFlat { BgColor = new Color(0.10f, 0.12f, 0.15f, 0.55f) };
                rsb.SetCornerRadiusAll(4);
                rsb.ContentMarginLeft = 5; rsb.ContentMarginRight = 5;
                rsb.ContentMarginTop = 4; rsb.ContentMarginBottom = 4;
                rowPanel.AddThemeStyleboxOverride("panel", rsb);
                var rhb = new HBoxContainer();
                rhb.AddThemeConstantOverride("separation", 7);
                rowPanel.AddChild(rhb);
                var tile = TileVisual(def, 36);
                tile.MouseFilter = MouseFilterEnum.Ignore;
                rhb.AddChild(tile);
                // ExpandFill is load-bearing: an HBox gives a
                // plain child only its minimum width, and the
                // metrics label's minimum is ~0 (ClipText) — the
                // texts column collapsed to the name's width and
                // the metrics clipped at ~10 characters (measured).
                var texts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                var nm = new Label { Text = def.Name };
                nm.AddThemeFontSizeOverride("font_size", 13);
                nm.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
                texts.AddChild(nm);
                // No wrap (owner): a fitted weapon is exactly two
                // lines — name, metrics. The label's full-width
                // minimum is what sizes the panel; if a panel is
                // ever capped narrower, ClipText shortens the line
                // instead of wrapping it onto extra lines.
                var mt = new Label { Text = Metrics(def), ClipText = true };
                mt.AddThemeFontSizeOverride("font_size", 11);
                mt.AddThemeColorOverride("font_color", new Color(0.62f, 0.68f, 0.75f));
                texts.AddChild(mt);
                rhb.AddChild(texts);
                rowPanel.GuiInput += (InputEvent e) =>
                {
                    if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    {
                        if (Loadout.RemoveAt(sid, idx, out var rid))
                            SetHint($"{WeaponCatalog.ById(rid)?.Name} removed from the {Spec.Slot(sid)?.Display}.");
                        Refresh();
                    }
                };
                panel.ListBox.AddChild(rowPanel);
            }
        }
        _paperdoll?.Layout();
        if (_tonnage != null)
            _tonnage.Text = $"WEAPONS: {Loadout.TotalWeaponWeight:F0}t of {Spec.Tonnage:F0}t chassis   ·   SPEED {Spec.MaxSpeed:F1} u/s walk / {Spec.MaxSpeed * 3f:F1} run ({Spec.ClassLabel} x{Spec.ClassSpeedFactor:0.00})";
    }
}
