using System.Diagnostics;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

public sealed partial class DiskInspectionService
{
    private sealed class ScanTracker
    {
        private readonly object syncRoot = new();
        private readonly IProgress<DiskScanProgress>? progress;
        private readonly Stopwatch stopwatch;
        private string phase = "正在准备扫描";
        private string currentPath = string.Empty;
        private long processedFiles;
        private long totalFiles;
        private long processedBytes;
        private long totalBytes;
        private long lastReportTicks;

        public ScanTracker(IProgress<DiskScanProgress>? progress)
        {
            this.progress = progress;
            stopwatch = Stopwatch.StartNew();
        }

        public void BeginPhase(string phase, long totalFiles, long totalBytes)
        {
            lock (syncRoot)
            {
                this.phase = phase;
                this.totalFiles = totalFiles;
                this.totalBytes = totalBytes;
                processedFiles = 0;
                processedBytes = 0;
                lastReportTicks = 0;
                ReportLocked();
            }
        }

    public void ReportPhase(string phase, string currentPath)
    {
        lock (syncRoot)
        {
            this.phase = phase;
            this.currentPath = currentPath;
            ReportLocked();
        }
    }

    public void CompletePhase(string phase)
    {
        lock (syncRoot)
        {
            this.phase = phase;
            currentPath = string.Empty;
            processedFiles = totalFiles;
            processedBytes = totalBytes;
            lastReportTicks = 0;
            ReportLocked(force: true);
        }
    }

        public void ReportFile(string path, long length)
        {
            lock (syncRoot)
            {
                currentPath = path;
                processedFiles++;
                processedBytes += length;
                ReportLocked();
            }
        }

        public void ReportBytes(string path, long length)
        {
            lock (syncRoot)
            {
                currentPath = path;
                processedBytes += length;
                ReportLocked();
            }
        }

        private void ReportLocked(bool force = false)
        {
            if (progress is null
                || (!force && stopwatch.ElapsedTicks - lastReportTicks < Stopwatch.Frequency / 5))
            {
                return;
            }

            lastReportTicks = stopwatch.ElapsedTicks;
            var totalFileCount = Math.Max(totalFiles, processedFiles);
            var totalByteCount = Math.Max(totalBytes, processedBytes);
            double? ratio = totalByteCount == 0
                ? totalFileCount == 0 ? null : (double)processedFiles / totalFileCount
                : (double)processedBytes / totalByteCount;
            TimeSpan? remaining = ratio is not { } value || value < 0.01d
                ? null
                : TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds / value * (1d - value));
            progress.Report(new DiskScanProgress(
                phase,
                ratio,
                processedFiles,
                totalFileCount,
                processedBytes,
                Math.Max(totalBytes, processedBytes),
                currentPath,
                stopwatch.Elapsed,
                remaining));
        }
    }
}
