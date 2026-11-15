using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Execution;

/// <summary>
/// The Execute row's choices, drawn as the game draws its right-click menu (the same panel, title and buttons, at the same spot), in the HUD's
/// overlay slot: Kill, or a limb or set of limbs to cut off. A row whose limbs are all gone (a stump or a prosthetic) is greyed. A click outside the
/// menu closes it. Test build: Kill gives the execution order; a limb row only writes its choice to the game log.
/// </summary>
sealed class MaimMenu : IUiExtension
{
    /// <summary>
    /// The rows: caption and the limb slots it cuts (bit k for limb slot k: 0 left arm, 1 right arm, 2 left leg, 3 right leg; none for Kill).
    /// </summary>
    internal static readonly (string Caption, int Limbs)[] Rows =
    [
        ("Kill", 0), ("Left Arm", 1), ("Right Arm", 2), ("Both Arms", 3), ("Left Leg", 4), ("Right Leg", 8), ("Both Legs", 12), ("All Limbs", 15),
    ];

    /// <summary>
    /// The right-click menu's text colour and hovered text colour.
    /// </summary>
    static readonly Vector4 Ink = new(0.86f, 0.82f, 0.71f, 1), Hover = new(0.86f, 0.62f, 0.2f, 1);

    /// <summary>
    /// An open menu: who was selected, the body, its name, the limbs it still has (bits as in <see cref="Rows"/>) and where the right-click menu was.
    /// </summary>
    sealed record Open(IReadOnlyList<CharacterId> Selection, CharacterId Target, string Title, int Present, Vector2 At);

    /// <summary>
    /// The open menu, null when closed. Set and read on the interface's thread.
    /// </summary>
    static Open? s_open;

    /// <summary>
    /// The extension's id (its registrations' prefix).
    /// </summary>
    public string Id => "execution";

    /// <summary>
    /// Opens the menu for <paramref name="target"/>, in place of the right-click menu that was at <paramref name="at"/>.
    /// </summary>
    internal static void Show(IReadOnlyList<CharacterId> selection, CharacterSnapshot target, Vector2 at) =>
        s_open = new(selection, target.Id, target.Name, Present(target.LimbBits), at);

    /// <summary>
    /// The limbs that can still be cut off: each limb slot whose state (2 bits of <paramref name="limbBits"/>) is the original (0) or crushed (3),
    /// not a stump (1) or a prosthetic (2).
    /// </summary>
    static int Present(int limbBits)
    {
        int present = 0;
        for (int k = 0; k < 4; k++)
            if (((limbBits >> (2 * k)) & 3) is 0 or 3) present |= 1 << k;
        return present;
    }

    /// <summary>
    /// Registers the styles, the two actions (a row picked, a click outside) and the menu's contribution to the overlay.
    /// </summary>
    public void Register(UiRegistration registration)
    {
        string panel = registration.RegisterStyle("panel", new UiStyle
        {
            Skin = "Kenshi_FloatingPanelSkin", Background = new(0.1f, 0.09f, 0.08f, 0.94f), Text = Ink,
        });
        string title = registration.RegisterStyle("title", new UiStyle { Text = Ink, TextAlign = "Center", FontName = "Kenshi_StandardFont_Medium" });
        string button = registration.RegisterStyle("button", new UiStyle
        {
            Skin = "Kenshi_Button1Skin", Text = Ink, Hover = Hover, Disabled = new(0.42f, 0.4f, 0.37f, 1), TextAlign = "Center",
            FontName = "Kenshi_PaintedTextFont_Medium",
        });
        string backdrop = registration.RegisterStyle("backdrop", new UiStyle());
        string pick = registration.RegisterAction("pick", c => Pick(c.Argument));
        string close = registration.RegisterAction("close", _ => s_open = null);
        string backdropId = registration.Key("backdrop"), panelId = registration.Key("menu"), titleId = registration.Key("title");
        string[] rowIds = [.. Rows.Select((_, i) => registration.Key($"row-{i}"))];

        registration.Contribute(UiSlots.Overlay, frame =>
        {
            if (s_open is not { } open) return new UiNode(backdropId) { Visible = false };

            // Kenshi_ContextMenu, as the game lays its right-click menu out (ContextMenuWindow.AddContextMenu): an eighth of the screen wide, the
            // title 47.3 % of a 0.0685-screen row pair, one option per row, beside the cursor and kept on screen.
            var view = frame.Viewport;
            float w = MathF.Max(200, view.Width * 0.125f), full = MathF.Max(64, view.Height * 0.0685185f);
            float titleH = full * 0.472973f, optionH = full - titleH, h = titleH + optionH * Rows.Length + 6;
            float x = Math.Clamp(open.At.X + 12, 0, Math.Max(0, view.Width - w)), y = Math.Clamp(open.At.Y, 0, Math.Max(0, view.Height - h));

            var menu = new UiNode(panelId) { Placement = new(w, h, x, y), Style = panel };
            menu.Children.Add(new UiNode(titleId, UiNodeKind.Label) { Placement = new(w - 12, titleH, 6, 2), Style = title, Text = open.Title });
            for (int i = 0; i < Rows.Length; i++)
            {
                var (caption, limbs) = Rows[i];
                menu.Children.Add(new UiNode(rowIds[i], UiNodeKind.Button)
                {
                    Placement = new(w * (1 - 2 * 0.0208333f), optionH * 0.897436f, w * 0.0208333f, titleH + i * optionH + optionH * 0.0512821f),
                    Style = button,
                    Text = caption,
                    Action = pick,
                    Argument = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Enabled = limbs == 0 || (limbs & open.Present) != 0,
                });
            }

            // A clear panel over the whole screen under the menu: a click anywhere else closes it, as a click outside the right-click menu does.
            var root = new UiNode(backdropId) { Style = backdrop, Action = close };
            root.Children.Add(menu);
            return root;
        }, order: 1000);
    }

    /// <summary>
    /// A row picked: the menu closes; Kill gives the execution order, a limb row (test build) writes its choice to the log.
    /// </summary>
    static void Pick(string argument)
    {
        var open = s_open;
        s_open = null;
        if (open is null || !int.TryParse(argument, out int i) || i < 0 || i >= Rows.Length) return;
        if (ExecutionMod.Context is not { } context) return;

        if (Rows[i].Limbs == 0)
            context.Enqueue(new ExecuteOrder(open.Selection, open.Target) { Tick = context.Tick });
        else
            context.Log($"maim menu: {Rows[i].Caption} picked for {open.Title} (limbs {Rows[i].Limbs & open.Present}); not built yet");
    }
}
