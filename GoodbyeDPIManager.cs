using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace GoodbyeDPIManager
{
    static class Program
    {
        private const string MutexName = "Global\\GoodbyeDPI_Manager_SingleInstance_Mutex";
        public const string WindowTitle = "GoodbyeDPI Türkiye Yöneticisi";

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Yönetici yetkisi kontrolü
            if (!IsAdministrator())
            {
                ElevateToAdmin(args);
                return;
            }

            // Tekil çalışan uygulama kontrolü (Single Instance)
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    IntPtr hWnd = FindWindow(null, WindowTitle);
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                    }
                    return;
                }

                bool startInTray = args.Any(a => 
                    a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("-tray", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("/tray", StringComparison.OrdinalIgnoreCase));

                Application.Run(new MainForm(startInTray));
            }
        }

        private static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static void ElevateToAdmin(string[] args)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = string.Join(" ", args),
                Verb = "runas",
                UseShellExecute = true
            };
            try
            {
                Process.Start(psi);
            }
            catch
            {
                MessageBox.Show("GoodbyeDPI servislerini yönetebilmek için uygulamanın Yönetici olarak çalıştırılması gerekmektedir.",
                    "Yönetici İzni Gerekli", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    public class MainForm : Form
    {
        private const string ServiceName = "GoodbyeDPI";
        private const string ScheduledTaskName = "GoodbyeDPIManager";

        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayContextMenu;

        // UI Bileşenleri
        private Panel headerPanel;
        private Label lblTitle;
        private Label lblSubtitle;
        private Label lblArchBadge;

        private Panel cardStatus;
        private Label lblStatusHeader;
        private Label lblStatusBadge;
        private Label lblSessionStatus;
        private Label lblBootStatus;
        private Button btnToggleDpi;

        private Panel cardSettings;
        private Label lblSettingsHeader;
        private CheckBox chkStartWithWindows;
        private Label lblStartWithWindowsHint;
        private CheckBox chkMinimizeToTray;
        private Label lblMinimizeToTrayHint;

        private Panel cardInfo;
        private Label lblInfoDns;
        private Label lblInfoMode;
        private Button btnHideToTray;
        private Button btnExitApp;

        private bool isBusy = false;
        private bool startInTray = false;
        private bool reallyExit = false;

        public MainForm(bool startInTray)
        {
            this.startInTray = startInTray;
            InitializeComponent();
            SetupTray();
            LoadSettings();
            UpdateStatus();
        }

        private void InitializeComponent()
        {
            this.Text = Program.WindowTitle;
            this.Size = new Size(540, 620);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(15, 23, 42); // Slate-900
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.ForeColor = Color.White;
            this.DoubleBuffered = true;

            // Header Panel
            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 85,
                BackColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(24, 16, 24, 8)
            };

            lblTitle = new Label
            {
                Text = "GoodbyeDPI Türkiye",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 16),
                AutoSize = true
            };

            lblArchBadge = new Label
            {
                Text = Environment.Is64BitOperatingSystem ? "x64 (64-Bit)" : "x86 (32-Bit)",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                BackColor = Color.FromArgb(12, 74, 110),
                Location = new Point(248, 21),
                Padding = new Padding(6, 2, 6, 2),
                AutoSize = true
            };

            lblSubtitle = new Label
            {
                Text = "DPI Atlatma, Sansür Aşma ve Güvenli DNS Redirection Yöneticisi",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, 48),
                AutoSize = true
            };

            headerPanel.Controls.Add(lblTitle);
            headerPanel.Controls.Add(lblArchBadge);
            headerPanel.Controls.Add(lblSubtitle);
            this.Controls.Add(headerPanel);

            // 1. Durum Kartı (Status Card)
            cardStatus = CreateCard(24, 95, 476, 205);

            lblStatusHeader = new Label
            {
                Text = "DPI KORUMA VE SERVİS DURUMU",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, 16),
                AutoSize = true
            };

            lblStatusBadge = new Label
            {
                Text = "DURUM DENETLENİYOR...",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(20, 38),
                Size = new Size(436, 32),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };

            lblSessionStatus = new Label
            {
                Text = "O anki oturum: Servis durumu alınıyor...",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(20, 78),
                AutoSize = true
            };

            lblBootStatus = new Label
            {
                Text = "Yeniden başlatma: Kalıcılık durumu taranıyor...",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 102),
                AutoSize = true
            };

            btnToggleDpi = new Button
            {
                Text = "Lütfen bekleyin...",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(30, 41, 59),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(20, 135),
                Size = new Size(436, 48),
                Cursor = Cursors.Hand
            };
            btnToggleDpi.FlatAppearance.BorderSize = 0;
            btnToggleDpi.Click += (s, e) => ToggleDpiService();

            cardStatus.Controls.Add(lblStatusHeader);
            cardStatus.Controls.Add(lblStatusBadge);
            cardStatus.Controls.Add(lblSessionStatus);
            cardStatus.Controls.Add(lblBootStatus);
            cardStatus.Controls.Add(btnToggleDpi);
            this.Controls.Add(cardStatus);

            // 2. Ayarlar Kartı (Settings Card)
            cardSettings = CreateCard(24, 312, 476, 135);

            lblSettingsHeader = new Label
            {
                Text = "BAŞLANGIÇ VE ÇALIŞMA TERCİHLERİ",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, 14),
                AutoSize = true
            };

            chkStartWithWindows = new CheckBox
            {
                Text = "Bilgisayar açıldığında otomatik başlat (Sistem Tepsisinde)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 36),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            chkStartWithWindows.CheckedChanged += (s, e) => ToggleStartup(chkStartWithWindows.Checked);

            lblStartWithWindowsHint = new Label
            {
                Text = "İşaretlendiğinde uygulama Windows başlangıcında arka planda tepside açılır.",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(40, 58),
                AutoSize = true
            };

            chkMinimizeToTray = new CheckBox
            {
                Text = "Pencere kapatıldığında [X] sistem tepsisine küçült",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.White,
                Location = new Point(20, 82),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            chkMinimizeToTray.CheckedChanged += (s, e) => SaveSettings();

            lblMinimizeToTrayHint = new Label
            {
                Text = "Kapatma tuşuna bastığınızda uygulama tepside çalışmaya devam eder.",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(40, 104),
                AutoSize = true
            };

            cardSettings.Controls.Add(lblSettingsHeader);
            cardSettings.Controls.Add(chkStartWithWindows);
            cardSettings.Controls.Add(lblStartWithWindowsHint);
            cardSettings.Controls.Add(chkMinimizeToTray);
            cardSettings.Controls.Add(lblMinimizeToTrayHint);
            this.Controls.Add(cardSettings);

            // 3. Bilgi & Eylem Kartı (Info Card)
            cardInfo = CreateCard(24, 460, 476, 100);

            lblInfoDns = new Label
            {
                Text = "• Mod: Türkiye Özel Modu (-5)  |  Hedef DNS: Yandex (77.88.8.8:1253)",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 14),
                AutoSize = true
            };

            lblInfoMode = new Label
            {
                Text = "• Yaptığınız aktif/pasif tercihi bilgisayar yeniden başlasa da korunur.",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 34),
                AutoSize = true
            };

            btnHideToTray = new Button
            {
                Text = "Sistem Tepsisine Gizle",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(203, 213, 225),
                BackColor = Color.FromArgb(30, 41, 59),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(18, 58),
                Size = new Size(210, 30),
                Cursor = Cursors.Hand
            };
            btnHideToTray.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            btnHideToTray.Click += (s, e) => HideToTray();

            btnExitApp = new Button
            {
                Text = "Uygulamadan Çık",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(248, 113, 113),
                BackColor = Color.FromArgb(30, 41, 59),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(248, 58),
                Size = new Size(210, 30),
                Cursor = Cursors.Hand
            };
            btnExitApp.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            btnExitApp.Click += (s, e) => ExitApplication();

            cardInfo.Controls.Add(lblInfoDns);
            cardInfo.Controls.Add(lblInfoMode);
            cardInfo.Controls.Add(btnHideToTray);
            cardInfo.Controls.Add(btnExitApp);
            this.Controls.Add(cardInfo);

            this.FormClosing += MainForm_FormClosing;
        }

        private Panel CreateCard(int x, int y, int width, int height)
        {
            Panel p = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Color.FromArgb(30, 41, 59) // Slate-800
            };
            p.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Color.FromArgb(51, 65, 85), 1)) // Slate-700
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
            };
            return p;
        }

        private void SetupTray()
        {
            trayContextMenu = new ContextMenuStrip();
            trayContextMenu.Font = new Font("Segoe UI", 9f);
            trayContextMenu.ForeColor = Color.FromArgb(241, 245, 249);
            trayContextMenu.Renderer = new DarkMenuRenderer();

            ToolStripMenuItem itemHeader = new ToolStripMenuItem("GoodbyeDPI Türkiye") { Enabled = false };
            itemHeader.Font = new Font("Segoe UI", 9f, FontStyle.Bold);

            ToolStripMenuItem itemStatus = new ToolStripMenuItem("Durum: Kontrol ediliyor...") { Name = "itemStatus", Enabled = false };

            ToolStripSeparator sep1 = new ToolStripSeparator();

            ToolStripMenuItem itemToggle = new ToolStripMenuItem("DPI'ı Aç / Kapat") { Name = "itemToggle" };
            itemToggle.Click += (s, e) => ToggleDpiService();

            ToolStripMenuItem itemOpen = new ToolStripMenuItem("Arayüzü Göster");
            itemOpen.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            itemOpen.Click += (s, e) => ShowMainWindow();

            ToolStripSeparator sep2 = new ToolStripSeparator();

            ToolStripMenuItem itemRestartService = new ToolStripMenuItem("Servisi Yeniden Başlat");
            itemRestartService.Click += (s, e) => RestartDpiService();

            ToolStripMenuItem itemStartup = new ToolStripMenuItem("Windows ile Başlat") { Name = "itemStartup", CheckOnClick = true };
            itemStartup.Click += (s, e) =>
            {
                chkStartWithWindows.Checked = itemStartup.Checked;
            };

            ToolStripSeparator sep3 = new ToolStripSeparator();

            ToolStripMenuItem itemExit = new ToolStripMenuItem("Uygulamadan Çık");
            itemExit.Click += (s, e) => ExitApplication();

            trayContextMenu.Items.AddRange(new ToolStripItem[] {
                itemHeader,
                itemStatus,
                sep1,
                itemToggle,
                itemOpen,
                itemRestartService,
                sep2,
                itemStartup,
                sep3,
                itemExit
            });

            notifyIcon = new NotifyIcon
            {
                Text = "GoodbyeDPI Yöneticisi",
                ContextMenuStrip = trayContextMenu,
                Visible = true
            };

            notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowMainWindow();
                }
            };
            notifyIcon.DoubleClick += (s, e) =>
            {
                ShowMainWindow();
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (startInTray)
            {
                BeginInvoke(new Action(() =>
                {
                    this.Hide();
                    this.ShowInTaskbar = false;
                    notifyIcon.ShowBalloonTip(2000, "GoodbyeDPI Türkiye",
                        "Uygulama arka planda sistem tepsisinde çalışıyor.", ToolTipIcon.Info);
                }));
            }
        }

        private bool balloonShown = false;
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!reallyExit && chkMinimizeToTray.Checked && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                if (!balloonShown)
                {
                    notifyIcon.ShowBalloonTip(2000, "GoodbyeDPI Türkiye",
                        "Uygulama sistem tepsisinde çalışıyor. Açmak için simgeye tıklayın.", ToolTipIcon.Info);
                    balloonShown = true;
                }
            }
        }

        private void HideToTray()
        {
            this.Hide();
            this.ShowInTaskbar = false;
        }

        private void ShowMainWindow()
        {
            UpdateStatus();
            this.Show();
            this.ShowInTaskbar = true;
            if (this.WindowState == FormWindowState.Minimized)
                this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }

        private void ExitApplication()
        {
            reallyExit = true;
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            Application.Exit();
        }

        // ==================== DURUM & SERVİS YÖNETİMİ ====================

        public void UpdateStatus()
        {
            if (isBusy) return;

            bool isRunning = IsServiceRunning(ServiceName);
            bool isInstalled = IsServiceInstalled(ServiceName);

            // Tray Menüsü Öğelerini Güncelle
            ToolStripItem itemStatus = trayContextMenu.Items["itemStatus"];
            ToolStripItem itemToggle = trayContextMenu.Items["itemToggle"];
            ToolStripMenuItem itemStartup = (ToolStripMenuItem)trayContextMenu.Items["itemStartup"];

            bool isStartup = IsTaskScheduled(ScheduledTaskName);
            if (chkStartWithWindows.Checked != isStartup)
            {
                chkStartWithWindows.CheckedChanged -= (s, e) => ToggleStartup(chkStartWithWindows.Checked);
                chkStartWithWindows.Checked = isStartup;
                chkStartWithWindows.CheckedChanged += (s, e) => ToggleStartup(chkStartWithWindows.Checked);
            }
            if (itemStartup != null) itemStartup.Checked = isStartup;

            Icon dynamicIcon = CreateStatusIcon(isRunning);
            string appIconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(appIconPath))
            {
                try { this.Icon = new Icon(appIconPath); } catch { this.Icon = dynamicIcon; }
            }
            else
            {
                this.Icon = dynamicIcon;
            }
            notifyIcon.Icon = dynamicIcon;

            if (isRunning)
            {
                // AKTİF DURUM
                lblStatusBadge.Text = "● DPI KORUMASI AKTİF (ÇALIŞIYOR)";
                lblStatusBadge.ForeColor = Color.FromArgb(52, 211, 153); // Emerald-400
                lblStatusBadge.BackColor = Color.FromArgb(6, 78, 59);   // Emerald-900

                lblSessionStatus.Text = "O anki oturum: DPI servisi çalışıyor, DNS engelleri aşılıyor.";
                lblBootStatus.Text = "Yeniden başlatma: Otomatik Başlayacak (Kalıcı olarak devrede).";

                btnToggleDpi.Text = "DPI Korumasını Durdur (Kapat)";
                btnToggleDpi.BackColor = Color.FromArgb(185, 28, 28); // Red-700
                btnToggleDpi.ForeColor = Color.White;

                notifyIcon.Text = "GoodbyeDPI: Aktif (Çalışıyor)";
                if (itemStatus != null) itemStatus.Text = "Durum: AKTİF (Çalışıyor)";
                if (itemToggle != null) itemToggle.Text = "DPI'ı Kapat";
            }
            else
            {
                // PASİF DURUM
                lblStatusBadge.Text = "○ DPI KORUMASI PASİF (KAPALI)";
                lblStatusBadge.ForeColor = Color.FromArgb(248, 113, 113); // Red-400
                lblStatusBadge.BackColor = Color.FromArgb(69, 26, 26);   // Dark Red

                lblSessionStatus.Text = isInstalled 
                    ? "O anki oturum: Servis durdurulmuş vaziyette." 
                    : "O anki oturum: Servis kurulu değil, DPI atlatma kapalı.";
                lblBootStatus.Text = "Yeniden başlatma: Bilgisayar açıldığında kapalı kalacak.";

                btnToggleDpi.Text = "DPI Korumasını Başlat (Aktif Et)";
                btnToggleDpi.BackColor = Color.FromArgb(16, 185, 129); // Emerald-500
                btnToggleDpi.ForeColor = Color.White;

                notifyIcon.Text = "GoodbyeDPI: Pasif (Kapalı)";
                if (itemStatus != null) itemStatus.Text = "Durum: PASİF (Kapalı)";
                if (itemToggle != null) itemToggle.Text = "DPI'ı Aç";
            }
        }

        private void ToggleDpiService()
        {
            if (isBusy) return;
            isBusy = true;

            bool isRunning = IsServiceRunning(ServiceName);
            btnToggleDpi.Text = isRunning ? "Servis durduruluyor..." : "Servis kuruluyor ve başlatılıyor...";
            btnToggleDpi.Enabled = false;

            Thread worker = new Thread(() =>
            {
                try
                {
                    if (isRunning)
                    {
                        // PASİF YAP - service_remove.cmd adımları
                        ExecuteServiceRemove();
                    }
                    else
                    {
                        // AKTİF YAP - service_install_dnsredir_turkey.cmd adımları
                        ExecuteServiceInstall();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("İşlem sırasında bir hata oluştu: " + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    this.Invoke(new Action(() =>
                    {
                        isBusy = false;
                        btnToggleDpi.Enabled = true;
                        UpdateStatus();
                    }));
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void RestartDpiService()
        {
            if (isBusy) return;
            isBusy = true;
            btnToggleDpi.Text = "Servis yeniden başlatılıyor...";
            btnToggleDpi.Enabled = false;

            Thread worker = new Thread(() =>
            {
                try
                {
                    ExecuteServiceRemove();
                    Thread.Sleep(500);
                    ExecuteServiceInstall();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Yeniden başlatma hatası: " + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    this.Invoke(new Action(() =>
                    {
                        isBusy = false;
                        btnToggleDpi.Enabled = true;
                        UpdateStatus();
                    }));
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void ExecuteServiceInstall()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
            string exePath = Path.Combine(appDir, arch, "goodbyedpi.exe");

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("goodbyedpi.exe dosyası bulunamadı:\n" + exePath);
            }

            // 1. Önceki servisleri durdur ve temizle
            RunCmd("cmd.exe", "/c sc stop GoodbyeDPI");
            RunCmd("cmd.exe", "/c sc delete GoodbyeDPI");

            // 2. Türkiye DNS Redirection ile otomatik servis olarak oluştur
            // service_install_dnsredir_turkey.cmd komutunun birebir karşılığı
            string binPathArg = string.Format("\\\"{0}\\\" -5 --set-ttl 5 --dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253", exePath);
            string createCmd = string.Format("sc create GoodbyeDPI binPath= \"{0}\" start= auto", binPathArg);
            RunCmd("cmd.exe", "/c " + createCmd);

            // 3. Açıklama ekle
            RunCmd("cmd.exe", "/c sc description GoodbyeDPI \"Turkiye icin DNS zorlamasini kaldirir.\"");

            // 4. Servisi başlat
            RunCmd("cmd.exe", "/c sc start GoodbyeDPI");

            Thread.Sleep(800);
        }

        private void ExecuteServiceRemove()
        {
            // service_remove.cmd komutlarının birebir karşılığı
            RunCmd("cmd.exe", "/c sc stop GoodbyeDPI");
            RunCmd("cmd.exe", "/c sc delete GoodbyeDPI");
            RunCmd("cmd.exe", "/c sc stop WinDivert");
            RunCmd("cmd.exe", "/c sc delete WinDivert");
            RunCmd("cmd.exe", "/c sc stop WinDivert14");
            RunCmd("cmd.exe", "/c sc delete WinDivert14");

            Thread.Sleep(800);
        }

        private static void RunCmd(string cmd, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit(10000);
            }
        }

        private bool IsServiceInstalled(string serviceName)
        {
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                return services.Any(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private bool IsServiceRunning(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    return sc.Status == ServiceControllerStatus.Running;
                }
            }
            catch
            {
                return false;
            }
        }

        // ==================== WINDOWS BAŞLANGIÇ YÖNETİMİ ====================

        private bool IsTaskScheduled(string taskName)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = string.Format("/query /tn \"{0}\"", taskName),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(5000);
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void ToggleStartup(bool enable)
        {
            try
            {
                string exePath = Application.ExecutablePath;
                if (enable)
                {
                    // UAC uyarısı olmadan en yüksek yetkiyle oturum açılışında tepside başlat
                    string trCmd = string.Format("\\\"{0}\\\" --tray", exePath);
                    string taskCmd = string.Format("schtasks /create /tn \"{0}\" /tr \"{1}\" /sc onlogon /rl highest /f", ScheduledTaskName, trCmd);
                    RunCmd("cmd.exe", "/c " + taskCmd);
                }
                else
                {
                    RunCmd("cmd.exe", string.Format("/c schtasks /delete /tn \"{0}\" /f", ScheduledTaskName));
                }
                SaveSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Başlangıç ayarı değiştirilemedi: " + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ==================== AYARLAR & İKON ÇİZİMİ ====================

        private void LoadSettings()
        {
            string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
            bool minimize = true;
            if (File.Exists(settingsFile))
            {
                string[] lines = File.ReadAllLines(settingsFile);
                foreach (string line in lines)
                {
                    if (line.StartsWith("MinimizeToTray=", StringComparison.OrdinalIgnoreCase))
                    {
                        bool val;
                        if (bool.TryParse(line.Substring("MinimizeToTray=".Length), out val))
                            minimize = val;
                    }
                }
            }
            chkMinimizeToTray.Checked = minimize;
            chkStartWithWindows.Checked = IsTaskScheduled(ScheduledTaskName);
        }

        private void SaveSettings()
        {
            try
            {
                string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
                string content = string.Format("MinimizeToTray={0}\r\nStartWithWindows={1}\r\n", 
                    chkMinimizeToTray.Checked, chkStartWithWindows.Checked);
                File.WriteAllText(settingsFile, content);
            }
            catch { }
        }

        private Icon CreateStatusIcon(bool active)
        {
            try
            {
                string iconName = active ? "tray_active.ico" : "tray_inactive.ico";
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconName);
                if (File.Exists(iconPath))
                {
                    return new Icon(iconPath, 32, 32);
                }
            }
            catch { }

            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Kalkan (Shield) veya Modern Daire İkonu Fallback
                Color baseColor = active ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
                Color glowColor = active ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);

                using (SolidBrush brush = new SolidBrush(baseColor))
                {
                    g.FillEllipse(brush, 2, 2, 28, 28);
                }

                using (Pen pen = new Pen(glowColor, 2))
                {
                    g.DrawEllipse(pen, 2, 2, 28, 28);
                }

                // İç Sembol
                if (active)
                {
                    // Tik işareti
                    using (Pen checkPen = new Pen(Color.White, 3))
                    {
                        checkPen.StartCap = LineCap.Round;
                        checkPen.EndCap = LineCap.Round;
                        g.DrawLines(checkPen, new Point[] {
                            new Point(8, 16),
                            new Point(14, 22),
                            new Point(24, 10)
                        });
                    }
                }
                else
                {
                    // Çarpı işareti
                    using (Pen xPen = new Pen(Color.White, 3))
                    {
                        xPen.StartCap = LineCap.Round;
                        xPen.EndCap = LineCap.Round;
                        g.DrawLine(xPen, 10, 10, 22, 22);
                        g.DrawLine(xPen, 22, 10, 10, 22);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                return (Icon)Icon.FromHandle(hIcon).Clone();
            }
        }
    }

    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Name == "itemStatus")
            {
                if (e.Text.Contains("AKTİF"))
                    e.TextColor = Color.FromArgb(52, 211, 153); // Emerald-400 (Canlı Yeşil)
                else if (e.Text.Contains("PASİF"))
                    e.TextColor = Color.FromArgb(248, 113, 113); // Red-400 (Canlı Kırmızı)
                else
                    e.TextColor = Color.FromArgb(148, 163, 184);
            }
            else if (!e.Item.Enabled)
            {
                e.TextColor = Color.FromArgb(56, 189, 248); // Sky-400 (Başlık)
            }
            else if (e.Item.Selected)
            {
                e.TextColor = Color.White;
            }
            else
            {
                e.TextColor = Color.FromArgb(241, 245, 249); // Slate-100 (Beyaz)
            }

            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                Rectangle rc = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(30, 41, 59))) // Slate-800
                using (Pen pen = new Pen(Color.FromArgb(56, 189, 248), 1)) // Sky-400 Çerçeve
                {
                    e.Graphics.FillRectangle(brush, rc);
                    e.Graphics.DrawRectangle(pen, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);
                }
            }
            else
            {
                base.OnRenderMenuItemBackground(e);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(e.ImageRectangle.X + 2, e.ImageRectangle.Y + 2, e.ImageRectangle.Width - 4, e.ImageRectangle.Height - 4);
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(16, 185, 129)))
            {
                e.Graphics.FillRectangle(brush, rect);
            }
            using (Pen pen = new Pen(Color.White, 2))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new Point[] {
                    new Point(rect.Left + 2, rect.Top + 6),
                    new Point(rect.Left + 5, rect.Top + 9),
                    new Point(rect.Left + 10, rect.Top + 3)
                });
            }
        }
    }

    public class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder { get { return Color.FromArgb(51, 65, 85); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(15, 23, 42); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(30, 41, 59); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(30, 41, 59); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(30, 41, 59); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(56, 189, 248); } }
        public override Color SeparatorDark { get { return Color.FromArgb(51, 65, 85); } }
        public override Color SeparatorLight { get { return Color.Transparent; } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(15, 23, 42); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(15, 23, 42); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(15, 23, 42); } }
        public override Color CheckBackground { get { return Color.FromArgb(30, 41, 59); } }
        public override Color CheckSelectedBackground { get { return Color.FromArgb(51, 65, 85); } }
        public override Color CheckPressedBackground { get { return Color.FromArgb(15, 23, 42); } }
    }
}
