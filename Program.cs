using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace PowerBlockerViewer
{
    public class MainForm : Form
    {
        private ListView processList = null!;
        private System.Windows.Forms.Timer refreshTimer = null!;
        private Label statusLabel = null!;
        private Button btnRefresh = null!;
        
        // Object pooling to reduce allocations
        private static readonly object[] _itemObjects = new object[3]; // Reuse for ListView items
        private static int _poolIndex = 0;
        
        // Caching to avoid unnecessary re-parsing
        private string _lastDataHash = string.Empty;
        private DateTime _lastRefresh = DateTime.MinValue;

        public MainForm()
        {
            InitializeComponents();
            LoadPowerData();
            
            // Auto-refresh every 3 seconds to show changes dynamically
            if (refreshTimer != null)
                refreshTimer.Start();
        }

        private void InitializeComponents()
        {
            this.Text = "Screen Off blocker / Power Requests Viewer";
            this.Size = new System.Drawing.Size(700, 500);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            // Status Label
            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Top;
            statusLabel.Padding = new Padding(10, 5, 0, 0);
            statusLabel.Text = "Scanning for processes blocking Power... (Run as Admin for full results)";

            // Refresh Button
            btnRefresh = new Button();
            btnRefresh.Text = "Refresh Now";
            btnRefresh.Dock = DockStyle.Bottom;
            btnRefresh.Height = 40;
            btnRefresh.Click += RefreshButton_Click;

            // ListView Configuration
            processList = new ListView();
            processList.Dock = DockStyle.Fill;
            processList.View = View.Details;
            processList.FullRowSelect = true;
            processList.GridLines = true;
            processList.Sorting = SortOrder.Ascending;

            // Columns: Process Name, Type (Display/System), Reason
            processList.Columns.Add("Process Name", 200);
            processList.Columns.Add("Block Type", 150);
            processList.Columns.Add("Reason / Details", 300);

            // Timer for auto-refresh - use named method to avoid lambda capture
            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 3000; // 3 seconds
            refreshTimer.Tick += RefreshTimer_Tick;

            // Container Panel
            Panel mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(10);
            
            // Add controls to main panel first
            mainPanel.Controls.Add(processList);
            mainPanel.Controls.Add(statusLabel);
            mainPanel.Controls.Add(btnRefresh);
            
            // Add main panel to form
            this.Controls.Add(mainPanel);
        }

        // Event handlers - use named methods to avoid lambda memory capture
        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            LoadPowerData();
        }

        private void RefreshButton_Click(object? sender, EventArgs e)
        {
            LoadPowerData();
        }

        private void LoadPowerData()
        {
            statusLabel.Text = "Executing: powercfg /requests ...";
            statusLabel.ForeColor = SystemColors.GrayText;
            
            // Skip refresh if within 1 second of last refresh (reduced from 2 seconds)
            if (DateTime.Now - _lastRefresh < TimeSpan.FromSeconds(1))
            {
                return;
            }
            
            _lastRefresh = DateTime.Now;
            
            // Clear ListView properly to free memory
            processList.BeginUpdate();
            processList.Items.Clear();
            processList.EndUpdate();

            try
            {
                using (Process powerCfgProcess = new Process())
                {
                    powerCfgProcess.StartInfo.FileName = "powercfg";
                    powerCfgProcess.StartInfo.Arguments = "/requests";
                    powerCfgProcess.StartInfo.UseShellExecute = false;
                    powerCfgProcess.StartInfo.RedirectStandardOutput = true;
                    powerCfgProcess.StartInfo.CreateNoWindow = true;

                    // Attempt to start. If it fails due to UAC, we catch the exception.
                    try
                    {
                        powerCfgProcess.Start();
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        // Specific handling for UAC/elevation issues
                        if (ex.ErrorCode == -2147467259) // ERROR_ELEVATION_REQUIRED
                        {
                            statusLabel.Text = "Error: This application must be run as Administrator to see detailed results.";
                            statusLabel.ForeColor = Color.Red;
                        }
                        else
                        {
                            statusLabel.Text = "Error: Failed to execute powercfg command.";
                            statusLabel.ForeColor = Color.Red;
                        }
                        return;
                    }

                    string output = powerCfgProcess.StandardOutput.ReadToEnd();
                    powerCfgProcess.WaitForExit();

                    // Improved hash - combine length and first/last chars to reduce collisions
                    if (!string.IsNullOrEmpty(output))
                    {
                        _lastDataHash = string.Empty;
                        ParseAndDisplayData(output);
                        return;
                    }
                    
                    string currentHash = output.Length + "_" + 
                                        (output.Length > 0 ? output[0] : '0') + "_" + 
                                        (output.Length > 1 ? output[output.Length - 1] : '0');
                    
                    if (currentHash == _lastDataHash && output.Trim().Length > 10)
                    {
                        return; // No meaningful changes detected
                    }
                    _lastDataHash = currentHash;

                    ParseAndDisplayData(output);
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "An error occurred: " + ex.Message;
            }
        }

        private void ParseAndDisplayData(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            
            // Split lines efficiently - reuse string array
            string[] lines = data.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            
            bool inDisplaySection = false;
            string? currentProcessName = null;

            foreach (string line in lines)
            {
                // 1. Detect Section Headers - we only care about DISPLAY section
                if (line.StartsWith("DISPLAY:"))
                {
                    inDisplaySection = true;
                    continue;
                }
                else if (line.StartsWith("SYSTEM:") || line.StartsWith("AWAYMODE:") || 
                         line.StartsWith("EXECUTION:") || line.StartsWith("PERFBOOST:") ||
                         line.StartsWith("ACTIVELOCKSCREEN:"))
                {
                    // We're not interested in these sections for screen-off blocking
                    inDisplaySection = false;
                    continue;
                }

                // 2. Only parse process lines when in DISPLAY section
                if (inDisplaySection && line.StartsWith("[") && line.Contains("]"))
                {
                    // Extract request type first (e.g., [PROCESS], [SERVICE], [DRIVER])
                    int firstBracketEnd = line.IndexOf("]");
                    if (firstBracketEnd > 1)
                    {
                        // Extract process path that comes after the brackets
                        string processPath = line.Substring(firstBracketEnd + 1).Trim();
                        
                        // Extract process name from full path
                        string procName = ExtractProcessName(processPath);
                        currentProcessName = procName;
                        
                        // Create the main ListViewItem for the process
                        ListViewItem item = new ListViewItem(procName);
                        item.SubItems.Add("Preventing Screen Off");
                        item.SubItems.Add("--- Active Request ---");
                        
                        // Color code: Red for display blocking
                        item.ForeColor = Color.Red;

                        processList.Items.Add(item);
                    }
                }
                // 3. Parse Reason/Description Lines
                else if (inDisplaySection && !string.IsNullOrWhiteSpace(line) && 
                         !line.StartsWith("[") && !line.StartsWith("===") && 
                         !line.StartsWith("DISPLAY:") && !line.StartsWith("SYSTEM:") &&
                         currentProcessName != null)
                {
                    // Update the last item's reason
                    if (processList.Items.Count > 0)
                    {
                        int lastIndex = processList.Items.Count - 1;
                        var lastItem = processList.Items[lastIndex];
                        if (lastItem.Text == currentProcessName)
                        {
                            lastItem.SubItems[2].Text = line.Trim();
                        }
                    }
                }
            }

            // Update status with actual count of display-blocking processes
            statusLabel.Text = $"Scanned. Found {processList.Items.Count} process(es) preventing screen off.";
            statusLabel.ForeColor = processList.Items.Count > 0 ? Color.Red : Color.Green;
            
            // Force garbage collection to free memory
            if (_poolIndex % 10 == 0) // Every 10 refreshes
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // Helper method for process name extraction to reduce duplication
        private static string ExtractProcessName(string processPath)
        {
            if (string.IsNullOrEmpty(processPath)) return "Unknown Process";
            
            // Handle different path formats and extract just the executable name
            int lastBackslash = processPath.LastIndexOf('\\');
            int lastSlash = processPath.LastIndexOf('/');
            int lastSeparator = Math.Max(lastBackslash, lastSlash);
            
            string procName = lastSeparator >= 0 ? processPath.Substring(lastSeparator + 1) : processPath;
            
            // Remove .exe extension if present (case insensitive)
            if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                procName = procName.Substring(0, procName.Length - 4);
            }
            
            // Remove any leading/trailing quotes or brackets
            procName = procName.Trim('\"', '[', ']', ' ');
            
            // If we still have an empty name, use the original
            return string.IsNullOrEmpty(procName) ? "Unknown Process" : procName;
        }

        // Helper struct to store logic data
        private class ProcessData
        {
            public string Name { get; set; } = string.Empty;
            public string BlockType { get; set; } = string.Empty;
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Check if running as admin
            bool isAdmin = false;
            System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
            System.Security.Principal.WindowsPrincipal principal = new System.Security.Principal.WindowsPrincipal(id);
            if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                isAdmin = true;
            }

            if (!isAdmin)
            {
                DialogResult result = MessageBox.Show(
                    "For complete results, it is highly recommended to run this program as Administrator.\n\nClick OK to run as Admin, or Cancel to run normally (Limited View).",
                    "Administrator Rights Required",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.OK)
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.UseShellExecute = true;
                    psi.FileName = Application.ExecutablePath;
                    psi.Verb = "runas"; // Trigger UAC
                    try
                    {
                        System.Diagnostics.Process.Start(psi);
                        return; // Exit current process
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // User cancelled UAC
                    }
                }
                else
                {
                    Application.Run(new MainForm()); // Run without elevation
                }
            }
            else
            {
                Application.Run(new MainForm());
            }
        }
    }
}