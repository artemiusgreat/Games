using Godot;
using System;

// Pre-game RiderLab (owner's ask, after the MW4/MW5 labs in his
// screenshots): drag weapons from the catalog onto the rider's
// hardpoints. Slot type and slot weight capacity rule what fits - a
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
    private Label _tabName;
    private bool _weaponsPage = true;
    private Control _pageWeapons, _pageEquipment;
    private Button _jetsTileBtn;
    private Label _jetsLine;
    private Label _speed;
    private readonly System.Collections.Generic.Dictionary<string, SlotPanel> _slots = new();
    private string _selectedWeapon; // click-to-assign path
    // Live slot anchors (owner): viewport position of each
    // hardpoint's part on the rider, projected from the 3D view
    // every frame - the paperdoll dots track the real parts at
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
    // handle and the click target - the row's text is inert.
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
            { UiSfx.Play(); Lab?.SelectWeapon(WeaponId); }
        }
    }

    public void SelectWeapon(string id)
    {
        _selectedWeapon = id;
        var w = WeaponCatalog.ById(id);
        SetHint($"{w.Name} selected - click a hardpoint to install it, or drag its icon.");
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
        var accent = w.Beam == BeamKind.Ppc ? new Color(0.45f, 0.72f, 1f)
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
    // rider-schematic positions - missile pods up at the shoulders,
    // arms out at the sides - with connector lines to a center
    // node, over the orbiting rider behind the lab. Panels are
    // positioned by slot id whenever the area resizes.
    private sealed partial class PaperdollArea : Control
    {
        private readonly System.Collections.Generic.Dictionary<string, Control> _panels = new();
        private readonly System.Collections.Generic.Dictionary<string, Vector2> _live = new();
        // Each live panel's side and stacking order come from its
        // DOT (anchor vs the body center, and the dot's height) -
        // never from the placed panel's own center (owner, round
        // 29: on a window resize a wide panel whose dot sits near
        // the body's midline had its center cross the middle, the
        // overlap sweep then grouped it with the WRONG side's
        // stack and dragged it down it - Enyo's right missile
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
            // hugs the silhouette near the part it names - pods
            // up by the shoulders, arms out at arm height, the
            // waist gun below - instead of the two ruler-straight
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
                // live panel gets the SAME height - the header
                // plus a scroll viewport of two weapon rows,
                // dropping to one row when a side's stack would
                // not otherwise fit the area. Width still pins
                // to content (unpinned Sizes drift well past
                // their minimums - 227-321px tall against
                // 133-180 minimums measured in round 28 - and
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
                int rowsVisible = avail >= 141f ? 2 : 1;
                float scrollH = rowsVisible * 48f + (rowsVisible - 1) * 4f; // a fitted row = the 40px tile + the row panel's margins
                float panelH = rowsVisible == 2 ? 141f : Mathf.Min(89f, avail);
                if (rowsVisible == 1 && panelH < 89f) scrollH = Mathf.Max(20f, panelH - 41f);
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
                        // The dot sits (almost) at the center -
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
                // is impossible at any window size - the sweeps
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
                // by side (215 + 215 in a 376px area) - the
                // radial placement already gives the geometric
                // minimum overlap, but the graze lands on the
                // panels' content. Push the pair apart by the
                // overlap, half each way; the panels may spill a
                // little past the area's sides (it does not clip
                // its children) rather than cover each other's
                // text. Wide windows never trigger this (the
                // pairs sit farther apart than their widths).
                // Final pairwise separation (owner, 2026-11-18):
                // the per-side stacks and the level-pair spread
                // leave one case open - panels on OPPOSITE sides
                // at DIFFERENT stack levels whose radial X put
                // them on a collision course mid-frame (Hector's
                // chest gun and chest energy panels, anchors a
                // few cm apart on the model, overlapping as the
                // rider orbits). A few relaxation passes over
                // every live pair, pushing along the axis of
                // least penetration, close that case without
                // disturbing the stacks when nothing collides.
                {
                    var livePanels = new System.Collections.Generic.List<Control>();
                    foreach (var (pid, p) in placed) livePanels.Add(p);
                    for (int pass = 0; pass < 8; pass++)
                    {
                        bool moved = false;
                        for (int i = 0; i < livePanels.Count; i++)
                            for (int j = i + 1; j < livePanels.Count; j++)
                            {
                                var a = livePanels[i]; var b = livePanels[j];
                                float ox = Mathf.Min(a.Position.X + a.Size.X, b.Position.X + b.Size.X)
                                         - Mathf.Max(a.Position.X, b.Position.X) + 6f;
                                if (ox <= 0f) continue;
                                float oy = Mathf.Min(a.Position.Y + a.Size.Y, b.Position.Y + b.Size.Y)
                                         - Mathf.Max(a.Position.Y, b.Position.Y) + 6f;
                                if (oy <= 0f) continue;
                                moved = true;
                                if (ox < oy)
                                {
                                    float push = ox * 0.5f;
                                    if (a.Position.X + a.Size.X * 0.5f <= b.Position.X + b.Size.X * 0.5f)
                                    {
                                        a.Position = new Vector2(Mathf.Max(a.Position.X - push, -60f), a.Position.Y);
                                        b.Position = new Vector2(Mathf.Min(b.Position.X + push, Size.X - b.Size.X + 60f), b.Position.Y);
                                    }
                                    else
                                    {
                                        a.Position = new Vector2(Mathf.Min(a.Position.X + push, Size.X - a.Size.X + 60f), a.Position.Y);
                                        b.Position = new Vector2(Mathf.Max(b.Position.X - push, -60f), b.Position.Y);
                                    }
                                }
                                else
                                {
                                    float push = oy * 0.5f;
                                    if (a.Position.Y + a.Size.Y * 0.5f <= b.Position.Y + b.Size.Y * 0.5f)
                                    {
                                        a.Position = new Vector2(a.Position.X, Mathf.Max(a.Position.Y - push, -20f));
                                        b.Position = new Vector2(b.Position.X, Mathf.Min(b.Position.Y + push, Size.Y - b.Size.Y + 20f));
                                    }
                                    else
                                    {
                                        a.Position = new Vector2(a.Position.X, Mathf.Min(a.Position.Y + push, Size.Y - a.Size.Y + 20f));
                                        b.Position = new Vector2(b.Position.X, Mathf.Max(b.Position.Y - push, -20f));
                                    }
                                }
                            }
                        if (!moved) break;
                    }
                }
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
        // Fonts (owner, 2026-11-13): the lab's texts are Open
        // Sans EXPLICITLY (its headings already take Cinzel via
        // FlowUi.Heading) - a local theme pins the default font
        // for the whole lab subtree, with the game's label and
        // button shadow colours kept from game_theme.tres.
        var labTheme = new Theme { DefaultFont = FlowUi.BodyFont };
        labTheme.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.85f));
        labTheme.SetColor("font_shadow_color", "Button", new Color(0, 0, 0, 0.85f));
        Theme = labTheme;
        // Anchors AND offsets: SetAnchorsPreset alone left this
        // code-created control at a 0x0 rect under its CanvasLayer
        // (measured live), which collapsed the backdrop ColorRect
        // to nothing while the containers overflowed visibly - the
        // lab's text sat directly on the bright scene.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = new Color(0.012f, 0.02f, 0.035f, 0.55f) }; // light dim (owner): the rider is the lab's centerpiece and must read clearly through it; panels and rows carry their own opaque backgrounds
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 6);
        root.OffsetLeft = 40; root.OffsetTop = 28; root.OffsetRight = -40; root.OffsetBottom = -24;
        AddChild(root);

        var title = new Label { Text = Spec.Name }; // title is the rider name only (owner, 2026-11-23); tonnage/class/house moved to the status stack below the weapon list
        FlowUi.Heading(title);
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", new Color(0.97f, 0.98f, 1f));
        root.AddChild(title);
        var sub = new Label { Text = "Drag a weapon's icon onto a hardpoint - or click the icon, then click a hardpoint. A hardpoint fits as many weapons of its type as its tonnage allows. Click an occupied hardpoint with nothing selected to remove its last weapon; right-click empties it. The G-button on a fitted weapon sets its firing group (keys 1-6 in the duel)." };
        sub.AddThemeColorOverride("font_color", new Color(0.82f, 0.88f, 0.94f));
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(sub);

        // The rider selector lives in the bottom bar now, just
        // before Deploy, with no 'RIDER:' label (owner,
        // 2026-11-14).

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
        // here collapsed the column entirely - measured.)
        // Sidebar (owner, 2026-11-14): the catalog column is a
        // semi-transparent panel with two tabs - Weapons (the
        // catalog below) and Equipment (jets, same row format).
        // Slightly taller (owner, 2026-11-14): the lists show
        // their first four rows in full. The minimum height
        // below is what guarantees it on a short window; the
        // root's tighter separation above pays for it.
        var sidePanel = new PanelContainer { CustomMinimumSize = new Vector2(400, 280) };
        sidePanel.SizeFlagsVertical = SizeFlags.ExpandFill; // the list adjusts to the screen height (owner, 2026-11-23)
        var sideSb = new StyleBoxFlat
        {
            BgColor = new Color(0.045f, 0.065f, 0.105f, 0.85f),
            BorderColor = new Color(0.42f, 0.48f, 0.56f, 0.8f),
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 10, ContentMarginBottom = 10
        };
        sideSb.SetBorderWidthAll(1); // 1px like Deploy (owner, 2026-11-15: all lab borders thin)
        sideSb.SetCornerRadiusAll(6);
        sidePanel.AddThemeStyleboxOverride("panel", sideSb);
        // Air above and below the sidebar (owner, 2026-11-14):
        // it no longer runs flush into the subtitle and the
        // status lines.
        var sideWrap = new MarginContainer();
        sideWrap.SizeFlagsVertical = SizeFlags.ExpandFill;
        sideWrap.AddThemeConstantOverride("margin_top", 16);
        sideWrap.AddThemeConstantOverride("margin_bottom", 16);
        mid.AddChild(sideWrap);
        sideWrap.AddChild(sidePanel);
        var catBox = new VBoxContainer();
        catBox.AddThemeConstantOverride("separation", 8);
        sidePanel.AddChild(catBox);
        // The page switch is a slider (owner, 2026-11-14):
        // the front-end's selector row cycling Weapon /
        // Equipment, the same control as the planet and
        // rider selectors. Either arrow flips the page.
        var tabSlider = FlowUi.SelectorRow(out _tabName, 14, step => ShowLabTab(!_weaponsPage));
        // The page name reads in Open Sans (owner, 2026-11-15:
        // the same-day Cinzel pass reverted at his word), and
        // the slider's arrows take the bottom bar's 1px frame.
        foreach (var ch in tabSlider.GetChildren()) if (ch is Button ab) LabBorder(ab);
        // Centred between the arrows (owner's correction,
        // 2026-11-15): the shared row only claims the middle
        // space (Expand without Fill), so the label kept its
        // text width at the slot's left edge.
        _tabName.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _tabName.HorizontalAlignment = HorizontalAlignment.Center;
        catBox.AddChild(tabSlider);
        // The catalog scrolls (owner): the full list outgrows the
        // screen height once tiles made the rows taller.
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        catBox.AddChild(scroll);
        var rowsBox = new VBoxContainer();
        rowsBox.AddThemeConstantOverride("separation", 3);
        rowsBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var rowsMargin = new MarginContainer();
        rowsMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill; // rows take the sidebar's full width, like the equipment page (owner, 2026-11-14): without the expand flag the scroll's child chain kept its content minimum and the rows stopped short
        rowsMargin.AddThemeConstantOverride("margin_right", 10); // air between the rows and the scrollbar: equal to the panel padding on the scrollbar's other side (owner, 2026-11-14)
        rowsMargin.AddChild(rowsBox);
        scroll.AddChild(rowsMargin);
        // The catalog is three stacked lists (owner): Lasers,
        // Cannons, Missiles. The group heading replaces the type
        // named in every row - which also buys back the width
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
            if (!firstGroup) rowsBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
            firstGroup = false;
            var gh = new Label { Text = gTitle };
            gh.AddThemeFontSizeOverride("font_size", 12);
            gh.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.5f));
            rowsBox.AddChild(gh);
            foreach (var w in WeaponCatalog.All)
            {
            if (w.Type != gType || !w.CatalogListed) continue;
            // Row = [draggable tile] + inert text (owner: drag and
            // click live on the square icon, not the text). Rows
            // carry no boxes and no separators (owner, 2026-11-14).
            var hb = new HBoxContainer { CustomMinimumSize = new Vector2(0, 44) };
            hb.AddThemeConstantOverride("separation", 12); // clear gap between tile and name (owner); rows compacted (owner, 2026-11-23)
            var drag = new DragTile { WeaponId = w.Id, Lab = this };
            drag.MouseEntered += () => drag.Modulate = new Color(1.3f, 1.3f, 1.3f);
            drag.MouseExited += () => drag.Modulate = Colors.White;
            var tileCtl = TileVisual(w, 34, drag);
            tileCtl.SizeFlagsVertical = SizeFlags.ShrinkCenter; // the tile fits INSIDE the row band (owner)
            hb.AddChild(tileCtl);
            var texts = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            texts.AddThemeConstantOverride("separation", 1);
            var nm = new Label { Text = w.Name, MouseFilter = MouseFilterEnum.Ignore };
            nm.AddThemeFontSizeOverride("font_size", 13);
            nm.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
            texts.AddChild(nm);
            var st = new Label { Text = StatsLine(w, full: true), MouseFilter = MouseFilterEnum.Ignore };
            st.AddThemeFontSizeOverride("font_size", 11);
            st.AddThemeColorOverride("font_color", new Color(0.62f, 0.68f, 0.75f));
            texts.AddChild(st);
            hb.AddChild(texts);
            rowsBox.AddChild(hb);
            }
        }

        _pageWeapons = scroll;
        // -- Equipment page (owner, 2026-11-14): the same row
        // format as the weapons catalog; the jets toggle lives
        // here now instead of a bottom-bar button.
        var equipBox = new VBoxContainer { Visible = false };
        equipBox.AddThemeConstantOverride("separation", 4);
        catBox.AddChild(equipBox);
        _pageEquipment = equipBox;
        // A plain row, like the weapons list (owner,
        // 2026-11-14): no box of its own.
        var jhb = new HBoxContainer { CustomMinimumSize = new Vector2(0, 44) };
        jhb.AddThemeConstantOverride("separation", 12);
        // The TH tile carries the weapon icons' framed look
        // (owner, 2026-11-14): a bordered square, not a plain
        // button face. The Button itself is faceless - the
        // frame and the 'TH' are children that ignore the
        // mouse, so the press lands on the button.
        _jetsTileBtn = new Button { CustomMinimumSize = new Vector2(34, 34) };
        _jetsTileBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _jetsTileBtn.TooltipText = "Fit or remove the thrusters";
        foreach (var bst in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            _jetsTileBtn.AddThemeStyleboxOverride(bst, new StyleBoxEmpty());
        var thFrame = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        thFrame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Thin and square, like the weapon icons' own frames
        // (owner, 2026-11-14).
        var thSb = new StyleBoxFlat { BgColor = new Color(0.10f, 0.12f, 0.16f, 0.95f), BorderColor = new Color(0.62f, 0.68f, 0.75f, 0.95f) };
        thSb.SetBorderWidthAll(1);
        thSb.SetCornerRadiusAll(0);
        thFrame.AddThemeStyleboxOverride("panel", thSb);
        var thLabel = new Label { Text = "TH", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        thLabel.AddThemeFontSizeOverride("font_size", 12);
        thLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 1f));
        thFrame.AddChild(thLabel);
        _jetsTileBtn.AddChild(thFrame);
        _jetsTileBtn.MouseEntered += () => thFrame.Modulate = new Color(1.3f, 1.3f, 1.3f);
        _jetsTileBtn.MouseExited += () => thFrame.Modulate = Colors.White;
        _jetsTileBtn.Pressed += () =>
        {
            Loadout.JumpJets = !Loadout.JumpJets;
            SetHint(Loadout.JumpJets ? "Thrusters fitted - hold Space in the duel to fly." : "Thrusters removed - this rider stays grounded.");
            Refresh();
        };
        jhb.AddChild(_jetsTileBtn);
        var jtexts = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        jtexts.AddThemeConstantOverride("separation", 1);
        var jnm = new Label { Text = "Thrusters", MouseFilter = MouseFilterEnum.Ignore };
        jnm.AddThemeFontSizeOverride("font_size", 13);
        jnm.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
        jtexts.AddChild(jnm);
        _jetsLine = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _jetsLine.AddThemeFontSizeOverride("font_size", 11);
        _jetsLine.AddThemeColorOverride("font_color", new Color(0.62f, 0.68f, 0.75f));
        jtexts.AddChild(_jetsLine);
        jhb.AddChild(jtexts);
        equipBox.AddChild(jhb);
        ShowLabTab(true);

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
                BorderColor = new Color(0.42f, 0.48f, 0.56f, 0.8f), // Deploy's steel (owner, 2026-11-15)
                ContentMarginLeft = 12, ContentMarginRight = 12,
                ContentMarginTop = 10, ContentMarginBottom = 10
            };
            sb.SetBorderWidthAll(1); // 1px like Deploy (owner, 2026-11-15)
            sb.SetCornerRadiusAll(6);
            panel.AddThemeStyleboxOverride("panel", sb);
            var vb = new VBoxContainer();
            panel.AddChild(vb);
            var head = new Label { Text = $"{slot.Type} · max {slot.MaxWeight:F0}t" }; // slot names removed (owner): type + capacity only
            panel.HeadLabel = head;
            head.AddThemeFontSizeOverride("font_size", 13); // smaller head: the panel's width is no longer forced by its title
            head.AddThemeColorOverride("font_color", new Color(0.8f, 0.86f, 0.92f));
            vb.AddChild(head);
            // The weapon list scrolls inside the panel (owner,
            // round 30): the panel's height is fixed by the
            // layout, the list shows a viewport of one or two
            // rows and scrolls for the rest - however many
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
                    UiSfx.Play();
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
                    UiSfx.Play();
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

        // -- Bottom bar (owner, 2026-11-14): Weapons and Speed
        // stacked on the left with no parenthetical; Strip all
        // is removed; Deploy sits on the right with the rider
        // selector just before it.
        var statusBox = new VBoxContainer();
        statusBox.AddThemeConstantOverride("separation", 2);
        root.AddChild(statusBox);
        // Identity under the list (owner, 2026-11-23): tonnage,
        // class and house, moved out of the title.
        var ident = new Label { Text = $"{Spec.Tonnage:F0}t  ·  {Spec.ClassLabel}  ·  HOUSE {Spec.House?.ToUpperInvariant()}" };
        ident.AddThemeFontSizeOverride("font_size", 15);
        ident.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
        statusBox.AddChild(ident);
        _tonnage = new Label();
        _tonnage.AddThemeFontSizeOverride("font_size", 15);
        _tonnage.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
        statusBox.AddChild(_tonnage);
        _speed = new Label();
        _speed.AddThemeFontSizeOverride("font_size", 15);
        _speed.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
        statusBox.AddChild(_speed);
        // No standing instruction text (owner): the line starts
        // empty - it only speaks as action feedback via SetHint.
        _hint = new Label();
        _hint.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.5f));
        root.AddChild(_hint);
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(btnRow);
        // Back to the briefing / arena screen (owner) - only
        // when the front-end flow brought the player here; a
        // standalone duel has nowhere to go back to.
        if (FlowMain.FromFlow)
        {
            var backBtn = new Button { Text = "Back" };
            backBtn.Pressed += () =>
            {
                // Back to the previous wizard step (owner, teams
                // stage): the mission team selector in Story
                // (owner, 2026-11-15: the team is formed there,
                // not in the lab); the team selector in
                // Tournament.
                FlowMain.StartScreen = WorldConfig.IsMission ? "storyteam" : "teams";
                GetTree().ChangeSceneToFile("res://flow/Flow.tscn");
            };
            btnRow.AddChild(backBtn);
            PadButton(backBtn);
            LabBorder(backBtn);
        }
        btnRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var start = new Button { Text = "Deploy" };
        start.AddThemeFontSizeOverride("font_size", 18);
        start.Pressed += () => OnStart?.Invoke();
        PadButton(start);
        LabBorder(start); // before the selector panel duplicates this face, so it inherits the border
        // The selector is the same control as Deploy (owner,
        // 2026-11-14): its panel duplicates Deploy's own
        // stylebox - same colour, border and transparency by
        // construction - with extra air on the sides, on the
        // left of the left arrow and the right of the right.
        var selPanel = new PanelContainer();
        var selSb = (StyleBox)start.GetThemeStylebox("normal").Duplicate();
        if (selSb is StyleBoxFlat selFlat)
        {
            selFlat.ContentMarginLeft += 8;
            selFlat.ContentMarginRight += 8;
        }
        selPanel.AddThemeStyleboxOverride("panel", selSb);
        selPanel.AddChild(RiderSelector("", true));
        btnRow.AddChild(selPanel);
        btnRow.AddChild(start);

        Refresh();
    }

    // Sentence case for display (owner, 2026-11-14): spec
    // names are stored all-caps ('PROMETHEUS'); everywhere
    // except the lab title they read 'Prometheus'.
    private static string NameCase(string n) => FlowUi.NameCase(n);

    // Frame for the bottom-bar buttons (owner, 2026-11-14;
    // calibrated 2026-11-15): the first pass - a light 2px
    // border - read too bright, so Back and Deploy now take
    // the sidebar's own dim steel at 1px. The selector
    // panel, which duplicates Deploy's face, inherits it.
    private static void LabBorder(Button b)
    {
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            if (b.GetThemeStylebox(state).Duplicate() is StyleBoxFlat sb)
            {
                sb.BorderColor = new Color(0.42f, 0.48f, 0.56f, 0.8f);
                sb.SetBorderWidthAll(1);
                b.AddThemeStyleboxOverride(state, sb);
            }
        }
    }

    // Dev-flag support (--lab-tab=equip): open the sidebar
    // on the Equipment page for captures.
    public void OpenOnEquipmentTab() => ShowLabTab(false);

    // Sidebar pages (owner, 2026-11-14): Weapon / Equipment,
    // switched by the slider at the sidebar's head.
    private void ShowLabTab(bool weapons)
    {
        _weaponsPage = weapons;
        if (_pageWeapons != null) _pageWeapons.Visible = weapons;
        if (_pageEquipment != null) _pageEquipment.Visible = !weapons;
        if (_tabName != null) _tabName.Text = weapons ? "Weapon" : "Equipment";
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

    // A flat bordered stylebox in the lab's idiom (the flow
    // screens' panel look, built locally so the lab stays
    // theme-self-contained).
    private static StyleBoxFlat LabPanel(Color bg, Color border, bool drawCenter = true)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            DrawCenter = drawCenter,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 5, ContentMarginBottom = 5
        };
        sb.SetBorderWidthAll(1); // 1px like Deploy (owner, 2026-11-15: all lab borders thin)
        sb.SetCornerRadiusAll(4);
        return sb;
    }

    // Slideshow selector (owner): chips don't scale to tens of
    // riders - arrows cycle through the registry, wrapping around.
    private Control RiderSelector(string role, bool playerSide)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        if (!string.IsNullOrEmpty(role))
        {
            var lab = new Label { Text = role + ":", VerticalAlignment = VerticalAlignment.Center };
            lab.AddThemeColorOverride("font_color", new Color(0.82f, 0.88f, 0.94f));
            box.AddChild(lab);
            // Wider gap between the role label and the selector
            // (owner, 2026-11-04).
            box.AddChild(new Control { CustomMinimumSize = new Vector2(22, 0) });
        }
        var name = new Label
        {
            CustomMinimumSize = new Vector2(170, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        name.AddThemeFontSizeOverride("font_size", 18); // Deploy size (owner, 2026-11-15: the selector read smaller)
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
        // Faceless arrows (owner, 2026-11-14): the selector's
        // panel carries the face now; a gold tint on hover is
        // the only feedback.
        foreach (var ab in new[] { prev, next })
        {
            foreach (var bst in new[] { "normal", "hover", "pressed", "focus" })
                ab.AddThemeStyleboxOverride(bst, new StyleBoxEmpty());
            ab.AddThemeColorOverride("font_hover_color", new Color(0.98f, 0.80f, 0.35f));
        }
        name.Text = NameCase(RiderSpecs.All[Index()].Name);
        box.AddChild(prev);
        box.AddChild(name);
        box.AddChild(next);
        return box;
    }

    private static string StatsLine(WeaponDef d, bool full) => d.Type == WeaponSlotType.Missile
        ? $"{d.Weight:F0}t · {d.Damage} dmg ×{d.Rockets} · cd {d.Cooldown:0.00}s{(full ? $" · rng {Units.Km(d.Range)}" : "")}"
        : $"{d.Weight:F0}t · dmg {d.Damage:F0} · cd {d.Cooldown:0.00}s{(full ? $" · rng {Units.Km(d.Range)}" : "")}";

    private static string Metrics(WeaponDef d) => StatsLine(d, full: false);

    // Dev measurement: panel centers vs live projected anchors,
    // in viewport pixels - the slot-flip investigation record.
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
        if (_jetsTileBtn != null)
        {
            _jetsTileBtn.Disabled = !Spec.JumpCapable;
            _jetsTileBtn.Modulate = Spec.JumpCapable ? Colors.White : new Color(1f, 1f, 1f, 0.45f);
            _jetsLine.Text = !Spec.JumpCapable ? "This chassis cannot fit thrusters"
                : Loadout.JumpJets ? "Fitted - hold Space in the duel to fly" : "Not fitted";
        }
        foreach (var (id, panel) in _slots)
        {
            var ids = Loadout.WeaponsIn(id);
            var slotDef = Spec.Slot(id);
            panel.HeadLabel.Text = $"{slotDef?.Type} · {Loadout.SlotWeight(id):F0}/{slotDef?.MaxWeight:F0}t";
            // Fitted weapons as a list (owner): each row carries
            // the weapon's icon tile with its abbreviation, the
            // name, and slash-separated metrics - the same visual
            // language as the catalog. Clicking a row removes
            // exactly that weapon.
            // Panel width need (owner: a fitted weapon is two
            // lines, never clipped mid-word at this size): the
            // rows live inside the panel's scroll, whose minimum
            // size reports ~0, so measure the texts directly -
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
                panel.NeedW = ids.Count == 0 ? 0f : 24f + 14f + 40f + 7f + textW + 7f + 38f + 4f;
            }
            foreach (var child in panel.ListBox.GetChildren()) child.QueueFree();
            if (ids.Count == 0)
            {
                var empty = new Label { Text = "- EMPTY -" };
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
                var tile = TileVisual(def, 40); // same size as the catalog tiles (owner: the fitted icons read smaller)
                tile.MouseFilter = MouseFilterEnum.Ignore;
                rhb.AddChild(tile);
                // ExpandFill is load-bearing: an HBox gives a
                // plain child only its minimum width, and the
                // metrics label's minimum is ~0 (ClipText) - the
                // texts column collapsed to the name's width and
                // the metrics clipped at ~10 characters (measured).
                var texts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                var nm = new Label { Text = def.Name };
                nm.AddThemeFontSizeOverride("font_size", 13);
                nm.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
                texts.AddChild(nm);
                // No wrap (owner): a fitted weapon is exactly two
                // lines - name, metrics. The label's full-width
                // minimum is what sizes the panel; if a panel is
                // ever capped narrower, ClipText shortens the line
                // instead of wrapping it onto extra lines.
                var mt = new Label { Text = Metrics(def), ClipText = true };
                mt.AddThemeFontSizeOverride("font_size", 11);
                mt.AddThemeColorOverride("font_color", new Color(0.62f, 0.68f, 0.75f));
                texts.AddChild(mt);
                rhb.AddChild(texts);
                // Firing-group pick (owner): the G-button cycles
                // this weapon's group 1..6; the duel fires the
                // group as one volley and the dashboard lists
                // groups, not types.
                var grpBtn = new Button
                {
                    Text = $"G{Loadout.GroupAt(sid, idx)}",
                    CustomMinimumSize = new Vector2(38, 40),
                    TooltipText = "Firing group - click to change"
                };
                grpBtn.AddThemeFontSizeOverride("font_size", 12);
                grpBtn.Pressed += () =>
                {
                    int n = Loadout.GroupAt(sid, idx);
                    Loadout.SetGroup(sid, idx, n % LoadoutState.MaxGroup + 1);
                    Refresh();
                };
                rhb.AddChild(grpBtn);
                rowPanel.GuiInput += (InputEvent e) =>
                {
                    if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    {
                        if (grpBtn.GetGlobalRect().HasPoint(mb.GlobalPosition)) return; // the G-button's click is not a removal
                        UiSfx.Play();
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
            _tonnage.Text = $"Weapons: {Loadout.TotalWeaponWeight:F0}t of {Spec.Tonnage:F0}t chassis";
            _speed.Text = $"Speed: {Units.Kmh(Spec.MaxSpeed)} walk / {Units.Kmh(Spec.MaxSpeed * 3f)} run";
    }
}
