using Godot;

// Front-end step 1: choose the game mode. Layout after the
// owner's menu concept (2026-11-13), restyled the same day at
// his word as a command-deck CONTROL PANEL: engraved serif
// display fonts (Cinzel) for the headings and action bars,
// a clean sans (Open Sans) for subtitles and body, chamfered
// "rhombus-cut" panels instead of rounded corners, a thin
// instrument frame with corner brackets around the screen,
// and small mono telemetry readouts in the bottom corners.
// No rewards block, no bottom status bar, no top-right text.
// The whole card is the button, as before.
public partial class MapTypeScreen : Control
{
    public System.Action OnTournament;
    public System.Action OnMission;

    // Layout refs for the responsive fit (owner, 2026-11-13:
    // the cards must fit the visible area at his window size,
    // not just at 1080p).
    private Button _cardT, _cardM;
    private TextureRect _artT, _artM, _titleRect;
    private Label _descT, _descM;

    private static FontFile _cinzelBold, _cinzel, _sans, _sansSemi;
    private static FontFile CinzelBold => _cinzelBold ??= GD.Load<FontFile>("res://assets/fonts/Cinzel-Bold.ttf");
    private static FontFile Cinzel => _cinzel ??= GD.Load<FontFile>("res://assets/fonts/Cinzel-Regular.ttf");
    private static FontFile Sans => _sans ??= GD.Load<FontFile>("res://assets/fonts/OpenSans-Regular.ttf");
    private static FontFile SansSemi => _sansSemi ??= GD.Load<FontFile>("res://assets/fonts/OpenSans-SemiBold.ttf");

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 44);
        margin.AddThemeConstantOverride("margin_right", 44);
        margin.AddThemeConstantOverride("margin_top", 30);
        margin.AddThemeConstantOverride("margin_bottom", 34);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        // Top bar: the owner's title art only (the concept's
        // top-right tagline is removed at his word).
        var top = new HBoxContainer();
        root.AddChild(top);
        _titleRect = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/flow/title_card.png"),
            CustomMinimumSize = new Vector2(300f, 96f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin
        };
        top.AddChild(_titleRect);

        // The two mode cards, centred as a pair.
        var centre = new CenterContainer();
        centre.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(centre);
        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 30);
        centre.AddChild(cards);
        cards.AddChild(Card(
            "01", "TOURNAMENT", "SELECT YOUR CHAMPION",
            "res://assets/flow/card_tournament.jpg",
            "Arena duel on a compact battlefield. Three riders enter — one leaves. " +
            "The classic test of machine and pilot, fought around the mesa under tournament rules.",
            "ENTER TOURNAMENT", featured: true, icon: CardIcon.Kind.Compass,
            go: () => OnTournament?.Invoke()));
        cards.AddChild(Card(
            "02", "STORY", "TAKE ON A MISSION",
            "res://assets/flow/card_mission.jpg",
            "Planetary operations on a full-scale front. Pick a world in orbit, take on " +
            "a mission, then deploy from your base and march on the enemy.",
            "VIEW MISSIONS", featured: false, icon: CardIcon.Kind.Target,
            go: () => OnMission?.Invoke()));

        // Bottom telemetry row: dashboard readouts, not a bar.
        var bottom = new HBoxContainer();
        root.AddChild(bottom);
        var teleL = FlowUi.MakeLabel(Spread("COMMAND DECK // ORBITAL UPLINK STABLE"), 11, FlowUi.Dim);
        bottom.AddChild(teleL);
        var teleR = FlowUi.MakeLabel(Spread("AWAITING ORDERS"), 11, FlowUi.Dim, HorizontalAlignment.Right);
        teleR.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bottom.AddChild(teleR);

        // The instrument frame over everything (drawn, inert).
        var frame = new DeckFrame { MouseFilter = MouseFilterEnum.Ignore };
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(frame);

        Layout();
        GetTree().Root.SizeChanged += Layout;
    }

    // Fit the cards to the visible area: their width follows
    // the window (two cards + margins + the gap), their height
    // what remains under the title and telemetry rows. The ART
    // height is what's left of the card after the fixed
    // interior (card margins 48, separations 70, header 56,
    // rule 2, action bar 50) and the description's wrapped
    // height — the first cut scaled the art by a ratio of the
    // card height and the interior overflowed at short
    // windows, pushing the action bar past the card's bottom
    // edge over the telemetry line (owner's screenshot,
    // 2026-11-13). At 1080p the budget lands on the designed
    // 600x620 with the full 246px art.
    private void Layout()
    {
        var vp = GetViewportRect().Size;
        if (vp.X < 200f || _cardT == null) return;
        float cardW = Mathf.Clamp((vp.X - 88f - 30f) / 2f, 320f, 600f);
        float th = Mathf.Clamp(vp.Y * 0.115f, 60f, 96f);
        // Card height from the EQUAL-GAPS rule (owner): the
        // space under the action bar inside the card (its
        // bottom padding, 26) must equal the space under the
        // card down to the telemetry row. The cards are
        // vertically centred between the title and telemetry,
        // so: centre zone = vp - root margins (64) - title -
        // separations (20) - telemetry (~16); the card leaves
        // 2 x (26 - 10 separation) of that zone below itself.
        // At the 620 design cap (tall windows) the rule is
        // unreachable and the gap simply grows.
        float cardH = Mathf.Clamp(vp.Y - th - 132f, 360f, 620f);
        var cs = new Vector2(cardW, cardH);
        _cardT.CustomMinimumSize = cs;
        _cardM.CustomMinimumSize = cs;
        float descH = cardW >= 470f ? 62f : 84f; // wrapped lines at the card's text width
        float artH = Mathf.Clamp(cardH - 48f - 178f - descH, 84f, 246f);
        _artT.CustomMinimumSize = new Vector2(0f, artH);
        _artM.CustomMinimumSize = new Vector2(0f, artH);
        var dsz = new Vector2(cardW - 52f, 0f);
        _descT.CustomMinimumSize = dsz;
        _descM.CustomMinimumSize = dsz;
        _titleRect.CustomMinimumSize = new Vector2(th * (300f / 96f), th);
    }

    // Letterspaced caps for the small panel labels: the sans
    // has no tracking control here, so the spaces are literal.
    private static string Spread(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            if (ch == ' ') sb.Append("   ");
            else { sb.Append(ch); sb.Append(' '); }
        }
        return sb.ToString().TrimEnd();
    }

    private static Label Serif(string text, int size, Color color, FontFile font,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private Control Card(string index, string title, string sub, string artPath, string desc,
        string action, bool featured, CardIcon.Kind icon, System.Action go)
    {
        var card = new Button { CustomMinimumSize = new Vector2(600f, 620f) };
        if (featured) _cardT = card; else _cardM = card;
        var empty = new StyleBoxEmpty();
        card.AddThemeStyleboxOverride("normal", empty);
        card.AddThemeStyleboxOverride("hover", empty);
        card.AddThemeStyleboxOverride("pressed", empty);
        card.AddThemeStyleboxOverride("focus", empty);
        card.Pressed += () => go();

        // The chamfered shell draws the card's face and frame;
        // its palette follows the button's state.
        var shell = new CutPanel
        {
            Cut = 18f,
            BorderWidth = featured ? 2.5f : 2f,
            FillNormal = FlowUi.Navy,
            FillHover = FlowUi.NavySoft,
            FillPressed = new Color(0.23f, 0.19f, 0.08f, 0.98f),
            BorderNormal = featured ? FlowUi.Gold : FlowUi.Steel,
            BorderHover = FlowUi.Gold,
            OutlineColor = FlowUi.Gold
        };
        shell.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        card.AddChild(shell);
        card.MouseEntered += () => { shell.Hover = true; shell.QueueRedraw(); };
        card.MouseExited += () => { shell.Hover = false; shell.QueueRedraw(); };
        card.ButtonDown += () => { shell.Pressed = true; shell.QueueRedraw(); };
        card.ButtonUp += () => { shell.Pressed = false; shell.QueueRedraw(); };
        card.FocusEntered += () => { shell.Focused = true; shell.QueueRedraw(); };
        card.FocusExited += () => { shell.Focused = false; shell.QueueRedraw(); };

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 26);
        margin.AddThemeConstantOverride("margin_right", 26);
        margin.AddThemeConstantOverride("margin_top", 22);
        margin.AddThemeConstantOverride("margin_bottom", 26); // = the gap under the card (owner: inside/outside spacing equal)
        card.AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 14);
        margin.AddChild(v);

        // Header: line icon + title / subtitle stack + the
        // panel's index number, over a gold rule.
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 16);
        v.AddChild(head);
        head.AddChild(new CardIcon
        {
            IconKind = icon,
            Tint = featured ? FlowUi.Gold : FlowUi.Dim,
            CustomMinimumSize = new Vector2(56f, 56f)
        });
        var titles = new VBoxContainer();
        titles.AddThemeConstantOverride("separation", 3);
        titles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(titles);
        titles.AddChild(Serif(title, 34, featured ? FlowUi.Gold : FlowUi.Ink, CinzelBold));
        var subL = Serif(Spread(sub), 12, featured ? FlowUi.GoldDim : FlowUi.Dim, SansSemi);
        titles.AddChild(subL);
        var idx = Serif(index, 22, featured ? FlowUi.GoldDim : FlowUi.Dim, Cinzel);
        idx.VerticalAlignment = VerticalAlignment.Top;
        head.AddChild(idx);
        var rule = new ColorRect
        {
            Color = new Color(FlowUi.GoldDim, 0.55f),
            CustomMinimumSize = new Vector2(0f, 2f)
        };
        v.AddChild(rule);

        // Illustration, hard-edged like the panel cuts.
        var art = new TextureRect
        {
            Texture = GD.Load<Texture2D>(artPath),
            CustomMinimumSize = new Vector2(0f, 246f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            SizeFlagsVertical = SizeFlags.ExpandFill // absorbs leftover height with the spacer
        };
        if (featured) _artT = art; else _artM = art;
        v.AddChild(art);

        var d = Serif(desc, 15, FlowUi.Ink, Sans);
        if (featured) _descT = d; else _descM = d;
        d.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        d.CustomMinimumSize = new Vector2(548f, 72f);
        v.AddChild(d);

        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        v.AddChild(spacer);

        // Action bar: a chamfered plate — solid gold on the
        // featured card, dark with a steel frame on the other.
        var barWrap = new Control { CustomMinimumSize = new Vector2(0f, 50f) };
        v.AddChild(barWrap);
        var bar = new CutPanel
        {
            Cut = 20f,
            BorderWidth = 2f,
            FillNormal = featured ? new Color(0.83f, 0.64f, 0.22f, 1f)
                                 : new Color(0.08f, 0.11f, 0.16f, 0.95f),
            FillHover = featured ? new Color(0.88f, 0.69f, 0.26f, 1f)
                                 : new Color(0.12f, 0.16f, 0.23f, 0.98f),
            FillPressed = new Color(0.70f, 0.53f, 0.18f, 1f),
            BorderNormal = featured ? new Color(0.98f, 0.82f, 0.42f, 1f) : FlowUi.Steel,
            BorderHover = FlowUi.Gold
        };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        barWrap.AddChild(bar);
        // The bar's face follows the CARD's hover/press, so it
        // lights with the card, not on its own bounds.
        card.MouseEntered += () => { bar.Hover = true; bar.QueueRedraw(); };
        card.MouseExited += () => { bar.Hover = false; bar.QueueRedraw(); };
        card.ButtonDown += () => { bar.Pressed = true; bar.QueueRedraw(); };
        card.ButtonUp += () => { bar.Pressed = false; bar.QueueRedraw(); };
        var act = Serif(action, 19, featured ? new Color(0.09f, 0.07f, 0.02f) : FlowUi.Ink,
            CinzelBold, HorizontalAlignment.Center);
        act.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        act.VerticalAlignment = VerticalAlignment.Center;
        barWrap.AddChild(act);

        // Labels and layout containers must not eat the click:
        // the whole subtree passes input through to the button.
        SetIgnoreMouse(card);
        return card;
    }

    private static void SetIgnoreMouse(Control c)
    {
        foreach (var child in c.GetChildren())
            if (child is Control cc)
            {
                cc.MouseFilter = MouseFilterEnum.Ignore;
                SetIgnoreMouse(cc);
            }
    }
}

