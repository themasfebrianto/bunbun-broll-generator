using BunBunBroll.Data;
using BunBunBroll.Models;
using Microsoft.EntityFrameworkCore;

namespace BunBunBroll.Services;

public interface IProjectService
{
    Task<List<Project>> GetProjectsAsync();
    Task<Project?> GetProjectAsync(string id);
    Task<Project> SaveJobAsProjectAsync(ProcessingJob job);
    Task DeleteProjectAsync(string id);
}

public class ProjectService(AppDbContext db) : IProjectService
{
    public async Task<List<Project>> GetProjectsAsync()
    {
        return await db.Projects
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Project?> GetProjectAsync(string id)
    {
        return await db.Projects
            .Include(p => p.Segments)
                .ThenInclude(s => s.Sentences)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Project> SaveJobAsProjectAsync(ProcessingJob job)
    {
        var project = await db.Projects
            .Include(p => p.Segments)
            .FirstOrDefaultAsync(p => p.Id == job.Id);
            
        bool isNew = project == null;

        if (isNew)
        {
            project = new Project
            {
                Id = job.Id,
                Name = job.ProjectName,
                RawScript = job.RawScript,
                Mood = job.Mood,
                CreatedAt = DateTime.UtcNow
            };
            db.Projects.Add(project);
        }
        else
        {
            project!.Name = job.ProjectName;
            project.RawScript = job.RawScript;
            project.Mood = job.Mood;
            db.Segments.RemoveRange(project.Segments);
            project.Segments.Clear();
        }

        int segmentOrder = 0;
        foreach (var jobSegment in job.Segments)
        {
            var segment = new ProjectSegment
            {
                ProjectId = project.Id,
                Title = jobSegment.Title,
                Order = segmentOrder++
            };
            project.Segments.Add(segment);

            int sentenceOrder = 0;
            foreach (var jobSentence in jobSegment.Sentences)
            {
                segment.Sentences.Add(new ProjectSentence
                {
                    Order = sentenceOrder++,
                    Text = jobSentence.Text,
                    VideoId = jobSentence.SelectedVideo?.Id,
                    VideoProvider = jobSentence.SelectedVideo?.Provider,
                    VideoUrl = jobSentence.SelectedVideo?.DownloadUrl,
                    VideoPreviewUrl = jobSentence.SelectedVideo?.PreviewUrl,
                    VideoThumbUrl = jobSentence.SelectedVideo?.ThumbnailUrl
                });
            }
        }

        await db.SaveChangesAsync();
        return project;
    }

    public async Task DeleteProjectAsync(string id)
    {
        var project = await db.Projects.FindAsync(id);
        if (project != null)
        {
            db.Projects.Remove(project);
            await db.SaveChangesAsync();
        }
    }
}
