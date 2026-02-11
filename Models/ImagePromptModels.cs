namespace BunbunBroll.Models;

/// <summary>
/// Visual style constants for image prompts (ported from ScriptFlow)
/// </summary>
public static class ImageVisualStyle
{
    /// <summary>
    /// Base locked style suffix for all prompts
    /// </summary>
    public const string BASE_STYLE_SUFFIX =
        ", semi-realistic academic painting style with visible brushstrokes, " +
        "traditional Islamic iconography mixed with Western historical art influences, " +
        "dramatic high-contrast lighting with directional illumination, " +
        "vibrant focal colors against muted backgrounds, " +
        "expressive painterly textures, atmospheric depth, " +
        "ultra-detailed, sharp focus, 8k quality, consistent visual tone";

    /// <summary>
    /// Additional suffix for prophet characters (MANDATORY)
    /// </summary>
    public const string PROPHET_SUFFIX =
        ", face completely veiled in soft white-golden divine light, " +
        "facial features not visible, reverent depiction";
}

/// <summary>
/// Character rules for Islamic syar'i compliance (ported from ScriptFlow)
/// </summary>
public static class CharacterRules
{
    public const string GENDER_RULES = @"
CHARACTERS ALLOWED:
- Male characters: Specify as 'male man', 'male king', 'male elder', etc.
- Female characters: ALLOWED with syar'i dress code
  * Must specify: 'female in full hijab', 'woman in modest Islamic dress'
  * No exposed hair, skin, or feminine features
  * Loose modest clothing covering entire body except face/hands
  * No tight or revealing clothing
- Children: Specify gender with modest dress

PROHIBITED:
- Women without hijab/modest dress
- Exposed feminine features
- Any form of revealing clothing";

    public const string PROPHET_RULES = @"
PROPHET DEPICTION (MANDATORY):
- Face MUST be completely obscured/hidden
- Soft white-golden divine light (nur) covering face area
- Back view or side view preferred
- Add ALWAYS: ', face completely veiled in soft white-golden divine light, facial features not visible'
- NEVER show any facial features of prophets
- Reverent and respectful depiction at all times";
}
