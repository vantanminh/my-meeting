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
                ["InkBrush"] = "#0B0F17",
                ["SidebarBrush"] = "#0E1420",
                ["PanelBrush"] = "#121A28",
                ["CardBrush"] = "#182235",
                ["CardAltBrush"] = "#1C2940",
                ["LineBrush"] = "#29374D",
                ["TextBrush"] = "#F3F7FC",
                ["MutedBrush"] = "#91A0B5",
                ["SubtleBrush"] = "#65748A",
                ["MintBrush"] = "#66E3C0",
                ["MintDimBrush"] = "#193D3A",
                ["BlueBrush"] = "#88A9FF",
                ["BlueDimBrush"] = "#27365B",
                ["CoralBrush"] = "#FF977E",
                ["CoralDimBrush"] = "#492C32",
                ["GoldBrush"] = "#F3C878",
                ["GoldDimBrush"] = "#453922",
                ["WindowChromeBrush"] = "#0A0F18",
                ["WindowBorderBrush"] = "#182333",
                ["BrandInkBrush"] = "#081A15",
                ["HoverBrush"] = "#24334B",
                ["HoverBorderBrush"] = "#3A506F",
                ["NavHoverBrush"] = "#19283B",
                ["DangerBorderBrush"] = "#73444B",
                ["InputBrush"] = "#0F1725",
                ["PopupBorderBrush"] = "#3A4B68",
                ["InputHoverBrush"] = "#152238",
                ["ProgressBrush"] = "#26344A",
                ["ScrollThumbBrush"] = "#3A4F70",
                ["ScrollTrackBrush"] = "#0D1522",
                ["BadgeBrush"] = "#293A55",
                ["DividerBrush"] = "#1F2C3F",
                ["StatusCardBrush"] = "#121C2B",
                ["StatusCardBorderBrush"] = "#1E2D43",
                ["SearchBrush"] = "#0E1623",
                ["HeaderBorderBrush"] = "#202D40",
                ["SyncBadgeBrush"] = "#17263A",
                ["HeroStartBrush"] = "#173A3A",
                ["HeroMiddleBrush"] = "#192B3E",
                ["HeroEndBrush"] = "#1C2334",
                ["HeroStartColor"] = "#173A3A",
                ["HeroMiddleColor"] = "#192B3E",
                ["HeroEndColor"] = "#1C2334",
                ["HeroBadgeBrush"] = "#23514B",
                ["HeroCopyBrush"] = "#B3C4D0",
                ["HeroRingBrush"] = "#3AA999",
                ["HeroRingBrightBrush"] = "#65DFBF",
                ["HeroCoreBrush"] = "#64DDBD",
                ["HeroCoreInkBrush"] = "#0D3D35",
                ["HeroTagBrush"] = "#213B51",
                ["HeroTagTextBrush"] = "#A6C7D4",
                ["HeroTagAltBrush"] = "#284649",
                ["PrivacyCardBrush"] = "#14222D",
                ["PrivacyCopyBrush"] = "#C0D1D5",
                ["PrivacyDotBrush"] = "#123C31",
                ["SetupCardBrush"] = "#18263B",
                ["SetupCardBorderBrush"] = "#2B4163",
                ["DetailCardBrush"] = "#1B2C3E",
                ["DetailCardBorderBrush"] = "#2A4962",
                ["TranscriptIconBrush"] = "#24404A",
                ["EditorTextBrush"] = "#C3CDDA",
                ["ImportantCardBrush"] = "#172033",
                ["ImportantCardBorderBrush"] = "#2D3F5A",
                ["SpeakerHintBrush"] = "#14232E",
                ["SpeakerHintBorderBrush"] = "#2B4D51",
                ["SpeakerProfileHintBrush"] = "#111A29",
                ["SpeakerProfileHintBorderBrush"] = "#263750",
                ["ToastBrush"] = "#1D3434",
                ["ToastBorderBrush"] = "#306462"
            },
            [ThemeMode.Light] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["InkBrush"] = "#F4F7FB",
                ["SidebarBrush"] = "#FFFFFF",
                ["PanelBrush"] = "#F7F9FD",
                ["CardBrush"] = "#FFFFFF",
                ["CardAltBrush"] = "#F0F4FA",
                ["LineBrush"] = "#D7E0EC",
                ["TextBrush"] = "#142033",
                ["MutedBrush"] = "#52627A",
                ["SubtleBrush"] = "#71809A",
                ["MintBrush"] = "#0B806A",
                ["MintDimBrush"] = "#DDF7F1",
                ["BlueBrush"] = "#3568C9",
                ["BlueDimBrush"] = "#E5ECFF",
                ["CoralBrush"] = "#C34E3A",
                ["CoralDimBrush"] = "#FDE9E5",
                ["GoldBrush"] = "#A56A00",
                ["GoldDimBrush"] = "#FFF3D4",
                ["WindowChromeBrush"] = "#FFFFFF",
                ["WindowBorderBrush"] = "#D7E0EC",
                ["BrandInkBrush"] = "#FFFFFF",
                ["HoverBrush"] = "#EAF0FA",
                ["HoverBorderBrush"] = "#B8C8E2",
                ["NavHoverBrush"] = "#EEF3FB",
                ["DangerBorderBrush"] = "#E0A69B",
                ["InputBrush"] = "#FFFFFF",
                ["PopupBorderBrush"] = "#B8C8E2",
                ["InputHoverBrush"] = "#F4F7FC",
                ["ProgressBrush"] = "#DCE5F2",
                ["ScrollThumbBrush"] = "#B8C8E2",
                ["ScrollTrackBrush"] = "#EEF2F8",
                ["BadgeBrush"] = "#E7EEF9",
                ["DividerBrush"] = "#E4EAF2",
                ["StatusCardBrush"] = "#F1F6FB",
                ["StatusCardBorderBrush"] = "#DAE4F0",
                ["SearchBrush"] = "#FFFFFF",
                ["HeaderBorderBrush"] = "#DDE5EF",
                ["SyncBadgeBrush"] = "#E5ECFF",
                ["HeroStartBrush"] = "#E1F7F2",
                ["HeroMiddleBrush"] = "#EAF1FC",
                ["HeroEndBrush"] = "#F1F3FA",
                ["HeroStartColor"] = "#E1F7F2",
                ["HeroMiddleColor"] = "#EAF1FC",
                ["HeroEndColor"] = "#F1F3FA",
                ["HeroBadgeBrush"] = "#C8EFE6",
                ["HeroCopyBrush"] = "#486174",
                ["HeroRingBrush"] = "#61B8A8",
                ["HeroRingBrightBrush"] = "#4BAA98",
                ["HeroCoreBrush"] = "#0B806A",
                ["HeroCoreInkBrush"] = "#FFFFFF",
                ["HeroTagBrush"] = "#DDEBFA",
                ["HeroTagTextBrush"] = "#41677C",
                ["HeroTagAltBrush"] = "#D4EFEA",
                ["PrivacyCardBrush"] = "#EEF7F5",
                ["PrivacyCopyBrush"] = "#4A626D",
                ["PrivacyDotBrush"] = "#BCE8D9",
                ["SetupCardBrush"] = "#EDF3FC",
                ["SetupCardBorderBrush"] = "#C7D8F1",
                ["DetailCardBrush"] = "#EDF5FA",
                ["DetailCardBorderBrush"] = "#C5DCE8",
                ["TranscriptIconBrush"] = "#D4EBEC",
                ["EditorTextBrush"] = "#40516A",
                ["ImportantCardBrush"] = "#F0F3FB",
                ["ImportantCardBorderBrush"] = "#D4DDED",
                ["SpeakerHintBrush"] = "#EEF7F6",
                ["SpeakerHintBorderBrush"] = "#C7E5E0",
                ["SpeakerProfileHintBrush"] = "#F1F5FB",
                ["SpeakerProfileHintBorderBrush"] = "#D5DFED",
                ["ToastBrush"] = "#E2F5EF",
                ["ToastBorderBrush"] = "#9DD6C6"
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
