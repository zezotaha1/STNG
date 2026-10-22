using System;
using System.Collections.Generic;

namespace STNG.FileManagement;

public class FileManagementOptions
{
    public const string SectionName = "FileManagement";
    public long MaximumFileSizeBytes { get; set; } = 25 * 1024 * 1024;
    public HashSet<string> AllowedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg"
    };
    public HashSet<string> AllowedContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg"
    };
    public bool RequireKnownContentSignature { get; set; } = true;
    public bool RequireSecurityScan { get; set; }
    public string? StorageRoot { get; set; }
    public string? StagingRoot { get; set; }
    public int MaximumDiscoveryScanCount { get; set; } = 1000;
    public TimeSpan IncompleteUploadRetention { get; set; } = TimeSpan.FromHours(24);
}
