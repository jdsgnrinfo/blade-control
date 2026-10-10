namespace BladeControl.Core.Services;

/// <summary>How the Display page writes the screen's figures. Pure, so it can be tested without a screen.</summary>
internal static class DisplayPanelText
{
    /// <summary>A resolution, with a thin multiplication sign: "1920 × 1080".</summary>
    public static string Resolution(int width, int height) => $"{width} × {height}";
}
