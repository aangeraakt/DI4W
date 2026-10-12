using CommunityToolkit.WinUI.Controls;
using DynamicIsland.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DynamicIsland.SettingsApp;

/// <summary>
/// Translates a page after it's built: walks its controls (including ones not on screen yet, such as collapsed
/// expanders and closed drop-downs) and swaps English text that has a translation in <see cref="L"/>.
/// Text set later in code goes through L.T/L.F directly.
/// </summary>
public static class Translator
{
    public static void Apply(object? root)
    {
        if (!L.IsDutch || root is null) return;
        Walk(root, 0);
    }

    private static void Walk(object? node, int depth)
    {
        if (node is null || depth > 60) return;

        switch (node)
        {
            case TextBlock t:
                t.Text = Tr(t.Text);
                break;
            case SettingsExpander e:
                e.Header = TrObject(e.Header);
                e.Description = TrObject(e.Description);
                Walk(e.Header, depth + 1);
                Walk(e.Description, depth + 1);
                Walk(e.Content, depth + 1);
                Walk(e.ItemsHeader, depth + 1);
                foreach (var item in e.Items) Walk(item, depth + 1);
                break;
            case SettingsCard c:
                c.Header = TrObject(c.Header);
                c.Description = TrObject(c.Description);
                Walk(c.Header, depth + 1);
                Walk(c.Description, depth + 1);
                Walk(c.Content, depth + 1);
                break;
            case InfoBar i:
                i.Title = Tr(i.Title);
                i.Message = Tr(i.Message);
                Walk(i.ActionButton, depth + 1);
                Walk(i.Content, depth + 1);
                break;
            case NavigationView n:
                foreach (var item in n.MenuItems.Concat(n.FooterMenuItems)) Walk(item, depth + 1);
                Walk(n.Content, depth + 1);
                break;
            case TextBox box:
                box.PlaceholderText = Tr(box.PlaceholderText);
                box.Header = TrObject(box.Header);
                break;
            case ToggleSwitch toggle:
                toggle.OnContent = L.T("On");
                toggle.OffContent = L.T("Off");
                break;
            case ComboBox combo:
                foreach (var item in combo.Items) Walk(item, depth + 1);
                break;
            case ContentControl cc: // buttons, nav items, combo items, hyperlinks
                cc.Content = TrObject(cc.Content);
                Walk(cc.Content, depth + 1);
                break;
            case Panel panel:
                foreach (var child in panel.Children) Walk(child, depth + 1);
                break;
            case Border border:
                Walk(border.Child, depth + 1);
                break;
            case UserControl uc: // pages too (ScrollViewer is handled as a ContentControl above)
                Walk(uc.Content, depth + 1);
                break;
        }

        if (node is DependencyObject d && ToolTipService.GetToolTip(d) is string tip)
        {
            ToolTipService.SetToolTip(d, Tr(tip));
        }
    }

    private static string Tr(string text) => string.IsNullOrEmpty(text) ? text : L.Find(text) ?? text;

    private static object TrObject(object? value) => value is string s ? Tr(s) : value!;
}
