using Godot;

// Front-end step 1: choose the game mode. Layout after the
// owner's menu concept (2026-11-13), restyled the same day at
// his word as a command-deck CONTROL PANEL: engraved serif
// display fonts (Cinzel) for the headings and action bars,
// a clean sans (Open Sans) for subtitles and body, rounded
// panel corners (the chamfered "rhombus-cut" corners were
// retired at the owner's word, 2026-11-23), and
// small mono telemetry readouts in the bottom corners.
// No rewards block, no bottom status bar, no top-right text.
// The whole card is the button, as before. (The screen
// frame with gold corner brackets and the cards' second
// focus-outline frame were removed at the owner's word,
// 2026-11-14: no screen border, one border per card.)
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

    // Card layout (the Layout() fit works from these same
    // values, so the designed card and the fitted card agree).
    private const int PageMarginHorizontal = 44;
    private const int PageMarginTop = 30;
    private const int PageMarginBottom = 34;
    private const int CardGap = 30;
    private const float CardMinWidth = 320f;
    private const float CardMaxWidth = 600f;
    private const float CardMinHeight = 380f;
    private const float CardMaxHeight = 620f;
    private const float CardVerticalAllowance = 225f;
    private const float CardMarginAllowance = 62f;
    private const float CardInteriorAllowance = 182f;
    private const float DescBreakpointWidth = 470f;
    private const float DescWideHeight = 62f;
    private const float DescNarrowHeight = 84f;
    private const float DescMinHeight = 72f;
    private const float CardTextInset = 52f;
    private const float ArtMinHeight = 72f;
    private const float ArtMaxHeight = 246f;
    private const float TitleBaseWidth = 300f;
    private const float TitleBaseHeight = 96f;
    private const float TitleMinHeight = 60f;
    private const float TitleMaxHeight = 96f;
    private const float TitleHeightFraction = 0.115f;
    private const float TitleAspect = TitleBaseWidth / TitleBaseHeight;
    private const int CardMarginHorizontal = 26;
    private const int CardMarginTop = 22;
    private const int CardMarginBottom = 32;
    private const int CardColumnSeparation = 14;
    private const int CardHeaderSeparation = 16;
    private const float CardIconSize = 56f;
    private const float MinLayoutViewportWidth = 200f; // below this the card layout does not run
    private const float CardRuleAlpha = 0.55f;         // the gold rule under a card's titles
    private const int CardTitleSeparation = 3;         // title/subtitle stack spacing
    private const int CardTitleFontSize = 34;
    private const int CardSubtitleFontSize = 12;
    private const int CardDescFontSize = 15;
    private const int CardActionFontSize = 19;
    private const float CardSpacerHeight = 4f;
    private const float ActionBarHeight = 50f;
    private const float CardCornerRadius = 18f;
    private const float ActionBarCornerRadius = 20f;
    private const float CardBorderWidth = 2f;
    private const float FeaturedCardBorderWidth = 2.5f;
    private const float RuleHeight = 2f;

    // Action-bar palette (the featured card's bar is solid
    // gold; the other card's is dark with a steel frame).
    private static readonly Color BarFeaturedNormal = new(0.83f, 0.64f, 0.22f, 1f);
    private static readonly Color BarFeaturedHover = new(0.88f, 0.69f, 0.26f, 1f);
    private static readonly Color BarPlainNormal = new(0.08f, 0.11f, 0.16f, 0.95f);
    private static readonly Color BarPlainHover = new(0.12f, 0.16f, 0.23f, 0.98f);
    private static readonly Color BarPressed = new(0.70f, 0.53f, 0.18f, 1f);
    private static readonly Color BarFeaturedBorder = new(0.98f, 0.82f, 0.42f, 1f);
    private static readonly Color BarFeaturedText = new(0.09f, 0.07f, 0.02f);

    private static FontFile _cinzelBold, _cinzel, _sans, _sansSemi;
    private static FontFile CinzelBold => _cinzelBold ??= GD.Load<FontFile>(Paths.CinzelBold);
    private static FontFile Cinzel => _cinzel ??= GD.Load<FontFile>(Paths.CinzelRegular);
    private static FontFile Sans => _sans ??= GD.Load<FontFile>(Paths.OpenSansRegular);
    private static FontFile SansSemi => _sansSemi ??= GD.Load<FontFile>(Paths.OpenSansSemiBold);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", PageMarginHorizontal);
        margin.AddThemeConstantOverride("margin_right", PageMarginHorizontal);
        margin.AddThemeConstantOverride("margin_top", PageMarginTop);
        margin.AddThemeConstantOverride("margin_bottom", PageMarginBottom);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", FlowUi.ScreenSeparation);
        margin.AddChild(root);

        // Top bar: the owner's title art only (the concept's
        // top-right tagline is removed at his word).
        var top = new HBoxContainer();
        root.AddChild(top);
        _titleRect = new TextureRect
        {
            Texture = GD.Load<Texture2D>(Paths.TitleCard),
            CustomMinimumSize = new Vector2(TitleBaseWidth, TitleBaseHeight),
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
        cards.AddThemeConstantOverride("separation", CardGap);
        centre.AddChild(cards);
        cards.AddChild(Card(
            "TOURNAMENT", "SELECT YOUR CHAMPION",
            Paths.CardTournament,
            "Arena duel on a compact battlefield. Three riders enter - one leaves. " +
            "The classic test of machine and pilot, fought around the mesa under tournament rules.",
            "ENTER TOURNAMENT", featured: true, icon: CardIcon.Kind.Compass,
            go: () => OnTournament?.Invoke()));
        cards.AddChild(Card(
            "STORY", "TAKE ON A MISSION",
            Paths.CardMission,
            "Planetary operations on a full-scale front. Pick a world in orbit, take on " +
            "a mission, then deploy from your base and march on the enemy.",
            "VIEW MISSIONS", featured: false, icon: CardIcon.Kind.Target,
            go: () => OnMission?.Invoke()));

        Layout();
        GetTree().Root.SizeChanged += Layout;
    }

    // Fit the cards to the visible area: their width follows
    // the window (two cards + margins + the gap), their height
    // what remains under the title row. The ART
    // height is what's left of the card after the fixed
    // interior (card margins 62, separations 70, header 56,
    // rule 2, spacer 4, action bar 50) and the description's
    // height - the first cut scaled the art by a ratio of the
    // card height and the interior overflowed at short
    // windows, pushing the action bar past the card's bottom
    // edge over the telemetry line (owner's screenshot,
    // 2026-11-13). At 1080p the budget lands on the designed
    // 600x620 with the full 246px art.
    private void Layout()
    {
        var vp = GetViewportRect().Size;
        if (vp.X < MinLayoutViewportWidth || _cardT == null) return;
        float cardW = Mathf.Clamp((vp.X - 2f * PageMarginHorizontal - CardGap) / 2f, CardMinWidth, CardMaxWidth);
        float cardH = Mathf.Clamp(vp.Y - CardVerticalAllowance, CardMinHeight, CardMaxHeight); // 250 while the bottom telemetry row stood; it was removed at the owner's word (2026-11-15)
        var cs = new Vector2(cardW, cardH);
        _cardT.CustomMinimumSize = cs;
        _cardM.CustomMinimumSize = cs;
        float descH = cardW >= DescBreakpointWidth ? DescWideHeight : DescNarrowHeight; // wrapped lines at the card's text width
        float artH = Mathf.Clamp(cardH - CardMarginAllowance - CardInteriorAllowance - descH, ArtMinHeight, ArtMaxHeight);
        _artT.CustomMinimumSize = new Vector2(0f, artH);
        _artM.CustomMinimumSize = new Vector2(0f, artH);
        var dsz = new Vector2(cardW - CardTextInset, 0f);
        _descT.CustomMinimumSize = dsz;
        _descM.CustomMinimumSize = dsz;
        float th = Mathf.Clamp(vp.Y * TitleHeightFraction, TitleMinHeight, TitleMaxHeight);
        _titleRect.CustomMinimumSize = new Vector2(th * TitleAspect, th);
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

    private Control Card(string title, string sub, string artPath, string desc,
        string action, bool featured, CardIcon.Kind icon, System.Action go)
    {
        var card = new Button { CustomMinimumSize = new Vector2(CardMaxWidth, CardMaxHeight) };
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
            Cut = CardCornerRadius,
            BorderWidth = featured ? FeaturedCardBorderWidth : CardBorderWidth,
            FillNormal = FlowUi.Navy,
            FillHover = FlowUi.NavySoft,
            FillPressed = FlowUi.ButtonPressed,
            BorderNormal = featured ? FlowUi.Gold : FlowUi.Steel,
            BorderHover = FlowUi.Gold
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
        margin.AddThemeConstantOverride("margin_left", CardMarginHorizontal);
        margin.AddThemeConstantOverride("margin_right", CardMarginHorizontal);
        margin.AddThemeConstantOverride("margin_top", CardMarginTop);
        margin.AddThemeConstantOverride("margin_bottom", CardMarginBottom); // bar -> card edge = text -> bar (owner's rule)
        card.AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", CardColumnSeparation);
        margin.AddChild(v);

        // Header: line icon + title / subtitle stack, over a
        // gold rule. (The 01/02 panel indices were removed at
        // the owner's word.)
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", CardHeaderSeparation);
        v.AddChild(head);
        head.AddChild(new CardIcon
        {
            IconKind = icon,
            Tint = featured ? FlowUi.Gold : FlowUi.Dim,
            CustomMinimumSize = new Vector2(CardIconSize, CardIconSize)
        });
        var titles = new VBoxContainer();
        titles.AddThemeConstantOverride("separation", CardTitleSeparation);
        titles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(titles);
        titles.AddChild(Serif(title, CardTitleFontSize, featured ? FlowUi.Gold : FlowUi.Ink, CinzelBold));
        var subL = Serif(Spread(sub), CardSubtitleFontSize, featured ? FlowUi.GoldDim : FlowUi.Dim, SansSemi);
        titles.AddChild(subL);
        var rule = new ColorRect
        {
            Color = new Color(FlowUi.GoldDim, CardRuleAlpha),
            CustomMinimumSize = new Vector2(0f, RuleHeight)
        };
        v.AddChild(rule);

        // Illustration (inset from the card's rounded frame).
        var art = new TextureRect
        {
            Texture = GD.Load<Texture2D>(artPath),
            CustomMinimumSize = new Vector2(0f, ArtMaxHeight),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            SizeFlagsVertical = SizeFlags.ExpandFill // absorbs leftover height with the spacer
        };
        if (featured) _artT = art; else _artM = art;
        v.AddChild(art);

        var d = Serif(desc, CardDescFontSize, FlowUi.Ink, Sans);
        if (featured) _descT = d; else _descM = d;
        d.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        d.CustomMinimumSize = new Vector2(CardMaxWidth - CardTextInset, DescMinHeight);
        v.AddChild(d);

        // Fixed 4px spacer (owner's rule): with the two 14px
        // separations around it, the gap between the text and
        // the button is 32 - exactly the button's bottom
        // margin to the card's lower edge. Leftover height
        // goes to the art (ExpandFill), never to this gap.
        var spacer = new Control { CustomMinimumSize = new Vector2(0f, CardSpacerHeight) };
        v.AddChild(spacer);

        // Action bar: a chamfered plate - solid gold on the
        // featured card, dark with a steel frame on the other.
        var barWrap = new Control { CustomMinimumSize = new Vector2(0f, ActionBarHeight) };
        v.AddChild(barWrap);
        var bar = new CutPanel
        {
            Cut = ActionBarCornerRadius,
            BorderWidth = CardBorderWidth,
            FillNormal = featured ? BarFeaturedNormal : BarPlainNormal,
            FillHover = featured ? BarFeaturedHover : BarPlainHover,
            FillPressed = BarPressed,
            BorderNormal = featured ? BarFeaturedBorder : FlowUi.Steel,
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
        var act = Serif(action, CardActionFontSize, featured ? BarFeaturedText : FlowUi.Ink,
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

// A panel face with ROUNDED corners (owner, 2026-11-23:
// 'make all corners round on the 1st screen' - the
// chamfered "rhombus-cut" corners this class was built for
// are retired on this screen; the class name stays because
// the screens wiring it predate the change). The Cut field
// is now the corner radius. State colours are set by the
// owning button.
public partial class CutPanel : Control
{
    public float Cut = 16f;
    public float BorderWidth = 2f;
    public Color FillNormal = Colors.Transparent;
    public Color FillHover = Colors.Transparent;
    public Color FillPressed = Colors.Transparent;
    public Color BorderNormal = DefaultBorder;
    public Color BorderHover = DefaultBorderHover;

    private static readonly Color DefaultBorder = new(1, 1, 1, 0.5f);
    private static readonly Color DefaultBorderHover = new(1, 1, 1, 0.8f);
    public bool Hover, Pressed, Focused;

    public override void _Draw()
    {
        var fill = Pressed ? FillPressed : Hover ? FillHover : FillNormal;
        var border = Hover || Focused ? BorderHover : BorderNormal;
        var sb = new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            AntiAliasing = true,
        };
        sb.SetBorderWidthAll(Mathf.Max(1, Mathf.RoundToInt(BorderWidth)));
        sb.SetCornerRadiusAll(Mathf.RoundToInt(Cut));
        DrawStyleBox(sb, new Rect2(Vector2.Zero, Size));
    }
}

// The cards' line icons, drawn as vector strokes (no glyph
// fonts): a compass rose for Tournament, a targeting reticle
// for Mission - the concept's two marks.
public partial class CardIcon : Control
{
    // Stroke recipe shared by both marks (the radius
    // fractions and dot sizes are each icon's own artwork).
    private const float IconStrokeWidth = 1.5f;
    private const float IconThinStrokeWidth = 1.25f;
    private const int IconArcSegments = 48;
    private const int IconInnerArcSegments = 32;

    public enum Kind { Compass, Target }
    public Kind IconKind;
    public Color Tint = Colors.White;

    public override void _Draw()
    {
        var c = Size * 0.5f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.46f;
        if (IconKind == Kind.Compass)
        {
            DrawArc(c, r, 0f, Mathf.Tau, IconArcSegments, Tint, IconStrokeWidth, true);
            // Four long points, four short diagonals.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.Pi * 0.5f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                DrawLine(c - dir * r * 1.02f, c + dir * r * 1.02f, Tint, IconStrokeWidth, true);
                float b = a + Mathf.Pi * 0.25f;
                var d2 = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                DrawLine(c, c + d2 * r * 0.55f, Tint, IconThinStrokeWidth, true);
            }
            DrawCircle(c, 2.5f, Tint);
        }
        else
        {
            DrawArc(c, r * 0.78f, 0f, Mathf.Tau, IconArcSegments, Tint, IconStrokeWidth, true);
            DrawArc(c, r * 0.30f, 0f, Mathf.Tau, IconInnerArcSegments, Tint, IconThinStrokeWidth, true);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.Pi * 0.5f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                DrawLine(c + dir * r * 0.52f, c + dir * r * 1.05f, Tint, IconStrokeWidth, true);
            }
            DrawCircle(c, 2f, Tint);
        }
    }
}
