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
    private const int FetchPageSize = 5000;

    private string _baseName = "export";
    private string _sheetName = "Records";
    private string _folder = "";
    private long _total = 0;

    internal Func<int, int, Task<DesktopApiClient.ExportPage<DesktopApiClient.ExportVehicleRow>>>? _pageFetcher;

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
        cmbChunkSize.SelectedIndex = 4; // default 5 Lakh

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
        Func<int, int, Task<DesktopApiClient.ExportPage<DesktopApiClient.ExportVehicleRow>>> pageFetcher)
    {
        txtTitle.Text    = title;
        txtSubtitle.Text = subtitle;
        _baseName        = SafeFileNameStem(baseName);
        _sheetName       = string.IsNullOrWhiteSpace(sheetName) ? "Records" : sheetName;
        _total           = totalHint;
        _pageFetcher     = pageFetcher;
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
        if (_pageFetcher == null) return;
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
            string name  = fileCount == 1
                ? $"{_baseName}_{ts}.xlsx"
                : $"{_baseName}_part_{idx}_of_{fileCount}_{ts}.xlsx";
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
        if (_pageFetcher == null) return;

        // Phase plan:
        //   0 – 70%  : fetch all required pages (covering the range of the
        //              selected chunks). We always fetch from page 0 to the
        //              max needed page, then slice client-side per chunk so a
        //              user re-running with a different chunk size doesn't
        //              hammer the server twice.
        //   70 – 100%: write Excel files (one per chunk).

        long maxRowNeeded = rowsToDo.Max(r => r.StartRowInclusive + r.Count);
        int  totalPages   = (int)Math.Ceiling((double)maxRowNeeded / FetchPageSize);

        var allRows = new List<DesktopApiClient.ExportVehicleRow>((int)Math.Min(maxRowNeeded, int.MaxValue));
        for (int pg = 0; pg < totalPages; pg++)
        {
            txtStatus.Text = $"Fetching page {pg + 1} of {totalPages}…";
            double pct = (double)pg / totalPages * 70.0;
            pb.Value = pct;
            txtPct.Text = $"{(int)pct}%";

            var page = await _pageFetcher(pg, FetchPageSize);
            allRows.AddRange(page.Rows);
            if (!page.HasMore || page.Rows.Count == 0) break;
        }

        pb.Value = 70;
        txtPct.Text = "70%";

        double writeSliceWidth = 30.0 / rowsToDo.Count;
        int idx = 0;
        foreach (var row in rowsToDo)
        {
            idx++;
            row.SetStatus(ChunkStatus.Writing);
            txtStatus.Text = $"Writing {row.FileName} ({idx} of {rowsToDo.Count})…";

            int from = (int)row.StartRowInclusive;
            int upto = Math.Min(allRows.Count, from + (int)row.Count);
            if (from >= allRows.Count)
            {
                row.SetStatus(ChunkStatus.Failed);
                continue;
            }
            var slice = allRows.GetRange(from, upto - from);

            try
            {
                // Write on a background thread — Excel write of ~5L rows can take seconds.
                await Task.Run(() => VehicleExcelWriter.Write(slice, _sheetName, row.FullPath));
                row.SetStatus(ChunkStatus.Done);
            }
            catch (Exception ex)
            {
                row.SetStatus(ChunkStatus.Failed);
                txtStatus.Text = $"Failed to write {row.FileName}: {ex.Message}";
            }

            double pct = 70 + idx * writeSliceWidth;
            pb.Value = pct;
            txtPct.Text = $"{(int)pct}%";
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
