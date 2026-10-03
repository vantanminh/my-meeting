using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace MeetingAssistant.Services;

public enum ThemeMode
{
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
                ["InkBrush"] = "#0E1311",
                ["SidebarBrush"] = "#121816",
                ["PanelBrush"] = "#0E1311",
                ["CardBrush"] = "#18211D",
                ["CardAltBrush"] = "#202A25",
                ["LineBrush"] = "#2C3933",
                ["TextBrush"] = "#F4F7F5",
                ["MutedBrush"] = "#C3CFC8",
                ["SubtleBrush"] = "#93A29A",
                ["MintBrush"] = "#5EE0B5",
                ["MintDimBrush"] = "#14362C",
                ["BlueBrush"] = "#8EBEFF",
                ["BlueDimBrush"] = "#17283A",
                ["CoralBrush"] = "#FF9AAB",
                ["CoralDimBrush"] = "#3E2429",
                ["GoldBrush"] = "#E7C56A",
                ["GoldDimBrush"] = "#3A3216",
                ["WindowChromeBrush"] = "#0E1311",
                ["WindowBorderBrush"] = "#24302A",
                ["BrandInkBrush"] = "#062117",
                ["HoverBrush"] = "#24302A",
                ["HoverBorderBrush"] = "#3E5248",
                ["NavHoverBrush"] = "#1C2621",
                ["DangerBorderBrush"] = "#7A3D45",
                ["InputBrush"] = "#101614",
                ["PopupBorderBrush"] = "#3E5248",
                ["InputHoverBrush"] = "#1A2420",
                ["ProgressBrush"] = "#24302A",
                ["ScrollThumbBrush"] = "#4E6158",
                ["ScrollTrackBrush"] = "#101614",
                ["BadgeBrush"] = "#24302A",
                ["DividerBrush"] = "#24302A",
                ["StatusCardBrush"] = "#18211D",
                ["StatusCardBorderBrush"] = "#2C3933",
                ["SearchBrush"] = "#101614",
                ["HeaderBorderBrush"] = "#24302A",
                ["SyncBadgeBrush"] = "#14362C",
                ["HeroStartBrush"] = "#12352C",
                ["HeroMiddleBrush"] = "#16241F",
                ["HeroEndBrush"] = "#1A2420",
                ["HeroStartColor"] = "#12352C",
                ["HeroMiddleColor"] = "#16241F",
                ["HeroEndColor"] = "#1A2420",
                ["HeroBadgeBrush"] = "#1A463A",
                ["HeroCopyBrush"] = "#D5E3DC",
                ["HeroRingBrush"] = "#3D8F78",
                ["HeroRingBrightBrush"] = "#5EE0B5",
                ["HeroCoreBrush"] = "#5EE0B5",
                ["HeroCoreInkBrush"] = "#062117",
                ["HeroTagBrush"] = "#1A2830",
                ["HeroTagTextBrush"] = "#D5E4EC",
                ["HeroTagAltBrush"] = "#1A463A",
                ["PrivacyCardBrush"] = "#14241F",
                ["PrivacyCopyBrush"] = "#D7E8E0",
                ["PrivacyDotBrush"] = "#062117",
                ["SetupCardBrush"] = "#18211D",
                ["SetupCardBorderBrush"] = "#2C3933",
                ["DetailCardBrush"] = "#18211D",
                ["DetailCardBorderBrush"] = "#2C3933",
                ["TranscriptIconBrush"] = "#14362C",
                ["EditorTextBrush"] = "#E7EEEA",
                ["ImportantCardBrush"] = "#18211D",
                ["ImportantCardBorderBrush"] = "#2C3933",
                ["SpeakerHintBrush"] = "#14241F",
                ["SpeakerHintBorderBrush"] = "#2F5A4C",
                ["SpeakerProfileHintBrush"] = "#18211D",
                ["SpeakerProfileHintBorderBrush"] = "#2C3933",
                ["ToastBrush"] = "#14362C",
                ["ToastBorderBrush"] = "#3D8F78",
                ["AccentHoverBrush"] = "#7FE9C6",
                ["AccentPressedBrush"] = "#3DCEA0",
                ["CardHoverBrush"] = "#1E2924",
                ["CardHoverBorderBrush"] = "#3D8F78",
                ["DangerHoverBrush"] = "#4E2C32",
                ["ToolTipBrush"] = "#1A2420"
            },
            [ThemeMode.Light] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["InkBrush"] = "#F3F0E8",
                ["SidebarBrush"] = "#FBF8F2",
                ["PanelBrush"] = "#F3F0E8",
                ["CardBrush"] = "#FFFCF7",
                ["CardAltBrush"] = "#F7F3EB",
                ["LineBrush"] = "#E4DDD0",
                ["TextBrush"] = "#1C1914",
                ["MutedBrush"] = "#5E584E",
                ["SubtleBrush"] = "#6A6458",
                ["MintBrush"] = "#0E6B52",
                ["MintDimBrush"] = "#E3F6EE",
                ["BlueBrush"] = "#1B4F86",
                ["BlueDimBrush"] = "#E6F0F8",
                ["CoralBrush"] = "#B42318",
                ["CoralDimBrush"] = "#FDECEC",
                ["GoldBrush"] = "#8A5A00",
                ["GoldDimBrush"] = "#FFF3D4",
                ["WindowChromeBrush"] = "#FBF8F2",
                ["WindowBorderBrush"] = "#E4DDD0",
                ["BrandInkBrush"] = "#FFFFFF",
                ["HoverBrush"] = "#EBE6DB",
                ["HoverBorderBrush"] = "#D5CDBD",
                ["NavHoverBrush"] = "#EBE6DB",
                ["DangerBorderBrush"] = "#F0C2BE",
                ["InputBrush"] = "#FFFCF7",
                ["PopupBorderBrush"] = "#D5CDBD",
                ["InputHoverBrush"] = "#F7F3EB",
                ["ProgressBrush"] = "#E4DDD0",
                ["ScrollThumbBrush"] = "#C9C0B0",
                ["ScrollTrackBrush"] = "#F3F0E8",
                ["BadgeBrush"] = "#EBE6DB",
                ["DividerBrush"] = "#E4DDD0",
                ["StatusCardBrush"] = "#FFFCF7",
                ["StatusCardBorderBrush"] = "#E4DDD0",
                ["SearchBrush"] = "#FFFCF7",
                ["HeaderBorderBrush"] = "#E4DDD0",
                ["SyncBadgeBrush"] = "#E3F6EE",
                ["HeroStartBrush"] = "#D9F3E8",
                ["HeroMiddleBrush"] = "#F3F0E8",
                ["HeroEndBrush"] = "#FFFCF7",
                ["HeroStartColor"] = "#D9F3E8",
                ["HeroMiddleColor"] = "#F3F0E8",
                ["HeroEndColor"] = "#FFFCF7",
                ["HeroBadgeBrush"] = "#C8EBDC",
                ["HeroCopyBrush"] = "#3F3A32",
                ["HeroRingBrush"] = "#8FCBB8",
                ["HeroRingBrightBrush"] = "#0E6B52",
                ["HeroCoreBrush"] = "#0E6B52",
                ["HeroCoreInkBrush"] = "#FFFFFF",
                ["HeroTagBrush"] = "#E6F0F8",
                ["HeroTagTextBrush"] = "#1C1914",
                ["HeroTagAltBrush"] = "#C8EBDC",
                ["PrivacyCardBrush"] = "#F3FBF7",
                ["PrivacyCopyBrush"] = "#1C1914",
                ["PrivacyDotBrush"] = "#083D2E",
                ["SetupCardBrush"] = "#FFFCF7",
                ["SetupCardBorderBrush"] = "#E4DDD0",
                ["DetailCardBrush"] = "#FFFCF7",
                ["DetailCardBorderBrush"] = "#E4DDD0",
                ["TranscriptIconBrush"] = "#E3F6EE",
                ["EditorTextBrush"] = "#1C1914",
                ["ImportantCardBrush"] = "#FFFCF7",
                ["ImportantCardBorderBrush"] = "#E4DDD0",
                ["SpeakerHintBrush"] = "#F3FBF7",
                ["SpeakerHintBorderBrush"] = "#B7E0D2",
                ["SpeakerProfileHintBrush"] = "#FFFCF7",
                ["SpeakerProfileHintBorderBrush"] = "#E4DDD0",
                ["ToastBrush"] = "#E3F6EE",
                ["ToastBorderBrush"] = "#8FCBB8",
                ["AccentHoverBrush"] = "#128466",
                ["AccentPressedBrush"] = "#0B5A46",
                ["CardHoverBrush"] = "#FFFEFB",
                ["CardHoverBorderBrush"] = "#8FCBB8",
                ["DangerHoverBrush"] = "#F8DDDA",
                ["ToolTipBrush"] = "#FFFCF7"
            }
        };

    public static ThemeMode CurrentMode { get; private set; } = ThemeMode.Dark;

    public static bool IsDark => CurrentMode == ThemeMode.Dark;

    public static string DisplayName(ThemeMode mode) => mode == ThemeMode.Light ? "Light" : "Dark";

    public static void Apply(ThemeMode mode)
    {
        if (!Palettes.ContainsKey(mode))
            mode = ThemeMode.Dark;

        CurrentMode = mode;
        var resources = WpfApplication.Current?.Resources;
        if (resources is null) return;

        foreach (var (key, hex) in Palettes[mode])
        {
            var color = (WpfColor)WpfColorConverter.ConvertFromString(hex)!;
            if (key.EndsWith("Color", StringComparison.Ordinal))
            {
                resources[key] = color;
                continue;
            }

            if (resources[key] is SolidColorBrush existing)
            {
                var brush = existing.IsFrozen ? (SolidColorBrush)existing.Clone() : existing;
                if (brush.Color != color)
                    brush.Color = color;
                if (!ReferenceEquals(brush, existing))
                    resources[key] = brush;
                continue;
            }

            resources[key] = new SolidColorBrush(color);
        }
    }

    public static ThemeMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ThemeMode.Dark;
        return value.Trim().ToLowerInvariant() switch
        {
            "light" or "paper" or "moss" or "amber" => ThemeMode.Light,
            _ => ThemeMode.Dark
        };
    }
}
