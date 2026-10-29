using Godot;
using System;

// Pre-game MechLab (owner's ask, after the MW4/MW5 labs in his
// screenshots): drag weapons from the catalog onto the mech's
// hardpoints. Slot type and slot weight capacity rule what fits — a
// light mech cannot carry a heavy mech's gun. Click-to-select +
// click-to-assign works alongside drag-and-drop. The screen owns no
// game state: it edits the shared LoadoutState and reports START.
public partial class MechLab : Control
{
    public MechSpec Spec;
    public LoadoutState Loadout;
    public Action OnStart;

    private Label _hint;
    private Label _tonnage;
    private Button _jetsBtn;
    private readonly System.Collections.Generic.Dictionary<string, SlotPanel> _slots = new();
    private string _selectedWeapon; // click-to-assign path

    // The weapon TILE (owner): the small square icon is the drag
    // handle and the click target — the row's text is inert.
    private sealed partial class DragTile : Control
    {
        public string WeaponId;
        public MechLab Lab;
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
        "atlas_ac20" => "AC20",
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
        strip.OffsetTop = -(size >= 44 ? 16 : 14) - 2;
        strip.OffsetBottom = -2;
        root.AddChild(strip);
        var ab = new Label { Text = Abbrev(w), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore };
        ab.AddThemeFontSizeOverride("font_size", size >= 44 ? 12 : 10);
        ab.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 1f));
        ab.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        ab.AddThemeConstantOverride("shadow_offset_x", 1);
        ab.AddThemeConstantOverride("shadow_offset_y", 1);
        ab.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(ab);
        return root;
    }

    // MW4-style paperdoll (owner): the hardpoint panels float at
    // mech-schematic positions — missile pods up at the shoulders,
    // arms out at the sides — with connector lines to a center
    // node, over the orbiting mech behind the lab. Panels are
    // positioned by slot id whenever the area resizes.
    private sealed partial class PaperdollArea : Control
    {
        private readonly System.Collections.Generic.Dictionary<string, Control> _panels = new();
        public void AddPanel(string slotId, Control panel) { _panels[slotId] = panel; AddChild(panel); }
        public override void _Ready() { Resized += Layout; }
        public void Layout()
        {
            // Panel centers as fractions of the area, by slot id.
            var centers = new System.Collections.Generic.Dictionary<string, Vector2>
            {
                ["pod_L"] = new Vector2(0.24f, 0.11f),
                ["pod_R"] = new Vector2(0.76f, 0.11f),
                ["arm_L"] = new Vector2(0.15f, 0.60f),
                ["arm_R"] = new Vector2(0.85f, 0.60f),
            };
            foreach (var (id, panel) in _panels)
            {
                if (!centers.TryGetValue(id, out var c)) c = new Vector2(0.5f, 0.5f);
                var want = c * Size - panel.Size * 0.5f;
                panel.Position = new Vector2(
                    Mathf.Clamp(want.X, 0f, Mathf.Max(0f, Size.X - panel.Size.X)),
                    Mathf.Clamp(want.Y, 0f, Mathf.Max(0f, Size.Y - panel.Size.Y)));
            }
            QueueRedraw();
        }
        // Where each hardpoint lives on the machine, as fractions
        // of the paperdoll area (tuned to the showroom framing).
        private static readonly System.Collections.Generic.Dictionary<string, Vector2> Anchors = new()
        {
            ["pod_L"] = new Vector2(0.30f, 0.27f),
            ["pod_R"] = new Vector2(0.64f, 0.27f),
            ["arm_L"] = new Vector2(0.16f, 0.47f),
            ["arm_R"] = new Vector2(0.76f, 0.45f),
        };

        public override void _Draw()
        {
            if (Size.X < 10f) return;
            var line = new Color(0.5f, 0.58f, 0.66f, 0.5f);
            var dot = new Color(0.95f, 0.8f, 0.45f, 0.9f);
            foreach (var (id, panel) in _panels)
            {
                if (!Anchors.TryGetValue(id, out var af)) continue;
                var anchor = af * Size;
                // The line runs from the panel's EDGE (not its
                // center, not a shared hub) to the slot's own
                // place on the mech (owner).
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
        public MechLab Lab;
        public Label WeaponLabel;
        public Control Tile;
        public TextureRect TileTex;
        public Label TileAbbrev;
        public Label WeightLabel;
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
        var bg = new ColorRect { Color = new Color(0.012f, 0.02f, 0.035f, 0.55f) }; // light dim (owner): the mech is the lab's centerpiece and must read clearly through it; panels and rows carry their own opaque backgrounds
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 10);
        root.OffsetLeft = 40; root.OffsetTop = 28; root.OffsetRight = -40; root.OffsetBottom = -24;
        AddChild(root);

        var title = new Label { Text = $"MECH LAB — {Spec.Name}  ·  {Spec.Tonnage:F0}t CHASSIS" };
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", new Color(0.97f, 0.98f, 1f));
        root.AddChild(title);
        var sub = new Label { Text = "Drag a weapon's icon onto a hardpoint — or click the icon, then click a hardpoint. Type and tonnage must fit. Click an occupied hardpoint with nothing selected to strip just that slot." };
        sub.AddThemeColorOverride("font_color", new Color(0.82f, 0.88f, 0.94f));
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(sub);

        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 28);
        mid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(mid);

        // -- Catalog (left) --
        var catBox = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        catBox.AddThemeConstantOverride("separation", 4);
        mid.AddChild(catBox);
        var catTitle = new Label { Text = "WEAPONS" };
        catTitle.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.5f));
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
        scroll.AddChild(rowsBox);
        foreach (var w in WeaponCatalog.All)
        {
            // Row = [draggable tile] + inert text (owner: drag and
            // click live on the square icon, not the text).
            var rowPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 52), ClipContents = true }; // row sized to fit the tile with air around it
            StyleBoxFlat RowBox(Color c)
            {
                var b = new StyleBoxFlat { BgColor = c, ContentMarginLeft = 12f, ContentMarginRight = 10f, ContentMarginTop = 6f, ContentMarginBottom = 6f };
                b.SetCornerRadiusAll(4);
                return b;
            }
            rowPanel.AddThemeStyleboxOverride("panel", RowBox(new Color(0.09f, 0.12f, 0.16f, 1f)));
            var hb = new HBoxContainer();
            hb.AddThemeConstantOverride("separation", 16); // clear gap between tile and name (owner)
            rowPanel.AddChild(hb);
            var drag = new DragTile { WeaponId = w.Id, Lab = this };
            drag.MouseEntered += () => drag.Modulate = new Color(1.3f, 1.3f, 1.3f);
            drag.MouseExited += () => drag.Modulate = Colors.White;
            var tileCtl = TileVisual(w, 40, drag);
            tileCtl.SizeFlagsVertical = SizeFlags.ShrinkCenter; // the tile fits INSIDE the row band (owner)
            hb.AddChild(tileCtl);
            var lab = new Label
            {
                Text = $"{w.Name}   ·   {w.Weight:F0}t   ·   {w.Type}   ·   dmg {w.Damage:F0}{(w.Rockets > 0 ? $"/rocket · {w.Rockets}/pod" : "")}   ·   cd {w.Cooldown:F2}s",
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore
            };
            lab.AddThemeFontSizeOverride("font_size", 14);
            lab.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
            hb.AddChild(lab);
            rowsBox.AddChild(rowPanel);
        }

        // -- Hardpoints (right): MW4-style paperdoll over the mech --
        var paperdoll = new PaperdollArea();
        paperdoll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        paperdoll.SizeFlagsVertical = SizeFlags.ExpandFill;
        mid.AddChild(paperdoll);
        foreach (var slot in Spec.Slots)
        {
            var panel = new SlotPanel { SlotId = slot.Id, Lab = this, CustomMinimumSize = new Vector2(190, 92) };
            var sb = new StyleBoxFlat
            {
                BgColor = new Color(0.06f, 0.085f, 0.12f, 0.92f),
                BorderColor = new Color(0.5f, 0.56f, 0.62f, 0.8f),
                ContentMarginLeft = 12, ContentMarginRight = 12,
                ContentMarginTop = 10, ContentMarginBottom = 10
            };
            sb.SetBorderWidthAll(2);
            sb.SetCornerRadiusAll(6);
            panel.AddThemeStyleboxOverride("panel", sb);
            var vb = new VBoxContainer();
            panel.AddChild(vb);
            var head = new Label { Text = $"{slot.Display} — {slot.Type} · max {slot.MaxWeight:F0}t" };
            head.AddThemeFontSizeOverride("font_size", 13); // smaller head: the panel's width is no longer forced by its title
            head.AddThemeColorOverride("font_color", new Color(0.8f, 0.86f, 0.92f));
            vb.AddChild(head);
            var tileRow = new HBoxContainer();
            tileRow.AddThemeConstantOverride("separation", 10);
            vb.AddChild(tileRow);
            panel.Tile = new Control { CustomMinimumSize = new Vector2(40, 40), MouseFilter = MouseFilterEnum.Ignore }; // input falls through to the panel: right-click on the tile still strips the slot
            panel.TileTex = new TextureRect { StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore };
            panel.TileTex.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            panel.Tile.AddChild(panel.TileTex);
            var slotStrip = new ColorRect { Color = new Color(0f, 0f, 0f, 0.62f), MouseFilter = MouseFilterEnum.Ignore };
            slotStrip.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
            slotStrip.OffsetLeft = 2; slotStrip.OffsetRight = -2;
            slotStrip.OffsetTop = -18;
            slotStrip.OffsetBottom = -2;
            panel.Tile.AddChild(slotStrip);
            panel.TileAbbrev = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore };
            panel.TileAbbrev.AddThemeFontSizeOverride("font_size", 12);
            panel.TileAbbrev.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 1f));
            panel.TileAbbrev.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
            panel.TileAbbrev.AddThemeConstantOverride("shadow_offset_x", 1);
            panel.TileAbbrev.AddThemeConstantOverride("shadow_offset_y", 1);
            panel.TileAbbrev.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            panel.Tile.AddChild(panel.TileAbbrev);
            tileRow.AddChild(panel.Tile);
            panel.WeightLabel = new Label { VerticalAlignment = VerticalAlignment.Center };
            panel.WeightLabel.AddThemeFontSizeOverride("font_size", 15);
            panel.WeightLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.8f, 0.86f));
            tileRow.AddChild(panel.WeightLabel);
            panel.WeaponLabel = new Label { VerticalAlignment = VerticalAlignment.Center };
            panel.WeaponLabel.AddThemeFontSizeOverride("font_size", 15);
            tileRow.AddChild(panel.WeaponLabel);
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
                    // Strip ONE slot by click (owner): left-click an
                    // occupied hardpoint with no weapon selected.
                    Loadout.Clear(capturedSlot.Id);
                    SetHint($"{capturedSlot.Display} emptied.");
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
            SetHint(Loadout.JumpJets ? "Jump jets fitted — hold SPACE in the duel to fly." : "Jump jets removed — this mech stays grounded.");
            Refresh();
        };
        btnRow.AddChild(_jetsBtn);
        var start = new Button { Text = "DEPLOY  ▶" };
        start.AddThemeFontSizeOverride("font_size", 18);
        start.Pressed += () => OnStart?.Invoke();
        btnRow.AddChild(start);

        Refresh();
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
            var wid = Loadout.WeaponIn(id);
            var w = wid != null ? WeaponCatalog.ById(wid) : null;
            // Occupied slots show the square icon + abbreviation
            // (owner), not the weapon's full name.
            panel.Tile.Visible = w != null;
            if (w != null)
            {
                panel.TileTex.Texture = Icon(w);
                panel.TileAbbrev.Text = Abbrev(w);
            }
            panel.WeightLabel.Text = w != null ? $"{w.Weight:F0}t" : "";
            panel.WeaponLabel.Text = w == null ? "— EMPTY —" : "";
            panel.WeaponLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.5f, 0.45f));
        }
        if (_tonnage != null)
            _tonnage.Text = $"WEAPONS: {Loadout.TotalWeaponWeight:F0}t of {Spec.Tonnage:F0}t chassis";
    }
}
