using System.Numerics;
using ImGuiNET;
using Silk.NET.Core;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using StbImageSharp;
using System.Runtime.InteropServices;

namespace OwlMerger.App.Backend;

public sealed class Renderer
{
    private record Mod(string Name, string Description, string? Thumb);

    private static readonly Mod[] Mods =
    [
        new("Navi Re-colours", "A fresh coat of.. Paint..?", "thumbnailfornavi.png"),
        new("Hero of Time Retexture", "A high-resolution retexture of Link's tunic, cap and gear that stays true to the original look.", "HOTR.jpg"),
        new("Retro Sound Pack", "Swaps effects and music for classic chiptune-style sounds.", null),
    ];

    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private ImGuiController _imgui = null!;
    private int _selected;

    public void Run()
    {
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1000, 640),
            Title = "OwlMerger",
        };
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.FramebufferResize += size => _gl.Viewport(size);
        _window.Closing += () => _imgui.Dispose();
        _window.Run();
    }

    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        _input = _window.CreateInput();
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var fontPath = Path.Combine(assets, "HyliaSerifBeta-Regular.otf");
        _imgui = new ImGuiController(_gl, _window, _input, () =>
        {
            var fonts = ImGui.GetIO().Fonts;
            var body = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "bahnschrift.ttf");
            if (File.Exists(body)) fonts.AddFontFromFileTTF(body, 17f);
            else fonts.AddFontDefault();
            if (File.Exists(fontPath))
            {
                _titleFont = fonts.AddFontFromFileTTF(fontPath, 40f);
                _headingFont = fonts.AddFontFromFileTTF(fontPath, 28f);
                _hasTitleFont = true;
            }
        });
        ApplyDarkTheme();
        _logo = LoadTexture(Path.Combine(assets, "Hylian_Symbol.png"), out _logoSize);
        _defaultThumb = LoadTexture(Path.Combine(assets, "DefaultThumb.png"), out _defaultThumbSize);
        foreach (var file in Mods.Select(m => m.Thumb).OfType<string>().Distinct())
        {
            var id = LoadTexture(Path.Combine(assets, file), out var size);
            if (id != 0) _thumbs[file] = (id, size);
        }
        SetWindowIcon(Path.Combine(assets, "Icon.png"));
        ApplyTitleBar();
    }

    private uint _logo;
    private Vector2 _logoSize;
    private uint _defaultThumb;
    private Vector2 _defaultThumbSize;
    private readonly Dictionary<string, (uint Id, Vector2 Size)> _thumbs = new();

    private (uint Id, Vector2 Size) ThumbFor(Mod mod) =>
        mod.Thumb is { } file && _thumbs.TryGetValue(file, out var t) ? t : (_defaultThumb, _defaultThumbSize);
    private ImFontPtr _titleFont;
    private ImFontPtr _headingFont;
    private bool _hasTitleFont;

    private void SetWindowIcon(string path)
    {
        if (!File.Exists(path)) return;
        var img = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
        ReadOnlySpan<RawImage> icons = [new RawImage(img.Width, img.Height, img.Data)];
        _window.SetWindowIcon(icons);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void ApplyTitleBar()
    {
        if (!OperatingSystem.IsWindows() || _window.Native?.Win32 is not { } win) return;
        var hwnd = win.Hwnd;
        int dark = 1, black = 0x000000, white = 0xFFFFFF;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 35, ref black, sizeof(int));
        DwmSetWindowAttribute(hwnd, 36, ref white, sizeof(int));
    }

    private unsafe uint LoadTexture(string path, out Vector2 size)
    {
        size = default;
        if (!File.Exists(path)) return 0;
        var img = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
        var pixels = img.Data;
        size = new Vector2(img.Width, img.Height);

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        fixed (byte* p = pixels)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)img.Width, (uint)img.Height,
                0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        return tex;
    }

    private void OnRender(double dt)
    {
        _imgui.Update((float)dt);
        _gl.ClearColor(0.07f, 0.07f, 0.09f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        DrawUi();

        _imgui.Render();
    }

    private void DrawUi()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.WorkPos);
        ImGui.SetNextWindowSize(vp.WorkSize);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                                       ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;
        ImGui.Begin("Root", flags);

        float x = 0f;
        const float headerHeight = 48f;
        if (_logo != 0)
        {
            float h = 40f, w = h * _logoSize.X / _logoSize.Y;
            var start = ImGui.GetCursorPos();
            ImGui.SetCursorPos(start + new Vector2(0, (headerHeight - h) / 2f));
            ImGui.Image((IntPtr)_logo, new Vector2(w, h));
            ImGui.SetCursorPos(start);
            x = w + 14f;
        }
        if (_hasTitleFont) ImGui.PushFont(_titleFont);
        var top = ImGui.GetCursorPos();
        ImGui.SetCursorPos(top + new Vector2(x, (headerHeight - ImGui.GetTextLineHeight()) / 2f));
        ImGui.TextColored(new Vector4(1, 1, 1, 1), "OwlMerger");
        if (_hasTitleFont) ImGui.PopFont();
        ImGui.SetCursorPos(top + new Vector2(0, headerHeight));
        ImGui.Separator();
        ImGui.Spacing();

        float panelWidth = 420f;
        float detailWidth = ImGui.GetContentRegionAvail().X - panelWidth - ImGui.GetStyle().ItemSpacing.X;
        ImGui.BeginChild("Detail", new Vector2(detailWidth, 0), ImGuiChildFlags.AlwaysUseWindowPadding);
        DrawDetail();
        ImGui.EndChild();
        ImGui.SameLine();

        ImGui.BeginChild("Mods", new Vector2(panelWidth, 0), ImGuiChildFlags.Border);
        if (_hasTitleFont) ImGui.PushFont(_headingFont);
        ImGui.TextColored(new Vector4(1, 1, 1, 1), "Mods");
        if (_hasTitleFont) ImGui.PopFont();
        ImGui.Spacing();

        for (int i = 0; i < Mods.Length; i++)
            DrawModRow(i);

        ImGui.EndChild();
        ImGui.End();
    }

    private void DrawDetail()
    {
        var mod = Mods[_selected];
        var (id, size) = ThumbFor(mod);
        if (id != 0)
        {
            float w = Math.Min(ImGui.GetContentRegionAvail().X, 560f);
            float h = w * size.Y / size.X;
            var min = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddImageRounded((IntPtr)id, min, min + new Vector2(w, h),
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, 4f);
            ImGui.Dummy(new Vector2(w, h));
        }
        ImGui.Spacing();
        if (_hasTitleFont) ImGui.PushFont(_headingFont);
        ImGui.TextColored(new Vector4(1, 1, 1, 1), mod.Name);
        if (_hasTitleFont) ImGui.PopFont();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(new Vector4(0.70f, 0.71f, 0.76f, 1f), mod.Description);
        ImGui.PopTextWrapPos();
    }

    private void DrawModRow(int index)
    {
        const float rowHeight = 56f, pad = 8f, thumbHeight = 40f;
        var draw = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(ImGui.GetContentRegionAvail().X, rowHeight);

        bool hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max);
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) _selected = index;

        if (_selected == index)
        {
            draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f)), 5f);
            draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)), 5f);
        }
        else if (hovered)
        {
            draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), 5f);
        }

        float textX = pad;
        var (thumbId, thumbSize) = ThumbFor(Mods[index]);
        if (thumbId != 0)
        {
            float w = thumbHeight * thumbSize.X / thumbSize.Y;
            var tMin = min + new Vector2(pad, (rowHeight - thumbHeight) / 2f);
            draw.AddImageRounded((IntPtr)thumbId, tMin, tMin + new Vector2(w, thumbHeight),
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, 3f);
            textX += w + 12f;
        }

        ImGui.SetCursorScreenPos(min + new Vector2(textX, (rowHeight - ImGui.GetTextLineHeight()) / 2f));
        ImGui.TextUnformatted(Mods[index].Name);

        ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y + 6f));
        ImGui.Dummy(Vector2.Zero);
    }

    private static void ApplyDarkTheme()
    {
        var s = ImGui.GetStyle();
        s.WindowRounding = 0f;
        s.ChildRounding = 4f;
        s.FrameRounding = 3f;
        s.PopupRounding = 3f;
        s.WindowPadding = new Vector2(18, 14);
        s.ItemSpacing = new Vector2(10, 8);
        s.CellPadding = new Vector2(10, 6);

        var c = s.Colors;
        c[(int)ImGuiCol.WindowBg] = new Vector4(0.07f, 0.07f, 0.09f, 1f);
        c[(int)ImGuiCol.ChildBg] = new Vector4(0.10f, 0.10f, 0.13f, 1f);
        c[(int)ImGuiCol.Border] = new Vector4(0.18f, 0.18f, 0.23f, 1f);
        c[(int)ImGuiCol.Separator] = new Vector4(0.18f, 0.18f, 0.23f, 1f);
        c[(int)ImGuiCol.Text] = new Vector4(0.90f, 0.91f, 0.95f, 1f);
        c[(int)ImGuiCol.TextDisabled] = new Vector4(0.52f, 0.54f, 0.62f, 1f);
        c[(int)ImGuiCol.Header] = new Vector4(1f, 1f, 1f, 0.12f);
        c[(int)ImGuiCol.HeaderHovered] = new Vector4(1f, 1f, 1f, 0.06f);
        c[(int)ImGuiCol.HeaderActive] = new Vector4(1f, 1f, 1f, 0.18f);
        c[(int)ImGuiCol.TableRowBg] = new Vector4(0f, 0f, 0f, 0f);
        c[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1f, 1f, 1f, 0.03f);
    }
}
