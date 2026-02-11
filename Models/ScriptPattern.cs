using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BunbunBroll.Models;

/// <summary>
/// Database entity representing a loaded script pattern.
/// </summary>
public class ScriptPattern
{
    [Key]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    public int PhaseCount { get; set; }

    public string? FilePath { get; set; }

    [NotMapped]
    public PatternConfiguration Configuration { get; set; } = new();
}
