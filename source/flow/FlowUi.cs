using Godot;

// Shared look + procedural art for the front-end flow screens.
// Palette: deep navy panels, steel borders, champagne-gold headers -
// the storefront of the game, matching the RiderLab console.
public static class FlowUi
{
    public static readonly Color Navy = new(0.045f, 0.065f, 0.10f, 0.94f);
    public static readonly Color NavySoft = new(0.06f, 0.085f, 0.13f, 0.88f);
    public static readonly Color Steel = new(0.5f, 0.56f, 0.62f, 0.6f);
    public static readonly Color Gold = new(0.98f, 0.80f, 0.35f);
    public static readonly Color GoldDim = new(0.72f, 0.58f, 0.26f);
    public static readonly Color Ink = new(0.92f, 0.94f, 0.97f);
    public static readonly Color Dim = new(0.62f, 0.70f, 0.78f);
    public static readonly Color Good = new(0.55f, 0.95f, 0.60f);
    public static readonly Color Bad = new(1.0f, 0.55f, 0.45f);

    // Button state faces (MakeButton's box family).
    public static readonly Color ButtonNormal = new(0.10f, 0.14f, 0.20f, 0.95f);
    public static readonly Color ButtonHover = new(0.16f, 0.21f, 0.30f, 0.98f);
    public static readonly Color ButtonPressed = new(0.23f, 0.19f, 0.08f, 0.98f);
    public static readonly Color ButtonDisabled = new(0.07f, 0.08f, 0.10f, 0.9f);
    public static readonly Color ButtonFontDisabled = new(0.4f, 0.44f, 0.5f);
    public static readonly Color Transparent = new(0, 0, 0, 0);

    // Panel-button state faces: the compact +/-/- buttons on
    // the team screens, drawn with Panel boxes (a slightly
    // different face family from MakeButton's).
    public static readonly Color PanelButtonNormal = new(0.10f, 0.13f, 0.19f, 0.98f);
    public static readonly Color PanelButtonHover = new(0.16f, 0.20f, 0.28f, 0.98f);
    public static readonly Color PanelButtonPressed = new(0.20f, 0.17f, 0.08f, 0.98f);
    public static readonly Color PanelButtonDisabled = new(0.06f, 0.07f, 0.09f, 0.9f);
    public static readonly Color PanelButtonFontDisabled = new(0.45f, 0.48f, 0.52f);

    // Monitor palette (the desk-monitor description boxes).
    public static readonly Color MonitorBezelBg = new(0.05f, 0.06f, 0.075f, 0.98f);
    public static readonly Color MonitorBezelBorder = new(0.30f, 0.33f, 0.38f);
    public static readonly Color MonitorScreenBg = new(0.010f, 0.032f, 0.026f, 1f);
    public static readonly Color MonitorScreenBorder = new(0.10f, 0.24f, 0.19f);

    // Shared screen geometry: every wizard screen wears the
    // same frame (margins, separations, menu font sizes), so
    // the values live here once and the screens name them.
    public const int PanelMarginHorizontal = 18;
    public const int PanelMarginVertical = 12;
    public const int PanelBorderWidth = 2;
    public const int PanelCornerRadius = 8;
    public const int ButtonMarginHorizontal = 18;
    public const int ButtonMarginVertical = 11;
    public const int ButtonBorderWidth = 2;
    public const int ButtonCornerRadius = 6;
    public const int ButtonFocusBorderWidth = 3;
    public const int DefaultButtonFontSize = 15;
    public const int ScreenMarginHorizontal = 18;
    public const int ScreenMarginVertical = 14;
    public const int ScreenSeparation = 10;
    public const int MenuSeparation = 10;
    public const int MidSeparation = 12;
    public const int PanelColumnSeparation = 7;
    public const int RowSeparation = 8;
    public const int MenuButtonFontSize = 14;
    public const int SectionHeadingFontSize = 13;
    public const int BodyTextFontSize = 13;
    public const int SmallTextFontSize = 12;
    public const int SubTextFontSize = 11;
    public const float DescriptionScrollMinHeight = 150f;
    public const float VisualPanelAlpha = 0.75f;
    public const int SelectorSeparation = 8;
    public const int SelectorArrowFontSize = 17;
    public const float SelectorArrowWidth = 42f;
    public const float AddButtonSize = 46f;
    public const int AddButtonFontSize = 18;
    public const float RemoveButtonSize = 30f;
    public const int RemoveButtonFontSize = 15;
    public const int RowButtonMargin = 2;
    public const int RiderCardPadding = 10;
    public const float RiderCardAlpha = 0.9f;
    public const float RiderSliderExtraWidth = 100f;
    public const int ThreatPipCount = 5;
    public const int RiderSelectorFontSize = 16;