// A chamfered ("rhombus-cut") panel face: a rectangle with
// its four corners cut at 45 degrees, drawn as a filled
// polygon with a stroked frame. State colours are set by the
// owning button. When focused, a second frame is drawn a few
// pixels outside the first — the keyboard focus indicator.
public partial class CutPanel : Control
{
    public float Cut = 16f;
    public float BorderWidth = 2f;
    public Color FillNormal = new(0, 0, 0, 0);
    public Color FillHover = new(0, 0, 0, 0);
    public Color FillPressed = new(0, 0, 0, 0);
    public Color BorderNormal = new(1, 1, 1, 0.5f);
    public Color BorderHover = new(1, 1, 1, 0.8f);
    public Color OutlineColor = new(1, 1, 1, 0);
    public bool Hover, Pressed, Focused;

    private Vector2[] Points(float inset)
    {
        float x = inset, y = inset;
        float w = Size.X - inset * 2f, h = Size.Y - inset * 2f;
        float c = Mathf.Max(2f, Cut + inset);
        return new[]
        {
            new Vector2(x + c, y), new Vector2(x + w - c, y),
            new Vector2(x + w, y + c), new Vector2(x + w, y + h - c),
            new Vector2(x + w - c, y + h), new Vector2(x + c, y + h),
            new Vector2(x, y + h - c), new Vector2(x, y + c)
        };
    }

