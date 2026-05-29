using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Exports;

// Chunked export dialog: shows total record count, lets the admin pick a chunk
// size (1L–10L), previews the resulting file list, and writes each chunk to a
// separate .xlsx file. Used by Finances (per-finance / per-branch) and Reports
// (vehicle / RC / chassis records). Self-contained — given a page fetcher
// (returns rows + total) it does fetching, chunking, Excel writing and UI
// updates with progress reporting.
public partial class ChunkedExportDialog : Window
{
    // Allowed chunk sizes, in lakhs. 1L = 100,000.
    private static readonly int[] ChunkSizesLakh = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    // No more FetchPageSize — each chunk file is fetched as a single page
    // (size = chunk size, page = chunk index). 20L records at chunk size 5L
    // now takes 4 round-trips instead of the old 400 of 5000 rows each, and
    // we write each file as soon as its rows land instead of buffering the
    // whole dataset in memory first.

    private string _baseName = "export";
    private string _sheetName = "Records";
    private string _folder = "";
    private long _total = 0;

    internal Func<int, int, Task<DesktopApiClient.ExportPage<DesktopApiClient.ExportVehicleRow>>>? _pageFetcher;

    // Server-stream "part" downloader: (offset, count, savePath, progress) →
    // streams just that slice as an .xlsx straight to the file. When set, the
    // dialog uses this instant path instead of the JSON pageFetcher.
    internal Func<long, int, string, IProgress<long>?, Task>? _chunkDownloader;

    public ObservableCollection<ChunkRow> Chunks { get; } = new();

    public ChunkedExportDialog()
    {
        InitializeComponent();

        foreach (var l in ChunkSizesLakh)
            cmbChunkSize.Items.Add(new ComboBoxItem
            {
                Content = l == 1 ? "1 Lakh (100,000)" : $"{l} Lakhs ({l * 100_000:N0})",
                Tag     = l * 100_000
            });
        // Default to 1 lakh records per file — the admin asked for the export
        // split into 1-lakh parts. They can pick up to 10 lakh per file if
        // they'd rather have fewer, larger files. Each part streams from the
        // server as its own .xlsx, so part count doesn't affect speed.
        cmbChunkSize.SelectedIndex = 0; // 1 Lakh

        lvFiles.ItemsSource = Chunks;
        _folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        UpdateFolderLabel();
    }

    // Entry point used by callers. Wires up the data source and shows the
    // dialog. Returns once the user closes the window. The dialog itself does
    // the fetch / write loop; the caller only supplies the source and pretty
    // names. internal — references DesktopApiClient.ExportPage which is internal.
    internal void Configure(
        string title,
        string subtitle,
        string baseName,
        string sheetName,
        long totalHint,
        Func<int, int, Task<DesktopApiClient.ExportPage<DesktopApiClient.ExportVehicleRow>>>? pageFetcher = null,
        Func<long, int, string, IProgress<long>?, Task>? chunkDownloader = null)
    {
        txtTitle.Text    = title;
        txtSubtitle.Text = subtitle;
        _baseName        = SafeFileNameStem(baseName);
        _sheetName       = string.IsNullOrWhiteSpace(sheetName) ? "Records" : sheetName;
        _total           = totalHint;
        _pageFetcher     = pageFetcher;
        _chunkDownloader = chunkDownloader;
        txtTotal.Text    = totalHint > 0 ? $"{totalHint:N0}" : "—";
        RecomputeFiles();
    }

    // ── UI handlers ───────────────────────────────────────────────────────

