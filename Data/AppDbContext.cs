using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BunbunBroll.Models;

namespace BunbunBroll.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectSegment> Segments => Set<ProjectSegment>();
    public DbSet<ProjectSentence> Sentences => Set<ProjectSentence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>()
            .HasMany(p => p.Segments)
            .WithOne(s => s.Project)
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProjectSegment>()
            .HasMany(s => s.Sentences)
            .WithOne(s => s.Segment)
            .HasForeignKey(s => s.SegmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class Project
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string RawScript { get; set; } = string.Empty;
    public string? Mood { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public List<ProjectSegment> Segments { get; set; } = new();
}

public class ProjectSegment
{
    public int Id { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    
    public List<ProjectSentence> Sentences { get; set; } = new();
    public Project Project { get; set; } = null!;
}

public class ProjectSentence
{
    public int Id { get; set; }
    public int SegmentId { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;

    // Saving the video links
    public string? VideoId { get; set; }
    public string? VideoProvider { get; set; }
    public string? VideoUrl { get; set; }
    public string? VideoPreviewUrl { get; set; }
    public string? VideoThumbUrl { get; set; }

    // NEW: Persist KeywordSet as JSON
    public string? KeywordsJson { get; set; }
    public string? SuggestedCategory { get; set; }
    public string? DetectedMood { get; set; }

    // CRITICAL: Duration of selected video for accurate % match calculation on load
    public int VideoDuration { get; set; }

    public ProjectSegment Segment { get; set; } = null!;

    /// <summary>
    /// Deserializes KeywordsJson back to KeywordSet object.
    /// Returns empty KeywordSet if JSON is null or empty.
    /// </summary>
    public KeywordSet GetKeywordSet()
    {
        if (string.IsNullOrWhiteSpace(KeywordsJson))
            return new KeywordSet();

        try
        {
            return JsonSerializer.Deserialize<KeywordSet>(KeywordsJson) ?? new KeywordSet();
        }
        catch
        {
            return new KeywordSet();
        }
    }
}
