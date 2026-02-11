namespace BunbunBroll.Services;

/// <summary>
/// Predefined era prefixes for image prompt generation (ported from ScriptFlow)
/// </summary>
public static class EraLibrary
{
    public static readonly IReadOnlyList<string> HistoricalPropheticEras = new List<string>
    {
        "7th century Arabia Islamic era, prophetic atmosphere, ",
        "6th century Pre-Islamic Arabia era, jahiliyya atmosphere, ",
        "1500 BC Ancient Egypt era, prophetic confrontation, ",
        "6th century BC Ancient Babylon era, ancient mystery, ",
        "Late Ancient Roman Empire era, civilization decline, "
    };

    public static readonly IReadOnlyList<string> EndTimesEras = new List<string>
    {
        "Islamic End Times era, apocalyptic atmosphere, ",
        "Dajjal deception era, false light and illusion, ",
        "Ya'juj and Ma'juj chaos era, overwhelming destruction, ",
        "Pre-Imam Mahdi era, global confusion and fear, ",
        "Post-Nabi Isa descent era, fragile peace, ",
        "Sun rising from the west era, final apocalyptic sign, "
    };

    public static readonly IReadOnlyList<string> ModernEras = new List<string>
    {
        "21st century modern urban era, digital technology, ",
        "Late modern civilization era, moral decay, ",
        "Global surveillance era, dystopian control, ",
        "AI-dominated future era, cold technocracy, "
    };

    public static readonly IReadOnlyList<string> AbstractEras = new List<string>
    {
        "Post-apocalyptic era, abandoned cities, ",
        "Lost ancient civilization ruins era, ",
        "Metaphysical void era, existential reflection, ",
        "Cosmic end-of-world era, cracked sky, "
    };

    public static string GetEraSelectionInstructions()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("ERA SELECTION INSTRUCTIONS:");
        sb.AppendLine("Select the appropriate era prefix from the available options below based on the scene's setting.");
        sb.AppendLine();
        sb.AppendLine("AVAILABLE ERAS (select one per prompt):");
        sb.AppendLine();

        sb.AppendLine("Historical/Prophetic Eras:");
        foreach (var era in HistoricalPropheticEras) sb.AppendLine($"  - \"{era}\"");
        sb.AppendLine();

        sb.AppendLine("End Times Eschatological Eras:");
        foreach (var era in EndTimesEras) sb.AppendLine($"  - \"{era}\"");
        sb.AppendLine();

        sb.AppendLine("Modern/Contemporary Eras:");
        foreach (var era in ModernEras) sb.AppendLine($"  - \"{era}\"");
        sb.AppendLine();

        sb.AppendLine("Abstract/Symbolic Eras:");
        foreach (var era in AbstractEras) sb.AppendLine($"  - \"{era}\"");

        return sb.ToString();
    }
}