    private static Vector2[] Loop(Vector2[] pts)
    {
        var loop = new Vector2[pts.Length + 1];
        pts.CopyTo(loop, 0);
        loop[pts.Length] = pts[0];
        return loop;
    }

    public override void _Draw()
    {
        var fill = Pressed ? FillPressed : Hover ? FillHover : FillNormal;
        var border = Hover || Focused ? BorderHover : BorderNormal;
        var pts = Points(0f);
        if (fill.A > 0.001f) DrawPolygon(pts, new[] { fill });
        DrawPolyline(Loop(pts), border, BorderWidth, true);
        if (Focused && OutlineColor.A > 0.001f)
            DrawPolyline(Loop(Points(-5f)), OutlineColor, 2.5f, true);
    }
}

// The screen's instrument frame: a hairline border inset
// from the window edge with gold corner brackets — the
// control-panel bezel the cards sit inside.
public partial class DeckFrame : Control
{
    public override void _Draw()
    {
        float inset = 14f;
        var steel = new Color(FlowUi.Steel, 0.35f);
        var gold = new Color(FlowUi.Gold, 0.85f);
        var tl = new Vector2(inset, inset);
        var tr = new Vector2(Size.X - inset, inset);
        var bl = new Vector2(inset, Size.Y - inset);
        var br = new Vector2(Size.X - inset, Size.Y - inset);
        DrawLine(tl, tr, steel, 1.5f, true);
        DrawLine(bl, br, steel, 1.5f, true);
        DrawLine(tl, bl, steel, 1.5f, true);
        DrawLine(tr, br, steel, 1.5f, true);
        float arm = 30f;
        // Corner brackets.
        DrawLine(tl, tl + new Vector2(arm, 0), gold, 3f, true);
        DrawLine(tl, tl + new Vector2(0, arm), gold, 3f, true);
        DrawLine(tr, tr + new Vector2(-arm, 0), gold, 3f, true);
        DrawLine(tr, tr + new Vector2(0, arm), gold, 3f, true);
        DrawLine(bl, bl + new Vector2(arm, 0), gold, 3f, true);
        DrawLine(bl, bl + new Vector2(0, -arm), gold, 3f, true);
        DrawLine(br, br + new Vector2(-arm, 0), gold, 3f, true);
        DrawLine(br, br + new Vector2(0, -arm), gold, 3f, true);
    }
}

