using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace MeetingAssistant.Services;

public enum ThemeMode
{
    Ink,
    Harbor,
    Dusk,
    Paper,
    Moss,
    Amber,
    Dark,
    Light
}

public static class ThemeService
{
    private static readonly IReadOnlyDictionary<ThemeMode, IReadOnlyDictionary<string, string>> Palettes =
        new Dictionary<ThemeMode, IReadOnlyDictionary<string, string>>
        {
            [ThemeMode.Dark] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["InkBrush"] = "#202020",
                ["SidebarBrush"] = "#1F1F1F",
                ["PanelBrush"] = "#202020",
                ["CardBrush"] = "#2C2C2C",
                ["CardAltBrush"] = "#333333",
                ["LineBrush"] = "#3D3D3D",
                ["TextBrush"] = "#FFFFFF",
                ["MutedBrush"] = "#D6D6D6",
                ["SubtleBrush"] = "#9A9A9A",
                ["MintBrush"] = "#5EE0B5",
                ["MintDimBrush"] = "#1B3A32",
                ["BlueBrush"] = "#60CDFF",
                ["BlueDimBrush"] = "#1D3344",
                ["CoralBrush"] = "#FF99A4",
                ["CoralDimBrush"] = "#44282C",
                ["GoldBrush"] = "#FCE100",
                ["GoldDimBrush"] = "#3D3814",
                ["WindowChromeBrush"] = "#202020",
                ["WindowBorderBrush"] = "#2E2E2E",
                ["BrandInkBrush"] = "#0B1A14",
                ["HoverBrush"] = "#3A3A3A",
                ["HoverBorderBrush"] = "#4A4A4A",
                ["NavHoverBrush"] = "#2A2A2A",
                ["DangerBorderBrush"] = "#6B3A40",
                ["InputBrush"] = "#1A1A1A",
                ["PopupBorderBrush"] = "#454545",
                ["InputHoverBrush"] = "#262626",
                ["ProgressBrush"] = "#3A3A3A",
                ["ScrollThumbBrush"] = "#5A5A5A",
                ["ScrollTrackBrush"] = "#262626",
                ["BadgeBrush"] = "#3A3A3A",
                ["DividerBrush"] = "#333333",
                ["StatusCardBrush"] = "#262626",
                ["StatusCardBorderBrush"] = "#3A3A3A",
                ["SearchBrush"] = "#1A1A1A",
                ["HeaderBorderBrush"] = "#2E2E2E",
                ["SyncBadgeBrush"] = "#1D3344",
                ["HeroStartBrush"] = "#1A3330",
                ["HeroMiddleBrush"] = "#24302C",
                ["HeroEndBrush"] = "#2A2A2A",
                ["HeroStartColor"] = "#1A3330",
                ["HeroMiddleColor"] = "#24302C",
                ["HeroEndColor"] = "#2A2A2A",
                ["HeroBadgeBrush"] = "#1F4A40",
                ["HeroCopyBrush"] = "#D0D0D0",
                ["HeroRingBrush"] = "#3D8F78",
                ["HeroRingBrightBrush"] = "#5EE0B5",
                ["HeroCoreBrush"] = "#5EE0B5",
                ["HeroCoreInkBrush"] = "#0B1A14",
                ["HeroTagBrush"] = "#2A3338",
                ["HeroTagTextBrush"] = "#C8D4D8",
                ["HeroTagAltBrush"] = "#1F3A34",
                ["PrivacyCardBrush"] = "#24302C",
                ["PrivacyCopyBrush"] = "#D4E4DE",
                ["PrivacyDotBrush"] = "#12382C",
                ["SetupCardBrush"] = "#2A2A2A",
                ["SetupCardBorderBrush"] = "#3F3F3F",
                ["DetailCardBrush"] = "#2A2A2A",
                ["DetailCardBorderBrush"] = "#3F3F3F",
                ["TranscriptIconBrush"] = "#1F3A34",
                ["EditorTextBrush"] = "#E6E6E6",
                ["ImportantCardBrush"] = "#262626",
                ["ImportantCardBorderBrush"] = "#3A3A3A",
                ["SpeakerHintBrush"] = "#24302C",
                ["SpeakerHintBorderBrush"] = "#34584E",
                ["SpeakerProfileHintBrush"] = "#262626",
                ["SpeakerProfileHintBorderBrush"] = "#3A3A3A",
                ["ToastBrush"] = "#1F3A34",
                ["ToastBorderBrush"] = "#3D8F78"
            },
            [ThemeMode.Light] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["InkBrush"] = "#F3F3F3",
                ["SidebarBrush"] = "#F9F9F9",
                ["PanelBrush"] = "#F3F3F3",
                ["CardBrush"] = "#FFFFFF",
                ["CardAltBrush"] = "#F6F6F6",
                ["LineBrush"] = "#E5E5E5",
                ["TextBrush"] = "#1A1A1A",
                ["MutedBrush"] = "#5C5C5C",
                ["SubtleBrush"] = "#6E6E6E",
                ["MintBrush"] = "#0F6E56",
                ["MintDimBrush"] = "#D8F3EA",
                ["BlueBrush"] = "#0067C0",
                ["BlueDimBrush"] = "#E5F1FB",
                ["CoralBrush"] = "#C42B1C",
                ["CoralDimBrush"] = "#FDE7E9",
                ["GoldBrush"] = "#8A6A00",
                ["GoldDimBrush"] = "#FFF4CE",
                ["WindowChromeBrush"] = "#F3F3F3",
                ["WindowBorderBrush"] = "#E5E5E5",
                ["BrandInkBrush"] = "#FFFFFF",
                ["HoverBrush"] = "#EBEBEB",
                ["HoverBorderBrush"] = "#D0D0D0",
                ["NavHoverBrush"] = "#EFEFEF",
                ["DangerBorderBrush"] = "#E7B8B4",
                ["InputBrush"] = "#FFFFFF",
                ["PopupBorderBrush"] = "#D0D0D0",
                ["InputHoverBrush"] = "#F6F6F6",
                ["ProgressBrush"] = "#E5E5E5",
                ["ScrollThumbBrush"] = "#C4C4C4",
                ["ScrollTrackBrush"] = "#F3F3F3",
                ["BadgeBrush"] = "#EBEBEB",
                ["DividerBrush"] = "#E5E5E5",
                ["StatusCardBrush"] = "#FFFFFF",
                ["StatusCardBorderBrush"] = "#E5E5E5",
                ["SearchBrush"] = "#FFFFFF",
                ["HeaderBorderBrush"] = "#E5E5E5",
                ["SyncBadgeBrush"] = "#E5F1FB",
                ["HeroStartBrush"] = "#E5F6F1",
                ["HeroMiddleBrush"] = "#F3F3F3",
                ["HeroEndBrush"] = "#FFFFFF",
                ["HeroStartColor"] = "#E5F6F1",
                ["HeroMiddleColor"] = "#F3F3F3",
                ["HeroEndColor"] = "#FFFFFF",
                ["HeroBadgeBrush"] = "#D8F3EA",
                ["HeroCopyBrush"] = "#3B3B3B",
                ["HeroRingBrush"] = "#8FCBB8",
                ["HeroRingBrightBrush"] = "#0F6E56",
                ["HeroCoreBrush"] = "#0F6E56",
                ["HeroCoreInkBrush"] = "#FFFFFF",
                ["HeroTagBrush"] = "#E5F1FB",
                ["HeroTagTextBrush"] = "#1A1A1A",
                ["HeroTagAltBrush"] = "#D8F3EA",
                ["PrivacyCardBrush"] = "#F4FBF8",
                ["PrivacyCopyBrush"] = "#1A1A1A",
                ["PrivacyDotBrush"] = "#0B3D2E",
                ["SetupCardBrush"] = "#FFFFFF",
                ["SetupCardBorderBrush"] = "#E5E5E5",
                ["DetailCardBrush"] = "#FFFFFF",
                ["DetailCardBorderBrush"] = "#E5E5E5",
                ["TranscriptIconBrush"] = "#D8F3EA",
                ["EditorTextBrush"] = "#1A1A1A",
                ["ImportantCardBrush"] = "#FFFFFF",
                ["ImportantCardBorderBrush"] = "#E5E5E5",
                ["SpeakerHintBrush"] = "#F4FBF8",
                ["SpeakerHintBorderBrush"] = "#B7E0D2",
                ["SpeakerProfileHintBrush"] = "#FFFFFF",
                ["SpeakerProfileHintBorderBrush"] = "#E5E5E5",
                ["ToastBrush"] = "#D8F3EA",
                ["ToastBorderBrush"] = "#8FCBB8"
            }
        };

    public static ThemeMode CurrentMode { get; private set; } = ThemeMode.Ink;

    private static readonly IReadOnlyDictionary<ThemeMode, IReadOnlyDictionary<string, string>> Adjustments =
        new Dictionary<ThemeMode, IReadOnlyDictionary<string, string>>
        {
            [ThemeMode.Harbor] = new Dictionary<string, string>
            {
                ["InkBrush"] = "#071018", ["SidebarBrush"] = "#0C1824", ["PanelBrush"] = "#102033",
                ["CardBrush"] = "#16304A", ["MintBrush"] = "#79C7FF", ["MintDimBrush"] = "#12344A",
                ["BlueBrush"] = "#9EB6FF", ["WindowChromeBrush"] = "#071018"
            },
            [ThemeMode.Dusk] = new Dictionary<string, string>
            {
                ["InkBrush"] = "#120E18", ["SidebarBrush"] = "#1A1424", ["PanelBrush"] = "#221A30",
                ["CardBrush"] = "#2C2140", ["MintBrush"] = "#D7A6FF", ["MintDimBrush"] = "#3A2750",
                ["BlueBrush"] = "#F0B7C8", ["WindowChromeBrush"] = "#120E18"
            },
            [ThemeMode.Paper] = new Dictionary<string, string>
            {
                ["InkBrush"] = "#F3F1EC", ["SidebarBrush"] = "#FFFcf7", ["PanelBrush"] = "#F7F4EE",
                ["CardBrush"] = "#FFFFFF", ["TextBrush"] = "#1C1915", ["MutedBrush"] = "#5C564C",
                ["MintBrush"] = "#0F766E", ["MintDimBrush"] = "#D7F3EF", ["LineBrush"] = "#E4DDD2",
                ["InputBrush"] = "#FFFFFF", ["WindowChromeBrush"] = "#FFFcf7"
            },
            [ThemeMode.Moss] = new Dictionary<string, string>
            {
                ["InkBrush"] = "#F2F6F1", ["SidebarBrush"] = "#FFFFFF", ["PanelBrush"] = "#F5F8F4",
                ["CardBrush"] = "#FFFFFF", ["TextBrush"] = "#17211A", ["MutedBrush"] = "#4E6254",
                ["MintBrush"] = "#1F7A45", ["MintDimBrush"] = "#DDF3E4", ["LineBrush"] = "#D5E3D8",
                ["WindowChromeBrush"] = "#FFFFFF", ["BlueBrush"] = "#2F6F62"
            },
            [ThemeMode.Amber] = new Dictionary<string, string>
            {
                ["InkBrush"] = "#FBF6EE", ["SidebarBrush"] = "#FFF9F2", ["PanelBrush"] = "#F8F1E6",
                ["CardBrush"] = "#FFFFFF", ["TextBrush"] = "#2A2118", ["MutedBrush"] = "#6B5A48",
                ["MintBrush"] = "#B45309", ["MintDimBrush"] = "#FDE7C7", ["LineBrush"] = "#EADDCB",
                ["WindowChromeBrush"] = "#FFF9F2", ["BlueBrush"] = "#9A3412"
            }
        };

    public static void Apply(ThemeMode mode)
    {
        CurrentMode = mode;
        var resources = WpfApplication.Current?.Resources;
        if (resources is null) return;

        var basis = mode is ThemeMode.Paper or ThemeMode.Moss or ThemeMode.Amber or ThemeMode.Light
            ? ThemeMode.Light
            : ThemeMode.Dark;
        var colors = new Dictionary<string, string>(Palettes[basis], StringComparer.OrdinalIgnoreCase);
        if (Adjustments.TryGetValue(mode, out var adjustments))
        {
            foreach (var (key, hex) in adjustments)
                colors[key] = hex;
        }

        foreach (var (key, hex) in colors)
        {
            var color = (WpfColor)WpfColorConverter.ConvertFromString(hex)!;
            if (key.EndsWith("Color", StringComparison.Ordinal))
            {
                resources[key] = color;
            }
            else if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = color;
            }
            else
            {
                resources[key] = new SolidColorBrush(color);
            }
        }
    }

    public static ThemeMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ThemeMode.Ink;
        if (Enum.TryParse<ThemeMode>(value, ignoreCase: true, out var mode) && mode is not (ThemeMode.Dark or ThemeMode.Light))
            return mode;
        return value.ToLowerInvariant() switch
        {
            "light" or "paper" => ThemeMode.Paper,
            "harbor" => ThemeMode.Harbor,
            "dusk" => ThemeMode.Dusk,
            "moss" => ThemeMode.Moss,
            "amber" => ThemeMode.Amber,
            _ => ThemeMode.Ink
        };
    }
}
