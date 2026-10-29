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

    private sealed partial class WeaponRow : Button
    {
        public string WeaponId;
        public MechLab Lab;
        public override Variant _GetDragData(Vector2 atPosition)
        {
            var preview = new Label { Text = Text };
            preview.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.6f));
            SetDragPreview(preview);
            return WeaponId;
        }
    }

    private sealed partial class SlotPanel : PanelContainer
    {
        public string SlotId;
        public MechLab Lab;
        public Label WeaponLabel;
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
        var bg = new ColorRect { Color = new Color(0.012f, 0.02f, 0.035f, 1f) }; // fully opaque: text contrast cannot depend on the scene behind
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
        var sub = new Label { Text = "Drag a weapon onto a hardpoint — or click a weapon, then click a hardpoint. Type and tonnage must fit. Click an occupied hardpoint with nothing selected to strip just that slot." };
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
        foreach (var w in WeaponCatalog.All)
        {
            var row = new WeaponRow
            {
                WeaponId = w.Id,
                Lab = this,
                Text = $"{w.Name}   ·   {w.Weight:F0}t   ·   {w.Type}   ·   dmg {w.Damage:F0}{(w.Rockets > 0 ? $"/rocket · {w.Rockets}/pod" : "")}   ·   cd {w.Cooldown:F2}s",
                Alignment = HorizontalAlignment.Left
            };
            row.AddThemeFontSizeOverride("font_size", 14);
            row.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
            row.AddThemeColorOverride("font_hover_color", new Color(1f, 0.98f, 0.9f));
            // Inner padding around the weapon text (owner).
            StyleBoxFlat RowBox(Color c)
            {
                var b = new StyleBoxFlat { BgColor = c, ContentMarginLeft = 10f, ContentMarginRight = 10f, ContentMarginTop = 5f, ContentMarginBottom = 5f };
                b.SetCornerRadiusAll(4);
                return b;
            }
            row.AddThemeStyleboxOverride("normal", RowBox(new Color(0.09f, 0.12f, 0.16f, 1f)));
            row.AddThemeStyleboxOverride("hover", RowBox(new Color(0.16f, 0.21f, 0.28f, 1f)));
            row.AddThemeStyleboxOverride("pressed", RowBox(new Color(0.22f, 0.28f, 0.36f, 1f)));
            var captured = w;
            row.Pressed += () =>
            {
                _selectedWeapon = captured.Id;
                SetHint($"{captured.Name} selected — click a hardpoint to install it.");
            };
            catBox.AddChild(row);
        }

        // -- Hardpoints (right): paperdoll-ish 2x2 --
        var slotGrid = new GridContainer { Columns = 2 };
        slotGrid.AddThemeConstantOverride("h_separation", 16);
        slotGrid.AddThemeConstantOverride("v_separation", 16);
        slotGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        mid.AddChild(slotGrid);
        foreach (var slot in Spec.Slots)
        {
            var panel = new SlotPanel { SlotId = slot.Id, Lab = this, CustomMinimumSize = new Vector2(250, 108) };
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
            head.AddThemeColorOverride("font_color", new Color(0.8f, 0.86f, 0.92f));
            vb.AddChild(head);
            panel.WeaponLabel = new Label();
            panel.WeaponLabel.AddThemeFontSizeOverride("font_size", 17);
            vb.AddChild(panel.WeaponLabel);
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
            slotGrid.AddChild(panel);
        }

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
            panel.WeaponLabel.Text = w != null ? $"{w.Name} — {w.Weight:F0}t" : "— EMPTY —";
            panel.WeaponLabel.AddThemeColorOverride("font_color",
                w != null ? new Color(0.9f, 0.93f, 0.95f) : new Color(0.55f, 0.5f, 0.45f));
        }
        if (_tonnage != null)
            _tonnage.Text = $"WEAPONS: {Loadout.TotalWeaponWeight:F0}t of {Spec.Tonnage:F0}t chassis";
    }
}
