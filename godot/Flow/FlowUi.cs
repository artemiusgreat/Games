using Godot;

// Shared look + procedural art for the front-end flow screens.
// Palette: deep navy panels, steel borders, champagne-gold headers —
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

    public static StyleBoxFlat Panel(Color bg, float alpha = -1f)
    {
        var c = bg; if (alpha >= 0f) c.A = alpha;
        var sb = new StyleBoxFlat
        {
            BgColor = c,
            BorderColor = Steel,
            ContentMarginLeft = 18, ContentMarginRight = 18,
            ContentMarginTop = 12, ContentMarginBottom = 12
        };
        sb.SetBorderWidthAll(2);
        sb.SetCornerRadiusAll(8);
        return sb;
    }

    public static StyleBoxFlat ButtonBox(Color bg)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = Steel,
            ContentMarginLeft = 18, ContentMarginRight = 18,
            ContentMarginTop = 11, ContentMarginBottom = 11
        };
        sb.SetBorderWidthAll(2);
        sb.SetCornerRadiusAll(6);
        return sb;
    }

    public static Button MakeButton(string text, int fontSize = 15)
    {
        var b = new Button { Text = text };
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeColorOverride("font_color", Ink);
        b.AddThemeColorOverride("font_hover_color", Gold);
        b.AddThemeColorOverride("font_disabled_color", new Color(0.4f, 0.44f, 0.5f));
        b.AddThemeStyleboxOverride("normal", ButtonBox(new Color(0.10f, 0.14f, 0.20f, 0.95f)));
        b.AddThemeStyleboxOverride("hover", ButtonBox(new Color(0.16f, 0.21f, 0.30f, 0.98f)));
        b.AddThemeStyleboxOverride("pressed", ButtonBox(new Color(0.23f, 0.19f, 0.08f, 0.98f)));
        b.AddThemeStyleboxOverride("disabled", ButtonBox(new Color(0.07f, 0.08f, 0.10f, 0.9f)));
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
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    public static string ThreatPips(int threat)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 5; i++) sb.Append(i < threat ? "●" : "○");
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

    // A shaded planet disc: fbm continents over a deep base, polar
    // caps, and a baked terminator (light from the upper left), so
    // the flat texture reads as a globe on the orbital screen.
    public static ImageTexture PlanetTexture(PlanetDef p, int size = 512, float seed = 0f)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var light = new Vector2(-0.55f, -0.62f).Normalized();
        for (int j = 0; j < size; j++)
        for (int i = 0; i < size; i++)
        {
            float u = (i / (float)(size - 1)) * 2f - 1f;
            float v = (j / (float)(size - 1)) * 2f - 1f;
            float r2 = u * u + v * v;
            if (r2 > 1f) { img.SetPixel(i, j, new Color(0, 0, 0, 0)); continue; }
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
            col = col.Lerp(new Color(0.95f, 0.97f, 1.0f), Mathf.Clamp((cloud - 0.60f) * 1.5f, 0f, 0.4f));
            // Sphere shading: diffuse from the light dir + limb darkening.
            float diff = Mathf.Clamp(new Vector2(u, v).Dot(light) * 0.5f + nz * 0.85f, 0.06f, 1.12f);
            float limb = 0.72f + 0.28f * nz;
            col = new Color(col.R * diff * limb, col.G * diff * limb, col.B * diff * limb, 1f);
            img.SetPixel(i, j, col);
        }
        return ImageTexture.CreateFromImage(img);
    }

    // Deep-space panorama for the flow backdrop: near-black gradient,
    // star field, two faint nebula washes.
    public static ImageTexture StarTexture(int w = 1024, int h = 512, ulong seed = 4242)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = seed };
        for (int j = 0; j < h; j++)
        for (int i = 0; i < w; i++)
        {
            float t = j / (float)h;
            var col = new Color(0.012f, 0.018f, 0.035f).Lerp(new Color(0.03f, 0.045f, 0.08f), 1f - t);
            float neb = Fbm(i * 0.006f, j * 0.006f, 3);
            if (neb > 0.58f) col = col.Lerp(new Color(0.10f, 0.13f, 0.22f), (neb - 0.58f) * 1.2f);
            float neb2 = Fbm(i * 0.004f + 31f, j * 0.004f, 3);
            if (neb2 > 0.62f) col = col.Lerp(new Color(0.16f, 0.09f, 0.05f), (neb2 - 0.62f) * 0.9f);
            img.SetPixel(i, j, col);
        }
        for (int s = 0; s < 1500; s++)
        {
            int x = rng.RandiRange(0, w - 1), y = rng.RandiRange(0, h - 1);
            float b = rng.RandfRange(0.35f, 1f);
            var tint = rng.Randf() < 0.12f ? new Color(1f, 0.85f, 0.65f) : new Color(0.85f, 0.92f, 1f);
            img.SetPixel(x, y, tint * b);
            if (b > 0.9f && x + 1 < w) img.SetPixel(x + 1, y, tint * b * 0.5f);
        }
        return ImageTexture.CreateFromImage(img);
    }
}
