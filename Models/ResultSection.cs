namespace BunbunBroll.Models;

public class ResultSection
{
    public string PhaseId { get; set; } = "";
    public string PhaseName { get; set; } = "";
    public int Order { get; set; }
    public string Content { get; set; } = "";
    public int WordCount { get; set; }
    public double DurationSeconds { get; set; }
    public bool IsValidated { get; set; }
    public bool IsExpanded { get; set; } = true;
    public bool IsRegenerating { get; set; }
    public List<string>? OutlinePoints { get; set; }
    public string CopyState { get; set; } = "idle";
    public bool IsFileMissing { get; set; }

    // Editing State
    public bool IsEditing { get; set; }
    public string EditedContent { get; set; } = "";
    public Stack<string> UndoStack { get; set; } = new();
    public Stack<string> RedoStack { get; set; } = new();
}