    private void cmbChunkSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RecomputeFiles();
    }

    private void btnFolder_Click(object sender, RoutedEventArgs e)
    {
        // OpenFolderDialog is .NET 8+; .NET 6 falls back to SaveFileDialog
        // pointed at the chosen folder by stripping the file name.
        var dlg = new SaveFileDialog
        {
            Title       = "Choose folder (any file name)",
            FileName    = "select-this-folder",
            Filter      = "Folder|*.folder",
            CheckPathExists = true
        };
        if (dlg.ShowDialog() == true)
        {
            var dir = Path.GetDirectoryName(dlg.FileName);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                _folder = dir;
                UpdateFolderLabel();
                RecomputeFiles();
            }
        }
    }

    private async void btnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_pageFetcher == null && _chunkDownloader == null) return;
        var selected = Chunks.Where(c => c.Selected && !c.IsDone).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Tick at least one file to download.",
                "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!Directory.Exists(_folder))
        {
            MessageBox.Show("Output folder doesn't exist any more. Pick another one.",
                "Folder missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnDownload.IsEnabled = false;
        btnClose.Content     = "Cancel";
        cmbChunkSize.IsEnabled = false;

        try
        {
            await RunDownloadAsync(selected);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnDownload.IsEnabled = true;
            btnClose.Content      = "Close";
            cmbChunkSize.IsEnabled = true;
            txtStatus.Text        = "Done.";
            pb.Value              = 100;
            txtPct.Text           = "100%";
        }
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void btnOpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is ChunkRow row && row.IsDone && File.Exists(row.FullPath))
        {
            Process.Start("explorer.exe", $"/select,\"{row.FullPath}\"");
        }
    }

    // ── Core logic ────────────────────────────────────────────────────────

    private void RecomputeFiles()
    {
        Chunks.Clear();
        if (_total <= 0 || cmbChunkSize.SelectedItem is not ComboBoxItem ci) return;
        int chunkSize = (int)ci.Tag;
        int fileCount = (int)Math.Ceiling((double)_total / chunkSize);
        int pad       = fileCount.ToString().Length;
        string ts     = DateTime.Now.ToString("yyyyMMdd");

        for (int i = 0; i < fileCount; i++)
        {
            long start   = (long)i * chunkSize;
            long count   = Math.Min(chunkSize, _total - start);
            string idx   = (i + 1).ToString().PadLeft(pad, '0');
            string ext   = VehicleExcelWriter.Extension;   // "csv" — opens in Excel, writes instantly
            string name  = fileCount == 1
                ? $"{_baseName}_{ts}.{ext}"
                : $"{_baseName}_part_{idx}_of_{fileCount}_{ts}.{ext}";
            Chunks.Add(new ChunkRow
            {
                Index    = i,
                FileName = name,
                FullPath = Path.Combine(_folder, name),
                StartRowInclusive = start,
                Count    = count,
                Selected = true
            });
        }

        txtFileCount.Text = fileCount.ToString();
    }

    private void UpdateFolderLabel() => txtFolderPath.Text = $"Folder: {_folder}";

    private async Task RunDownloadAsync(List<ChunkRow> rowsToDo)
    {
        int chunkSize = (cmbChunkSize.SelectedItem is ComboBoxItem ci) ? (int)ci.Tag : 100_000;

        // ── Preferred path: server-streamed parts (instant) ──────────────────
        // For each selected part, the SERVER streams just that slice
        // (offset, count) as an .xlsx straight to the part's file. No JSON,
        // no client-side workbook build — each part downloads near-instantly.
        if (_chunkDownloader != null)
        {
            int done = 0;
            foreach (var row in rowsToDo)
            {
                done++;
                row.SetStatus(ChunkStatus.Writing);
                txtStatus.Text = $"Downloading {row.FileName} ({done} of {rowsToDo.Count}) — {row.Count:N0} rows…";
                try
                {
                    await _chunkDownloader(row.StartRowInclusive, (int)row.Count, row.FullPath, null);
                    row.SetStatus(ChunkStatus.Done);
                }
                catch (Exception ex)
                {
                    row.SetStatus(ChunkStatus.Failed);
                    txtStatus.Text = $"Failed: {row.FileName}: {ex.Message}";
                }
                double p = (double)done / rowsToDo.Count * 100.0;
                pb.Value = p; txtPct.Text = $"{(int)p}%";
            }
            return;
        }

        // ── Legacy path (Reports): fetch JSON pages, write client-side ───────
        if (_pageFetcher == null) return;
        int totalSteps = rowsToDo.Count * 2;
        int stepsDone  = 0;
        int idx = 0;
        foreach (var row in rowsToDo)
        {
            idx++;
            row.SetStatus(ChunkStatus.Writing);
            txtStatus.Text = $"Fetching {row.FileName} ({idx} of {rowsToDo.Count}) — {row.Count:N0} rows…";
            List<DesktopApiClient.ExportVehicleRow> rows;
            try
            {
                var resp = await _pageFetcher(row.Index, chunkSize);
                rows = resp.Rows;
            }
            catch (Exception ex)
            {
                row.SetStatus(ChunkStatus.Failed);
                txtStatus.Text = $"Failed to fetch {row.FileName}: {ex.Message}";
                continue;
            }
            stepsDone++;
            double pct = (double)stepsDone / totalSteps * 100.0;
            pb.Value = pct; txtPct.Text = $"{(int)pct}%";

            if (rows.Count == 0) { row.SetStatus(ChunkStatus.Failed); continue; }

            txtStatus.Text = $"Writing {row.FileName} ({idx} of {rowsToDo.Count})…";
            try
            {
                await Task.Run(() => VehicleExcelWriter.Write(rows, _sheetName, row.FullPath));
                row.SetStatus(ChunkStatus.Done);
            }
            catch (Exception ex)
            {
                row.SetStatus(ChunkStatus.Failed);
                txtStatus.Text = $"Failed to write {row.FileName}: {ex.Message}";
            }
            stepsDone++;
            pct = (double)stepsDone / totalSteps * 100.0;
            pb.Value = pct; txtPct.Text = $"{(int)pct}%";
        }
    }

    private static string SafeFileNameStem(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "export";
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Trim().Replace(' ', '_');
    }
}

public enum ChunkStatus { Pending, Writing, Done, Failed }

public class ChunkRow : INotifyPropertyChanged
{
    public int    Index    { get; set; }
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long   StartRowInclusive { get; set; }
    public long   Count    { get; set; }

    public string RecordsDisplay => $"{Count:N0}";

    private bool _selected = true;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; OnPropChanged(); }
    }

    private ChunkStatus _status = ChunkStatus.Pending;
    public ChunkStatus Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropChanged();
            OnPropChanged(nameof(StatusText));
            OnPropChanged(nameof(StatusBrush));
            OnPropChanged(nameof(IsDone));
        }
    }

    public string StatusText => _status switch
    {
        ChunkStatus.Pending => "Pending",
        ChunkStatus.Writing => "Writing…",
        ChunkStatus.Done    => "Downloaded",
        ChunkStatus.Failed  => "Failed",
        _ => "—"
    };

    public Brush StatusBrush => _status switch
    {
        ChunkStatus.Done    => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
        ChunkStatus.Failed  => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
        ChunkStatus.Writing => new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23)),
        _                   => new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77))
    };

    public bool IsDone => _status == ChunkStatus.Done;

    public void SetStatus(ChunkStatus s) => Status = s;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