// The cards' line icons, drawn as vector strokes (no glyph
// fonts): a compass rose for Tournament, a targeting reticle
// for Mission — the concept's two marks.
public partial class CardIcon : Control
{
    public enum Kind { Compass, Target }
    public Kind IconKind;
    public Color Tint = Colors.White;

    public override void _Draw()
    {
        var c = Size * 0.5f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.46f;
        if (IconKind == Kind.Compass)
        {
            DrawArc(c, r, 0f, Mathf.Tau, 48, Tint, 2.5f, true);
            // Four long points, four short diagonals.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.Pi * 0.5f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                DrawLine(c - dir * r * 1.02f, c + dir * r * 1.02f, Tint, 2.5f, true);
                float b = a + Mathf.Pi * 0.25f;
                var d2 = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                DrawLine(c, c + d2 * r * 0.55f, Tint, 2f, true);
            }
            DrawCircle(c, 3.5f, Tint);
        }
        else
        {
            DrawArc(c, r * 0.78f, 0f, Mathf.Tau, 48, Tint, 2.5f, true);
            DrawArc(c, r * 0.30f, 0f, Mathf.Tau, 32, Tint, 2f, true);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.Pi * 0.5f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                DrawLine(c + dir * r * 0.52f, c + dir * r * 1.05f, Tint, 2.5f, true);
            }
            DrawCircle(c, 3f, Tint);
        }
    }
}
