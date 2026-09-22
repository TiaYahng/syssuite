using System.IO;

namespace SysSuite.Core.System;

public sealed partial class DiskInspectionService
{
    private static readonly EnumerationOptions ApplicationOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false
    };

    private static bool HasApplicationInstallationMarker(DirectoryInfo directory) =>
        directory.EnumerateFiles("*.manifest", ScanOptions).Any()
        || directory.EnumerateDirectories("app", ScanOptions)
            .Any(child => child.EnumerateFiles("*.exe", ApplicationOptions).Any());

    private static bool IsApplicationDirectory(DirectoryInfo directory, out DirectoryChildren children)
    {
        children = new([], []);
        try
        {
            if (ApplicationDirectoryNames.Contains(directory.Name)
                || ApplicationRelatedDirectoryNames.Contains(directory.Name)
                || IsApplicationRelatedDirectoryName(directory.Name)
                || directory.Name.Contains("portable", StringComparison.OrdinalIgnoreCase)
                || directory.Name.Contains("便携", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var files = directory.EnumerateFiles("*", ScanOptions).ToArray();
            var directories = directory.EnumerateDirectories("*", ScanOptions).ToArray();
            children = new(directories, files);
            return files.Any(IsApplicationFile) || HasApplicationInstallationMarker(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static bool IsApplicationFile(FileInfo file) =>
        ApplicationFileExtensions.Contains(file.Extension)
        || ApplicationMarkerFiles.Contains(file.Name)
        || file.Name.Contains("portable", StringComparison.OrdinalIgnoreCase);

    private static bool IsApplicationRelatedDirectoryName(string name) =>
        name.StartsWith("shared my mastercam", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("my mastercam", StringComparison.OrdinalIgnoreCase);
}