    public static StyleBoxFlat Panel(Color bg, float alpha = -1f)
    {
        var c = bg; if (alpha >= 0f) c.A = alpha;
        var sb = new StyleBoxFlat
        {
            BgColor = c,
            BorderColor = Steel,
            ContentMarginLeft = PanelMarginHorizontal, ContentMarginRight = PanelMarginHorizontal,
            ContentMarginTop = PanelMarginVertical, ContentMarginBottom = PanelMarginVertical
        };
        sb.SetBorderWidthAll(PanelBorderWidth);
        sb.SetCornerRadiusAll(PanelCornerRadius);
        return sb;
    }

    public static StyleBoxFlat ButtonBox(Color bg)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = Steel,
            ContentMarginLeft = ButtonMarginHorizontal, ContentMarginRight = ButtonMarginHorizontal,
            ContentMarginTop = ButtonMarginVertical, ContentMarginBottom = ButtonMarginVertical
        };
        sb.SetBorderWidthAll(ButtonBorderWidth);
        sb.SetCornerRadiusAll(ButtonCornerRadius);
        return sb;
    }

    private static FontFile _bodyFont;
    // The body font, applied EXPLICITLY (owner, 2026-11-13:
    // Cinzel headings + Open Sans text on every screen - set
    // in code, not left to the project theme, so a stale theme
    // cache can never bring the mono back).
    public static FontFile BodyFont => _bodyFont ??= GD.Load<FontFile>("res://assets/fonts/OpenSans-Regular.ttf");

    public static Button MakeButton(string text, int fontSize = DefaultButtonFontSize)
    {
        var b = new Button { Text = text };
        b.AddThemeFontOverride("font", BodyFont);
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeColorOverride("font_color", Ink);
        b.AddThemeColorOverride("font_hover_color", Gold);
        b.AddThemeColorOverride("font_disabled_color", ButtonFontDisabled);
        b.AddThemeStyleboxOverride("normal", ButtonBox(ButtonNormal));
        b.AddThemeStyleboxOverride("hover", ButtonBox(ButtonHover));
        b.AddThemeStyleboxOverride("pressed", ButtonBox(ButtonPressed));
        b.AddThemeStyleboxOverride("disabled", ButtonBox(ButtonDisabled));
        // Keyboard focus must be VISIBLE: a gold frame, no fill.
        var focus = ButtonBox(Transparent);
        focus.DrawCenter = false;
        focus.BorderColor = Gold;
        focus.SetBorderWidthAll(ButtonFocusBorderWidth);
        b.AddThemeStyleboxOverride("focus", focus);
        b.AddThemeColorOverride("font_focus_color", Gold);
        return b;
    }

    // Autowrap is OPT-IN: a wrapping label reports a collapsed
    // minimum width and a huge wrapped height, which poisons every
    // container above it (measured: one wrapping label in a top bar
    // inflated the whole screen's minimum height to 1380px).
    // Paragraph labels pass wrap: true and live in fixed-width
    // columns where the container assigns their width.
    public static Label MakeLabel(string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = false)
    {
        var l = new Label { Text = text, HorizontalAlignment = align };
        l.AddThemeFontOverride("font", BodyFont);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    // A desk-monitor look for description text: a heavy bezel
    // around a dark green-black screen, so the player reads the
    // planet (or the contract) off a computer, not a panel.
    public const int MonitorBezelMargin = 8;
    public const int MonitorBezelBorderWidth = 3;
    public const int MonitorBezelCornerRadius = 10;
    public const int MonitorScreenMarginHorizontal = 10;
    public const int MonitorScreenMarginVertical = 8;
    public const int MonitorScreenBorderWidth = 1;
    public const int MonitorScreenCornerRadius = 4;

    public static StyleBoxFlat MonitorBezel()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = MonitorBezelBg,
            BorderColor = MonitorBezelBorder,
            ContentMarginLeft = MonitorBezelMargin, ContentMarginRight = MonitorBezelMargin,
            ContentMarginTop = MonitorBezelMargin, ContentMarginBottom = MonitorBezelMargin
        };
        sb.SetBorderWidthAll(MonitorBezelBorderWidth);
        sb.SetCornerRadiusAll(MonitorBezelCornerRadius);
        return sb;
    }

    public static StyleBoxFlat MonitorScreen()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = MonitorScreenBg,
            BorderColor = MonitorScreenBorder,
            ContentMarginLeft = MonitorScreenMarginHorizontal, ContentMarginRight = MonitorScreenMarginHorizontal,
            ContentMarginTop = MonitorScreenMarginVertical, ContentMarginBottom = MonitorScreenMarginVertical
        };
        sb.SetBorderWidthAll(MonitorScreenBorderWidth);
        sb.SetCornerRadiusAll(MonitorScreenCornerRadius);
        return sb;
    }

    public static readonly Color ScreenInk = new(0.84f, 0.96f, 0.89f);

    // CRT dressing for the monitor screens: faint green scanlines
    // and a soft glow at the top, drawn as an overlay texture.
    public const int ScanlineDefaultSize = 256;
    public const int ScanlinePeriod = 3;
    public const float ScanlineRowAlpha = 0.05f;
    public const float ScanlineGlowStrength = 0.03f;
    public const float ScanlineGlowTintFactor = 0.3f;
    public static readonly Color ScanlineTint = new(0.4f, 1f, 0.8f);

    public static ImageTexture ScanlineTexture(int w = ScanlineDefaultSize, int h = ScanlineDefaultSize)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int j = 0; j < h; j++)
        for (int i = 0; i < w; i++)
        {
            float a = (j % ScanlinePeriod == 2) ? ScanlineRowAlpha : 0f;
            float glow = ScanlineGlowStrength * (1f - j / (float)h);
            img.SetPixel(i, j, new Color(ScanlineTint, a + glow * ScanlineGlowTintFactor));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static FontFile _headFont;
    public static FontFile HeadFont(bool bold) =>
        _headFont ??= GD.Load<FontFile>("res://assets/fonts/Cinzel-Bold.ttf");

    // Display font for HEADINGS everywhere (owner, 2026-11-13:
    // the home screen's new fonts apply game-wide): Cinzel Bold,
    // the engraved serif of his menu concept. Body text is Open
    // Sans via the project theme's default font. (The bold flag
    // is retained at call sites but no longer changes the face.)
    public static void Heading(Control c, bool bold = true) =>
        c.AddThemeFontOverride("font", HeadFont(bold));

    public static Label MakeHeading(string text, int size, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left, bool bold = true)
    {
        var l = MakeLabel(text, size, color, align);
        Heading(l, bold);
        return l;
    }

    private static FontFile _navFont;
    // Navigation screens are Open Sans everywhere (owner,
    // 2026-11-14) - no Cinzel there. Semibold keeps the
    // headings' weight without the serif.
    public static FontFile NavFont =>
        _navFont ??= GD.Load<FontFile>("res://assets/fonts/OpenSans-SemiBold.ttf");

    public static Label NavHeading(string text, int size, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = MakeLabel(text, size, color, align);
        l.AddThemeFontOverride("font", NavFont);
        return l;
    }

    // Sentence case for display: spec and planet names are
    // stored all-caps ('PROMETHEUS'); most screens read
    // 'Prometheus'. (The lab title is the standing exception.)
    public static string NameCase(string n) =>
        string.IsNullOrEmpty(n) ? n : char.ToUpper(n[0]) + n.Substring(1).ToLowerInvariant();

    // The front-end's selector row (the owner's "slider"):
    // ‹ NAME › cycling one entry per press.
    public static HBoxContainer SelectorRow(out Label name, int fontSize, System.Action<int> step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", SelectorSeparation);
        var prev = MakeButton("‹", SelectorArrowFontSize);
        prev.CustomMinimumSize = new Vector2(SelectorArrowWidth, 0f);
        prev.Pressed += () => step(-1);
        row.AddChild(prev);
        name = NavHeading("", fontSize, Gold, HorizontalAlignment.Center);
        name.SizeFlagsHorizontal = Control.SizeFlags.Expand;
        row.AddChild(name);
        var next = MakeButton("›", SelectorArrowFontSize);
        next.CustomMinimumSize = new Vector2(SelectorArrowWidth, 0f);
        next.Pressed += () => step(1);
        row.AddChild(next);
        return row;
    }

    // Panel boxes for buttons that must fit inside a row wear
    // near-zero margins (FlowUi.Panel's own margins would give
    // the button a ~44px minimum height whatever its rect says).
    public static StyleBoxFlat RowButtonBox(Color bg)
    {
        var sb = Panel(bg);
        sb.ContentMarginLeft = RowButtonMargin; sb.ContentMarginRight = RowButtonMargin;
        sb.ContentMarginTop = RowButtonMargin; sb.ContentMarginBottom = RowButtonMargin;
        return sb;
    }

    // The team screens' add control: a compact '+' button,
    // right-aligned below the last row - never stretched
    // across the list.
    public static Control AddRow(System.Action add, bool disabled)
    {
        var row = new HBoxContainer();
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var b = new Button { Text = "+", CustomMinimumSize = new Vector2(AddButtonSize, AddButtonSize) };
        b.AddThemeFontSizeOverride("font_size", AddButtonFontSize);
        b.AddThemeStyleboxOverride("normal", Panel(PanelButtonNormal));
        b.AddThemeStyleboxOverride("hover", Panel(PanelButtonHover));
        b.AddThemeStyleboxOverride("pressed", Panel(PanelButtonPressed));
        b.AddThemeStyleboxOverride("disabled", Panel(PanelButtonDisabled));
        b.AddThemeColorOverride("font_color", Ink);
        b.AddThemeColorOverride("font_hover_color", Gold);
        b.AddThemeColorOverride("font_disabled_color", PanelButtonFontDisabled);
        b.Disabled = disabled;
        b.Pressed += () => add();
        row.AddChild(b);
        return row;
    }

    // One shared slider minimum for every rider row (owner,
    // 2026-11-15): the widest label the selector can show -
    // the longest rider name with the active suffix - plus
    // the two 42px arrows and separations. With one minimum
    // (and the remove slot reserved on rows without a
    // button), the half-width split lands identical on
    // every row instead of drifting with the name length.
    public static float RiderSliderMin()
    {
        var probe = NavHeading("", RiderSelectorFontSize, Gold, HorizontalAlignment.Center);
        var font = probe.GetThemeFont("font");
        float w = 0f;
        foreach (var s in RiderSpecs.All)
        {
            var t = $"{NameCase(s.Name)} - {s.Tonnage:F0}T - {NameCase(s.ClassLabel)} - Active";
            w = Mathf.Max(w, font.GetStringSize(t, HorizontalAlignment.Left, -1f, RiderSelectorFontSize).X);
        }
        return w + RiderSliderExtraWidth;
    }

    // A description on a desk monitor: bezel, dark screen,
    // scanlines. The bezel fits BOTH children to its rect
    // directly (a PanelContainer fits every child): the screen,
    // then the scanlines over it - no intermediate plain
    // Control (it silently zeroes the chain's minimum size).
    public static Control Monitor(Control inner)
    {
        var bezel = new PanelContainer();
        bezel.AddThemeStyleboxOverride("panel", MonitorBezel());
        var screen = new PanelContainer();
        screen.AddThemeStyleboxOverride("panel", MonitorScreen());
        bezel.AddChild(screen);
        screen.AddChild(inner);
        var scan = new TextureRect
        {
            Texture = ScanlineTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        bezel.AddChild(scan);
        return bezel;
    }

    // Keep a monitor label exactly as wide as its scroll
    // viewport (minus the scrollbar), so the text reflows to
    // the full screen width whenever the window changes size.
    public const float ScrollbarAllowance = 14f;
    public const float MinFitTextWidth = 40f;

    public static void FitTextWidth(ScrollContainer scroll, Label label)
    {
        if (scroll == null || label == null) return;
        float w = scroll.Size.X - ScrollbarAllowance;
        if (w > MinFitTextWidth) label.CustomMinimumSize = new Vector2(w, label.CustomMinimumSize.Y);
    }

    public static string ThreatPips(int threat)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ThreatPipCount; i++) sb.Append(i < threat ? "●" : "○");
        return sb.ToString();
    }

    // ---------- procedural textures ----------

    private static float HashNoise(float x, float z)
    {
        float s = Mathf.Sin(x * 127.1f + z * 311.7f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }

    private static float ValueNoise(float x, float z)
    {
        float xi = Mathf.Floor(x), zi = Mathf.Floor(z);
        float xf = x - xi, zf = z - zi;
        float u = xf * xf * (3f - 2f * xf), v = zf * zf * (3f - 2f * zf);
        float a = HashNoise(xi, zi), b = HashNoise(xi + 1f, zi);
        float c = HashNoise(xi, zi + 1f), d = HashNoise(xi + 1f, zi + 1f);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    private static float Fbm(float x, float z, int oct)
    {
        float sum = 0f, amp = 1f, tot = 0f;
        for (int i = 0; i < oct; i++)
        {
            sum += amp * ValueNoise(x, z); tot += amp;
            amp *= 0.5f; x = x * 2.03f + 19.19f; z = z * 2.03f - 7.31f;
        }
        return sum / tot;
    }

    public static readonly Color CloudColor = new(0.95f, 0.97f, 1.0f);

    // A shaded planet disc: fbm continents over a deep base, polar
    // caps, and a baked terminator (light from the upper left), so
    // the flat texture reads as a globe on the orbital screen.
    public const int PlanetTextureSize = 512;

    public static ImageTexture PlanetTexture(PlanetDef p, int size = PlanetTextureSize, float seed = 0f)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var light = new Vector2(-0.55f, -0.62f).Normalized();
        for (int j = 0; j < size; j++)
        for (int i = 0; i < size; i++)
        {
            float u = (i / (float)(size - 1)) * 2f - 1f;
            float v = (j / (float)(size - 1)) * 2f - 1f;
            float r2 = u * u + v * v;
            if (r2 > 1f) { img.SetPixel(i, j, Transparent); continue; }
            float nz = Mathf.Sqrt(1f - r2);
            float n = Fbm(u * 3.1f + seed * 13.7f, v * 3.1f - seed * 7.3f, 5);
            float detail = Fbm(u * 9f - seed * 3f, v * 9f + seed * 5f, 4);
            Color col = n > 0.52f
                ? p.LandColor.Lerp(p.PolarColor, Mathf.Clamp((detail - 0.55f) * 1.6f, 0f, 0.45f))
                : p.BaseColor.Lerp(p.LandColor, Mathf.Clamp((n - 0.30f) * 1.4f, 0f, 0.55f));
            // Polar caps.
            float cap = Mathf.SmoothStep(0.72f, 0.92f, Mathf.Abs(v) + (detail - 0.5f) * 0.14f);
            col = col.Lerp(p.PolarColor, cap * 0.85f);
            // Wispy cloud bands.
            float cloud = Fbm(u * 2.2f + 40f + seed, v * 6.5f, 3);
            col = col.Lerp(CloudColor, Mathf.Clamp((cloud - 0.60f) * 1.5f, 0f, 0.4f));
            // Sphere shading: diffuse from the light dir + limb darkening.
            float diff = Mathf.Clamp(new Vector2(u, v).Dot(light) * 0.5f + nz * 0.85f, 0.06f, 1.12f);
            float limb = 0.72f + 0.28f * nz;
            col = new Color(col.R * diff * limb, col.G * diff * limb, col.B * diff * limb, 1f);
            img.SetPixel(i, j, col);
        }
        return ImageTexture.CreateFromImage(img);
    }

    // Stylized top-down of the tournament arena for the map card:
    // dry-grass mottling, the flat mesa disc at the center, and the
    // three spawn pads on their triangle.
    public const int ArenaTextureSize = 512;
    public const float ArenaWorldHalfExtent = 130f;
    public static readonly Color PadSurfaceColor = new(0.72f, 0.74f, 0.78f);
    public static readonly Color PadRingColor = new(0.20f, 0.21f, 0.24f);

    public static ImageTexture ArenaTexture(int size = ArenaTextureSize, int biome = 1)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var grass = biome == 0 ? new Color(0.72f, 0.58f, 0.34f)
            : biome == 2 ? new Color(0.33f, 0.34f, 0.36f)
            : new Color(0.42f, 0.40f, 0.22f);
        var dry = biome == 0 ? new Color(0.79f, 0.65f, 0.40f)
            : biome == 2 ? new Color(0.40f, 0.40f, 0.42f)
            : new Color(0.55f, 0.47f, 0.28f);
        var rock = biome == 2 ? new Color(0.44f, 0.44f, 0.46f) : new Color(0.48f, 0.40f, 0.33f);
        var mesaTop = biome == 0 ? new Color(0.82f, 0.68f, 0.45f)
            : biome == 2 ? new Color(0.50f, 0.50f, 0.52f)
            : new Color(0.62f, 0.54f, 0.42f);
        float half = size / 2f;
        var pads = new[]
        {
            new Vector2(0f, 80f), new Vector2(-69.3f, -40f), new Vector2(69.3f, -40f)
        };
        for (int j = 0; j < size; j++)
        for (int i = 0; i < size; i++)
        {
            float wx = (i - half) / half * ArenaWorldHalfExtent;
            float wz = (j - half) / half * ArenaWorldHalfExtent;
            float r = Mathf.Sqrt(wx * wx + wz * wz);
            float n = Fbm(wx * 0.045f + 7f, wz * 0.045f, 4);
            Color col = grass.Lerp(dry, Mathf.Clamp((n - 0.3f) * 1.8f, 0f, 1f));
            if (biome == 2)
            {
                // City: a street grid over the blocks.
                float gx = Mathf.Abs(((wx + 13f) % 26f + 26f) % 26f - 13f);
                float gz = Mathf.Abs(((wz + 13f) % 26f + 26f) % 26f - 13f);
                if (Mathf.Min(gx, gz) > 11.4f) col = col.Darkened(0.35f);
            }
            if (r > 96f) col = col.Lerp(rock, Mathf.SmoothStep(96f, 128f, r + (n - 0.5f) * 22f)); // rim hills
            if (r < 15.5f) col = col.Lerp(rock, Mathf.SmoothStep(15.5f, 13.5f, r));                 // mesa wall
            if (r < 13.5f) col = mesaTop;                                                          // mesa top
            foreach (var p in pads)
            {
                float d = new Vector2(wx - p.X, wz - p.Y).Length();
                if (d < 5.5f) col = PadSurfaceColor;
                else if (d < 7.5f) col = PadRingColor;
            }
            float vig = 1f - 0.25f * Mathf.SmoothStep(90f, 130f, r);
            img.SetPixel(i, j, new Color(col.R * vig, col.G * vig, col.B * vig, 1f));
        }
        return ImageTexture.CreateFromImage(img);
    }

    // Deep-space panorama for the flow backdrop: near-black gradient,
    // star field, two faint nebula washes.
    public const int StarTextureWidth = 1024;
    public const int StarTextureHeight = 512;
    public const int StarCount = 1500;
    public static readonly Color SpaceTopColor = new(0.012f, 0.018f, 0.035f);
    public static readonly Color SpaceBottomColor = new(0.03f, 0.045f, 0.08f);
    public static readonly Color NebulaBlueColor = new(0.10f, 0.13f, 0.22f);
    public static readonly Color NebulaBrownColor = new(0.16f, 0.09f, 0.05f);
    public static readonly Color StarWarmColor = new(1f, 0.85f, 0.65f);
    public static readonly Color StarCoolColor = new(0.85f, 0.92f, 1f);

    public static ImageTexture StarTexture(int w = StarTextureWidth, int h = StarTextureHeight, ulong seed = 4242)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = seed };
        for (int j = 0; j < h; j++)
        for (int i = 0; i < w; i++)
        {
            float t = j / (float)h;
            var col = SpaceTopColor.Lerp(SpaceBottomColor, 1f - t);
            float neb = Fbm(i * 0.006f, j * 0.006f, 3);
            if (neb > 0.58f) col = col.Lerp(NebulaBlueColor, (neb - 0.58f) * 1.2f);
            float neb2 = Fbm(i * 0.004f + 31f, j * 0.004f, 3);
            if (neb2 > 0.62f) col = col.Lerp(NebulaBrownColor, (neb2 - 0.62f) * 0.9f);
            img.SetPixel(i, j, col);
        }
        for (int s = 0; s < StarCount; s++)
        {
            int x = rng.RandiRange(0, w - 1), y = rng.RandiRange(0, h - 1);
            float b = rng.RandfRange(0.35f, 1f);
            var tint = rng.Randf() < 0.12f ? StarWarmColor : StarCoolColor;
            img.SetPixel(x, y, tint * b);
            if (b > 0.9f && x + 1 < w) img.SetPixel(x + 1, y, tint * b * 0.5f);
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static Texture2D _loadingTex;

    // The ONE loading background colour (owner, 2026-11-11
    // round 5: 'make sure screen and loader background are
    // the same color'): sampled from the art's own edge
    // pixels, used OPAQUELY by every loading cover, so the
    // art's ground and the screen's ground are the same
    // colour by construction - no seam, at any window size.
    public static readonly Color LoadingBg = new(4f / 255f, 5f / 255f, 9f / 255f, 1f);

    // The loading screen (owner, round 5): his targeting-
    // reticle rider art, plain - no white corner brackets,
    // no blinking - over a LoadingBg rect. Under the art, an
    // animated ripple bar instead of a 'Loading...' line
    // (owner, 2026-11-13). Every loading cover in the game
    // builds this.
    public static Control MakeLoadingArt()
    {
        _loadingTex ??= GD.Load<Texture2D>("res://assets/ui/loading_target.png");
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var arc = new AspectRatioContainer { Ratio = 1f, StretchMode = AspectRatioContainer.StretchModeEnum.Fit };
        arc.AnchorLeft = 0.06f; arc.AnchorTop = 0.04f; arc.AnchorRight = 0.94f; arc.AnchorBottom = 0.87f;
        arc.OffsetLeft = 0f; arc.OffsetTop = 0f; arc.OffsetRight = 0f; arc.OffsetBottom = 0f;
        arc.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(arc);
        arc.AddChild(new TextureRect
        {
            Texture = _loadingTex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var bar = new RippleBar();
        bar.AnchorLeft = 0.5f; bar.AnchorRight = 0.5f;
        bar.AnchorTop = 0.90f; bar.AnchorBottom = 0.90f;
        bar.OffsetLeft = -230f; bar.OffsetRight = 230f;
        bar.OffsetTop = -11f; bar.OffsetBottom = 11f;
        root.AddChild(bar);
        return root;
    }
}

// The loading indicator (owner, 2026-11-13): a track line
// with end ticks and a bright gold segment that sweeps
// left to right and back on a timer - a ripple, no text.
// Redraws only while actually on screen.
public partial class RippleBar : Control
{
    public const float TrackWidth = 2f;
    public const float TickHalfHeight = 5f;
    public const float GlowWidth = 7f;
    public const float GlowAlpha = 0.22f;
    public const float CoreWidth = 2.5f;
    public const float SegmentFraction = 0.34f;
    public const float SweepSpeed = 1.9f;
    public static readonly Color TrackColor = new(0.45f, 0.52f, 0.60f, 0.35f);

    private double _t;

    public RippleBar()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        _t += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float w = Size.X, midY = Size.Y * 0.5f;
        var track = TrackColor;
        DrawLine(new Vector2(0, midY), new Vector2(w, midY), track, TrackWidth, true);
        DrawLine(new Vector2(0, midY - TickHalfHeight), new Vector2(0, midY + TickHalfHeight), track, TrackWidth, true);
        DrawLine(new Vector2(w, midY - TickHalfHeight), new Vector2(w, midY + TickHalfHeight), track, TrackWidth, true);
        // Ping-pong phase with eased ends (smoothstep of a sine).
        float ph = (Mathf.Sin((float)_t * SweepSpeed) + 1f) * 0.5f;
        ph = ph * ph * (3f - 2f * ph);
        float segW = w * SegmentFraction;
        float x = ph * (w - segW);
        var gold = FlowUi.Gold;
        DrawLine(new Vector2(x, midY), new Vector2(x + segW, midY), new Color(gold, GlowAlpha), GlowWidth, true);
        DrawLine(new Vector2(x, midY), new Vector2(x + segW, midY), gold, CoreWidth, true);
    }
}
