using Controller.tests;
using NLog;
using PokerSuite.Shared.Constants;
using PokerSuite.Shared.Helpers;
using System.Diagnostics;
using System.Reflection;

namespace PDecision
{
    public partial class DecisionUI : Form
    {
        private const int MaxActivityLines = 2000;
        private static DecisionUI instance;

        private readonly Logger logger = LogHelpers.GetStandardLogger(enServiceType.PDecision, Assembly.GetExecutingAssembly());
        private Controller contr;
        public DecisionUI()
        {
            InitializeComponent();
            string version = GetVersion();
            this.Text = string.Format("PDecision - v{0}", version);
            instance = this;

            Task.Run(() => Initialize());
        }


        private void Initialize()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            string msg = $"Started initializing DM";
            UpdateStatus(msg);
            contr = new Controller();
            if (!contr.Initialize())
            {
                if (contr.MachineNotRegistered)
                {
                    UpdateStatus($"PC not registered, send MAC {string.Join(", ", contr.MacAddresses)} to the administrator and restart");
                    ShowRegistration(contr.MacAddresses);
                }
                else
                {
                    UpdateStatus("Error initializing Decision Maker");
                    MessageBox.Show($"Error initializing Decision Maker" +
                                    (string.IsNullOrEmpty(contr.FailureReason) ? "" : $":{Environment.NewLine}{contr.FailureReason}"));
                }
                return;
            }
            logger.Info(msg);
            sw.Stop();
            msg = $"Done initializing DM: {Math.Round(sw.Elapsed.TotalSeconds, 2)}s";
            logger.Info(msg);
            UpdateStatus(msg);
        }


        // Initialize runs on a background thread, the dialog must be shown on the UI thread
        private void ShowRegistration(IReadOnlyList<string> macAddresses)
        {
            try
            {
                Invoke((System.Windows.Forms.MethodInvoker)delegate
                {
                    using var form = new RegistrationForm(macAddresses);
                    form.ShowDialog(this);
                });
            }
            catch (Exception ex)
            {
                logger.Error($"{ex}");
            }
        }

        private void UpdateStatus(string text)
        {
            if (!this.Visible)
            {
                return;
            }

            try
            {
                statusStrip1.Invoke((System.Windows.Forms.MethodInvoker)delegate
                {
                    toolStripStatusLabel1.Text = text;
                });
            }
            catch (Exception ex)
            {
                logger.Error($"{ex}");
            }
        }


        private string GetVersion()
        {
            try
            {
                return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
            catch (Exception ex)
            {
                logger.Error($"Exception in {nameof(GetVersion)}, {ex}");
                return "No Version";
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (instance == this)
            {
                instance = null;
            }
            if (contr != null)
            {
                Task.Run(async () =>
                {
                    await contr.Shutdown();
                }).Wait();
            }
        }

        /// <summary>
        /// Live view of what the service is doing, directly in the main window: a line per HandState received
        /// and per decision returned. Safe to call from any thread (REST requests run on background threads);
        /// a no-op before the window exists yet.
        /// </summary>
        public static void LogActivity(string text)
        {
            var form = instance;
            if (form == null || form.IsDisposed)
            {
                return;
            }
            try
            {
                if (form.txtActivity.InvokeRequired)
                {
                    form.txtActivity.BeginInvoke((System.Windows.Forms.MethodInvoker)(() => form.AppendActivityLine(text)));
                }
                else
                {
                    form.AppendActivityLine(text);
                }
            }
            catch
            {
                // the window closed between the null check and the Invoke call; nothing to log to anymore
            }
        }

        private void AppendActivityLine(string text)
        {
            txtActivity.AppendText($"{DateTime.Now:HH:mm:ss.fff}  {text}{Environment.NewLine}");
            // keep memory bounded on a long-running session; trims occasionally, not every line
            if (txtActivity.Lines.Length > MaxActivityLines + 200)
            {
                txtActivity.Lines = txtActivity.Lines[^MaxActivityLines..];
                txtActivity.SelectionStart = txtActivity.TextLength;
                txtActivity.ScrollToCaret();
            }
        }

        private void btnReport_Click(object sender, EventArgs e)
        {
            if (contr == null || contr.Casino == null)
            {
                MessageBox.Show("Not ready yet: the machine registration hasn't loaded.");
                return;
            }
            DisplayHumans form = new DisplayHumans(contr.Casino.Value, contr.CustomerName);
            form.Show();
        }
    }
}
