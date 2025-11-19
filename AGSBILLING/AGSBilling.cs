using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows.Forms;

#nullable disable

namespace AGSBilling
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory("data");
            DataManager.SeedIfMissing();

            GlobalData.Initialize(DataManager.LoadConfig().TotalUnits);
            GlobalData.TryAutoConnect();

            Application.Run(new FormLogin());
        }
    }

    // --- GLOBAL STATE ---
    public static class GlobalData
    {
        public static List<BillingSession> Sessions { get; private set; } = new List<BillingSession>();

        public static void Initialize(int count)
        {
            if (Sessions.Count == 0)
            {
                var cfg = DataManager.LoadConfig();
                for (int i = 0; i < count; i++)
                {
                    string name = (i < cfg.UnitNames.Count) ? cfg.UnitNames[i] : $"Unit {i + 1}";
                    Sessions.Add(new BillingSession { UnitId = i + 1, UnitName = name });
                }
            }
        }
        public static void UpdateUnitNames()
        {
            var cfg = DataManager.LoadConfig();
            for (int i = 0; i < Sessions.Count; i++)
            {
                if (i < cfg.UnitNames.Count) Sessions[i].UnitName = cfg.UnitNames[i];
            }
            while (Sessions.Count < cfg.TotalUnits)
            {
                int id = Sessions.Count + 1;
                string name = (Sessions.Count < cfg.UnitNames.Count) ? cfg.UnitNames[Sessions.Count] : $"Unit {id}";
                Sessions.Add(new BillingSession { UnitId = id, UnitName = name });
            }
        }
        public static void TryAutoConnect()
        {
            if (HardwareControl.IsOpen) return;
            foreach (var port in HardwareControl.GetPorts())
            {
                if (HardwareControl.Open(port)) return;
            }
        }
    }

    #region Models
    public class User
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Staff";
        public override string ToString() => $"{Username} ({Role})";
    }

    public class FnbItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Price { get; set; }
        public int Stock { get; set; }
    }

    public class PricePackage
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Prabayar";
        public double PricePerHour { get; set; }
        public double PackagePrice { get; set; }
        public int PackageDurationMinutes { get; set; }
        public override string ToString()
        {
            if (Type == "Paket") return $"{Name} (Rp {PackagePrice:N0} / {PackageDurationMinutes} mnt)";
            return $"{Name} (Rp {PricePerHour:N0} / jam)";
        }
    }

    public class AppConfig
    {
        public int TotalUnits { get; set; } = 10;
        public List<string> UnitNames { get; set; } = new List<string>();
    }

    public class OrderFnb
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int Qty { get; set; }
        public bool Delivered { get; set; }
        public double PriceEach { get; set; }
        public double Total => PriceEach * Qty;
    }

    public class BillingSession
    {
        public int UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public string Status { get; set; } = "IDLE";
        public PricePackage SelectedPrice { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? TargetEndTime { get; set; }
        public TimeSpan? PausedRemaining { get; set; }
        public TimeSpan Elapsed => (Status == "IDLE" || StartTime == DateTime.MinValue) ? TimeSpan.Zero : DateTime.Now - StartTime;
        public double CurrentRupiah { get; set; }
        public List<OrderFnb> Orders { get; set; } = new List<OrderFnb>();
        public string StaffUsername { get; set; }
    }

    public class TransactionLog
    {
        public DateTime Timestamp { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public string Staff { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public double TotalBill { get; set; }
        public double DurationMins { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
    #endregion

    #region DataManager
    public static class DataManager
    {
        public static string UsersFile = "data/Users.csv";
        public static string FnbFile = "data/FnbStock.csv";
        public static string PricesFile = "data/Prices.csv";
        public static string TransFile = "data/Transactions.csv";
        public static string UnitsFile = "data/Units.csv";
        public static string ConfigFile = "data/Config.txt";

        public static AppConfig LoadConfig()
        {
            var cfg = new AppConfig();
            if (File.Exists(UnitsFile))
            {
                try
                {
                    var lines = File.ReadAllLines(UnitsFile);
                    cfg.UnitNames.Clear();
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var parts = line.Split(',');
                        if (parts.Length >= 2) cfg.UnitNames.Add(parts[1].Trim());
                        else if (parts.Length == 1) cfg.UnitNames.Add(parts[0].Trim());
                    }
                    cfg.TotalUnits = cfg.UnitNames.Count;
                }
                catch { }
            }

            if (cfg.TotalUnits == 0)
            {
                if (File.Exists(ConfigFile))
                {
                    var lines = File.ReadAllLines(ConfigFile);
                    if (lines.Length > 0 && int.TryParse(lines[0], out int t)) cfg.TotalUnits = t;
                    if (lines.Length > 1) cfg.UnitNames = lines[1].Split('|').ToList();
                }
                if (cfg.TotalUnits == 0)
                {
                    cfg.TotalUnits = 10;
                    for (int i = 1; i <= 10; i++) cfg.UnitNames.Add($"Unit {i}");
                }
            }

            while (cfg.UnitNames.Count < cfg.TotalUnits)
                cfg.UnitNames.Add($"Unit {cfg.UnitNames.Count + 1}");

            return cfg;
        }

        public static void SaveConfig(AppConfig cfg)
        {
            while (cfg.UnitNames.Count < cfg.TotalUnits)
                cfg.UnitNames.Add($"Unit {cfg.UnitNames.Count + 1}");

            if (cfg.UnitNames.Count > cfg.TotalUnits)
                cfg.UnitNames = cfg.UnitNames.Take(cfg.TotalUnits).ToList();

            var lines = new List<string>();
            for (int i = 0; i < cfg.UnitNames.Count; i++) lines.Add($"{i + 1},{cfg.UnitNames[i]}");
            File.WriteAllLines(UnitsFile, lines);
            GlobalData.UpdateUnitNames();
        }

        public static List<User> LoadUsers()
        {
            if (!File.Exists(UsersFile)) return new List<User>();
            return File.ReadAllLines(UsersFile).Select(l => l.Split(',')).Where(p => p.Length >= 3)
                .Select(p => new User { Username = p[0].Trim(), Password = p[1].Trim(), Role = p[2].Trim() }).ToList();
        }
        public static List<FnbItem> LoadFnb()
        {
            if (!File.Exists(FnbFile)) return new List<FnbItem>();
            return File.ReadAllLines(FnbFile).Select(l => l.Split(',')).Where(p => p.Length >= 4)
                .Select(p => new FnbItem { Id = p[0].Trim(), Name = p[1].Trim(), Price = double.Parse(p[2]), Stock = int.Parse(p[3]) }).ToList();
        }
        public static List<PricePackage> LoadPrices()
        {
            if (!File.Exists(PricesFile)) return new List<PricePackage>();
            return File.ReadAllLines(PricesFile).Select(l => l.Split(',')).Where(p => p.Length >= 6)
                .Select(p => new PricePackage { Id = p[0].Trim(), Name = p[1].Trim(), Type = p[2].Trim(), PricePerHour = double.Parse(p[3]), PackagePrice = double.Parse(p[4]), PackageDurationMinutes = int.Parse(p[5]) }).ToList();
        }
        public static List<TransactionLog> LoadTransactions()
        {
            if (!File.Exists(TransFile)) return new List<TransactionLog>();
            return File.ReadAllLines(TransFile).Select(l => l.Split(',')).Where(p => p.Length >= 6)
                .Select(p => new TransactionLog
                {
                    Timestamp = DateTime.Parse(p[0]),
                    UnitName = p[1],
                    Staff = p[2],
                    PackageName = p[3],
                    TotalBill = double.Parse(p[4]),
                    DurationMins = double.Parse(p[5]),
                    Notes = p.Length > 6 ? p[6].Replace(";", ",") : ""
                }).ToList();
        }
        public static void SaveUsers(IEnumerable<User> list) => File.WriteAllLines(UsersFile, list.Select(x => $"{x.Username},{x.Password},{x.Role}"));
        public static void SaveFnb(IEnumerable<FnbItem> list) => File.WriteAllLines(FnbFile, list.Select(x => $"{x.Id},{x.Name},{x.Price},{x.Stock}"));
        public static void SavePrices(IEnumerable<PricePackage> list) => File.WriteAllLines(PricesFile, list.Select(x => $"{x.Id},{x.Name},{x.Type},{x.PricePerHour},{x.PackagePrice},{x.PackageDurationMinutes}"));
        public static void AppendTransaction(TransactionLog t) => File.AppendAllText(TransFile, $"{t.Timestamp:O},{t.UnitName},{t.Staff},{t.PackageName},{t.TotalBill},{t.DurationMins},{t.Notes.Replace(",", ";")}{Environment.NewLine}");

        public static void SeedIfMissing()
        {
            if (!File.Exists(UsersFile)) File.WriteAllLines(UsersFile, new[] { "admin,12345,Admin", "staff,staff123,Staff" });
            if (!File.Exists(PricesFile)) File.WriteAllLines(PricesFile, new[] { "P1,Reguler PS3,Prabayar,4000,0,60", "OP1,Open PS3,Pascabayar,4000,0,0" });
            if (!File.Exists(FnbFile)) File.WriteAllLines(FnbFile, new[] { "F1,Mie Goreng,10000,50" });
            if (!File.Exists(UnitsFile))
            {
                var lines = new List<string>();
                for (int i = 1; i <= 10; i++) lines.Add($"{i},Unit {i}");
                File.WriteAllLines(UnitsFile, lines);
            }
        }
    }
    #endregion

    #region HardwareControl
    public static class HardwareControl
    {
        static SerialPort port;
        public static bool IsOpen => port != null && port.IsOpen;
        public static string[] GetPorts() => SerialPort.GetPortNames();
        public static bool Open(string name)
        {
            if (IsOpen) return true;
            try { port = new SerialPort(name, 115200); port.Open(); return true; } catch { return false; }
        }
        public static void Close() { try { port?.Close(); port = null; } catch { } }
        public static void SendSyncData(List<BillingSession> sessions)
        {
            if (!IsOpen || sessions == null) return;
            try
            {
                StringBuilder sb = new StringBuilder("SYNC|");
                foreach (var s in sessions)
                {
                    string timeStr = "OFF";
                    if (s.Status.StartsWith("RUNNING") || s.Status == "TROUBLE")
                    {
                        if (s.Status == "TROUBLE" && s.PausedRemaining.HasValue)
                        {
                            var ts = s.PausedRemaining.Value; timeStr = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
                        }
                        else
                        {
                            TimeSpan ts = s.TargetEndTime.HasValue ? s.TargetEndTime.Value - DateTime.Now : s.Elapsed;
                            if (ts.TotalSeconds < 0) ts = TimeSpan.Zero;
                            timeStr = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
                        }
                    }
                    string fnbStatus = s.Orders.Any(o => !o.Delivered) ? "1" : "0";
                    sb.Append($"{s.UnitId};{s.UnitName};{s.Status};{timeStr};{s.CurrentRupiah};{fnbStatus}|");
                }
                port?.WriteLine(sb.ToString());
            }
            catch { }
        }
        public static void SendRelay(int unitId, bool on) { if (IsOpen) try { port?.WriteLine(on ? $"ON:{unitId}" : $"OFF:{unitId}"); } catch { } }
    }
    #endregion

    #region UI Forms
    public class FormLogin : Form
    {
        TextBox tU, tP;
        public FormLogin()
        {
            Text = "AGS Login"; Size = new Size(350, 220); StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(new Label { Text = "Username:", Left = 30, Top = 30, AutoSize = true });
            tU = new TextBox { Left = 120, Top = 27, Width = 180 };
            Controls.Add(new Label { Text = "Password:", Left = 30, Top = 70, AutoSize = true });
            tP = new TextBox { Left = 120, Top = 67, Width = 180, UseSystemPasswordChar = true };
            var bL = new Button { Text = "LOGIN", Left = 120, Top = 110, Width = 100, Height = 35, BackColor = Color.LightBlue };
            bL.Click += (s, e) => DoLogin();
            Controls.AddRange(new Control[] { tU, tP, bL });
        }
        void DoLogin()
        {
            var users = DataManager.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username.Equals(tU.Text, StringComparison.OrdinalIgnoreCase) && u.Password == tP.Text);
            if (user == null) MessageBox.Show("Username atau Password salah!");
            else
            {
                Hide();
                var main = new FormMain(user);
                main.ShowDialog();
                if (main.IsLogout) { tP.Clear(); this.Show(); } else { this.Close(); }
            }
        }
    }

    public class FormMain : Form
    {
        public bool IsLogout { get; private set; } = false;
        User user;
        AppConfig config;
        List<BillingSession> sessions => GlobalData.Sessions;
        TabControl tabs;
        System.Windows.Forms.Timer timer;

        DataGridView gridStaff;
        ComboBox cbUnits, cbPrices;
        Button btnStart;
        NumericUpDown numUnits;
        DataGridView gridUnitNames, gridAdminUsers, gridAdminPrices, gridAdminFnb;
        DataGridView gridHistory;

        public FormMain(User u)
        {
            user = u;
            config = DataManager.LoadConfig();
            Text = $"AGS Billing - {user.Username} ({user.Role})";
            Size = new Size(1000, 750); StartPosition = FormStartPosition.CenterScreen;

            var pnlHead = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.WhiteSmoke };
            var lblUser = new Label { Text = $"User: {user.Username}", Top = 15, Left = 15, AutoSize = true, Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold) };
            var btnLogout = new Button { Text = "LOGOUT", Top = 8, Left = 880, Width = 90, Height = 35, BackColor = Color.LightCoral, FlatStyle = FlatStyle.Flat };
            btnLogout.Click += (s, e) => { IsLogout = true; if (timer != null) { timer.Stop(); timer.Dispose(); } this.Close(); };
            pnlHead.Controls.Add(lblUser); pnlHead.Controls.Add(btnLogout);
            Controls.Add(pnlHead);

            tabs = new TabControl();
            tabs.Location = new Point(0, 50);
            tabs.Size = new Size(this.ClientSize.Width, this.ClientSize.Height - 50);
            tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            var tStaff = new TabPage("Operasional"); InitStaff(tStaff); tabs.TabPages.Add(tStaff);
            var tHistory = new TabPage("Laporan & Cetak"); InitHistory(tHistory); tabs.TabPages.Add(tHistory);

            if (user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                var tAdmin = new TabPage("Admin Panel"); InitAdmin(tAdmin); tabs.TabPages.Add(tAdmin);
            }
            Controls.Add(tabs);

            TryAutoConnect();
            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (s, e) => { UpdateLogic(); RefreshStaffGrid(); HardwareControl.SendSyncData(sessions); };
            timer.Start();
        }

        void TryAutoConnect()
        {
            foreach (var port in HardwareControl.GetPorts()) if (HardwareControl.Open(port)) return;
        }

        #region Staff Logic
        void InitStaff(TabPage p)
        {
            var lblConn = new Label { Text = "Koneksi Alat (COM):", Top = 15, Left = 15, AutoSize = true };
            var cbPort = new ComboBox { Top = 12, Left = 180, Width = 100 };
            cbPort.Items.AddRange(SerialPort.GetPortNames());
            if (HardwareControl.IsOpen) cbPort.Text = "Connected";

            var bCon = new Button { Text = "Connect", Top = 10, Left = 290, Width = 80, Height = 30 };
            bCon.Click += (s, e) => {
                if (cbPort.Text != "" && cbPort.Text != "Connected")
                {
                    bool ok = HardwareControl.Open(cbPort.Text);
                    if (ok) MessageBox.Show("Terkoneksi!"); else MessageBox.Show("Gagal!");
                }
            };
            p.Controls.AddRange(new Control[] { lblConn, cbPort, bCon });

            gridStaff = new DataGridView { Top = 50, Left = 10, Width = 650, Height = 580, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false };
            gridStaff.CellClick += (s, e) => { if (e.RowIndex >= 0 && e.RowIndex < cbUnits.Items.Count) { cbUnits.SelectedIndex = e.RowIndex; UpdateControlPanelState(sessions[e.RowIndex]); } };

            var panelAct = new GroupBox { Text = "Kontrol Billing", Top = 50, Left = 670, Width = 300, Height = 580 };
            panelAct.Controls.Add(new Label { Text = "1. Pilih Unit:", Top = 30, Left = 10 });
            cbUnits = new ComboBox { Top = 50, Left = 10, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cbUnits.SelectedIndexChanged += (s, e) => { if (cbUnits.SelectedIndex >= 0) UpdateControlPanelState(sessions[cbUnits.SelectedIndex]); };

            panelAct.Controls.Add(new Label { Text = "2. Pilih Paket Harga:", Top = 90, Left = 10 });
            cbPrices = new ComboBox { Top = 110, Left = 10, Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
            RefreshPriceCombo();

            btnStart = new Button { Text = "MULAI (Start)", Top = 150, Left = 10, Width = 130, Height = 40, BackColor = Color.LightGreen };
            var bStop = new Button { Text = "STOP / BAYAR", Top = 150, Left = 150, Width = 130, Height = 40, BackColor = Color.LightCoral };
            var bFnb = new Button { Text = "Tambah F&B (+)", Top = 210, Left = 10, Width = 270, Height = 35 };
            var bCheck = new Button { Text = "Tandai F&B Terkirim (✔)", Top = 255, Left = 10, Width = 270, Height = 35 };
            var bMove = new Button { Text = "Pindah Billing (⇆)", Top = 310, Left = 10, Width = 270, Height = 40, BackColor = Color.LightYellow };

            btnStart.Click += BtnStart_Click;
            bStop.Click += BtnStop_Click;
            bFnb.Click += BtnAddFnb_Click;
            bCheck.Click += BtnFnbDelivered_Click;
            bMove.Click += BtnMove_Click;

            panelAct.Controls.AddRange(new Control[] { cbUnits, cbPrices, btnStart, bStop, bFnb, bCheck, bMove });
            p.Controls.Add(gridStaff);
            p.Controls.Add(panelAct);
        }

        void UpdateControlPanelState(BillingSession s)
        {
            if (s.Status == "TROUBLE") { btnStart.Text = "RESET UNIT"; btnStart.BackColor = Color.LightBlue; }
            else if (s.Status.StartsWith("RUNNING")) { btnStart.Text = "TAMBAH WAKTU"; btnStart.BackColor = Color.Gold; }
            else { btnStart.Text = "MULAI (Start)"; btnStart.BackColor = Color.LightGreen; }
        }

        void RefreshPriceCombo()
        {
            cbPrices.Items.Clear();
            DataManager.LoadPrices().ForEach(x => cbPrices.Items.Add(x));
        }

        void UpdateLogic()
        {
            if (cbUnits.Items.Count != sessions.Count) { cbUnits.Items.Clear(); sessions.ForEach(s => cbUnits.Items.Add(s.UnitName)); }
            foreach (var s in sessions)
            {
                if (s.Status.StartsWith("RUNNING") && s.SelectedPrice != null)
                {
                    double hours = (DateTime.Now - s.StartTime).TotalHours;
                    if (s.SelectedPrice.Type != "Paket") s.CurrentRupiah = Math.Ceiling(hours) * s.SelectedPrice.PricePerHour;
                    if (s.TargetEndTime.HasValue && DateTime.Now >= s.TargetEndTime.Value) StopSession(s, true);
                }
            }
        }

        void RefreshStaffGrid()
        {
            // 1. Init Kolom jika belum ada
            if (gridStaff.Columns.Count == 0)
            {
                gridStaff.Columns.Add("u", "Unit");
                gridStaff.Columns.Add("s", "Status");
                gridStaff.Columns.Add("t", "Waktu");
                gridStaff.Columns.Add("b", "Tagihan (Rp)");
                gridStaff.Columns.Add("f", "F&B Status");
            }

            // 2. Sinkronkan jumlah baris dengan jumlah sesi
            if (gridStaff.RowCount != sessions.Count)
            {
                gridStaff.RowCount = sessions.Count;
            }

            // 3. Update isi sel (Update-In-Place) agar tidak flicker
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                var row = gridStaff.Rows[i];

                string timeStr = "-";
                if (s.Status != "IDLE")
                {
                    if (s.Status == "TROUBLE") timeStr = $"PAUSED ({(s.PausedRemaining.HasValue ? s.PausedRemaining.Value.ToString(@"hh\:mm\:ss") : "--")})";
                    else if (s.TargetEndTime.HasValue)
                    {
                        var left = s.TargetEndTime.Value - DateTime.Now;
                        timeStr = left.TotalSeconds > 0 ? left.ToString(@"hh\:mm\:ss") : "HABIS";
                    }
                    else timeStr = s.Elapsed.ToString(@"hh\:mm\:ss");
                }
                string bill = (s.CurrentRupiah + s.Orders.Sum(o => o.Total)).ToString("N0");
                string fnbStat = s.Orders.Count(o => !o.Delivered) > 0 ? $"BELUM ({s.Orders.Count(o => !o.Delivered)})" : "OK";

                // Update nilai hanya jika berubah (Optional optimization)
                if (Convert.ToString(row.Cells[0].Value) != s.UnitName) row.Cells[0].Value = s.UnitName;
                if (Convert.ToString(row.Cells[1].Value) != s.Status) row.Cells[1].Value = s.Status;
                if (Convert.ToString(row.Cells[2].Value) != timeStr) row.Cells[2].Value = timeStr;
                if (Convert.ToString(row.Cells[3].Value) != bill) row.Cells[3].Value = bill;
                if (Convert.ToString(row.Cells[4].Value) != fnbStat) row.Cells[4].Value = fnbStat;
            }
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            if (cbUnits.SelectedIndex < 0) return;
            var session = sessions[cbUnits.SelectedIndex];
            if (session.Status == "TROUBLE") { session.Status = "IDLE"; session.Orders.Clear(); HardwareControl.SendRelay(session.UnitId, false); UpdateControlPanelState(session); RefreshStaffGrid(); return; }
            if (cbPrices.SelectedIndex < 0) { MessageBox.Show("Pilih Paket."); return; }
            var price = (PricePackage)cbPrices.SelectedItem;

            if (session.Status.StartsWith("RUNNING"))
            {
                if (price.Type != "Pascabayar" && MessageBox.Show("Sudah bayar tambahan?", "Konfirmasi", MessageBoxButtons.YesNo) == DialogResult.No) return;
                int dur = price.PackageDurationMinutes > 0 ? price.PackageDurationMinutes : 60;
                if (session.TargetEndTime.HasValue) session.TargetEndTime = session.TargetEndTime.Value.AddMinutes(dur); else session.TargetEndTime = DateTime.Now.AddMinutes(dur);
                session.CurrentRupiah += (price.PackagePrice > 0 ? price.PackagePrice : price.PricePerHour); MessageBox.Show("Waktu ditambah!"); return;
            }
            if (session.Status != "IDLE") { MessageBox.Show("Unit sibuk!"); return; }
            if (price.Type != "Pascabayar" && MessageBox.Show("Sudah bayar?", "Konfirmasi", MessageBoxButtons.YesNo) == DialogResult.No) return;
            session.SelectedPrice = price; session.StaffUsername = user.Username; session.StartTime = DateTime.Now; session.Orders.Clear();
            if (price.Type == "Pascabayar") { session.Status = "RUNNING_OPEN"; session.TargetEndTime = null; session.CurrentRupiah = 0; } else { session.Status = "RUNNING_TIMER"; session.CurrentRupiah = price.PackagePrice > 0 ? price.PackagePrice : price.PricePerHour; session.TargetEndTime = session.StartTime.AddMinutes(price.PackageDurationMinutes > 0 ? price.PackageDurationMinutes : 60); }
            HardwareControl.SendRelay(session.UnitId, true); UpdateControlPanelState(session); RefreshStaffGrid();
        }

        private void BtnStop_Click(object sender, EventArgs e) { if (cbUnits.SelectedIndex >= 0) StopSession(sessions[cbUnits.SelectedIndex], false); }

        void StopSession(BillingSession s, bool auto)
        {
            if (s.Status == "IDLE" || s.Status == "TROUBLE") return;
            double total = s.CurrentRupiah + s.Orders.Sum(o => o.Total);
            DataManager.AppendTransaction(new TransactionLog { Timestamp = DateTime.Now, UnitName = s.UnitName, Staff = s.StaffUsername ?? "Unknown", PackageName = s.SelectedPrice?.Name ?? "Unknown", TotalBill = total, DurationMins = (DateTime.Now - s.StartTime).TotalMinutes, Notes = "F&B: " + string.Join(";", s.Orders.Select(o => $"{o.ItemName}({o.Qty})")) });
            LoadHistoryGrid();
            string msg = $"{(auto ? "WAKTU HABIS!" : "Sesi Stop")}\nUnit: {s.UnitName}\nTotal: Rp {total:N0}";
            if (s.SelectedPrice?.Type == "Pascabayar") msg += "\n\n!!! TAGIH PELANGGAN !!!";
            MessageBox.Show(msg, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            s.Status = "IDLE"; s.SelectedPrice = null; s.PausedRemaining = null; HardwareControl.SendRelay(s.UnitId, false);
            if (cbUnits.SelectedIndex >= 0) UpdateControlPanelState(sessions[cbUnits.SelectedIndex]); RefreshStaffGrid();
        }

        private void BtnMove_Click(object sender, EventArgs e)
        {
            if (cbUnits.SelectedIndex < 0) return;
            var src = sessions[cbUnits.SelectedIndex];
            if (src.Status == "IDLE") { MessageBox.Show("Pilih Unit AKTIF."); return; }
            Form moveDlg = new Form { Text = "Pindah", Size = new Size(300, 150), StartPosition = FormStartPosition.CenterParent };
            ComboBox cbDest = new ComboBox { Left = 20, Top = 20, Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var s in sessions) if (s.Status == "IDLE") cbDest.Items.Add(s.UnitName);
            Button bOk = new Button { Text = "PILIH", Left = 20, Top = 60, Width = 240, DialogResult = DialogResult.OK };
            moveDlg.Controls.Add(cbDest); moveDlg.Controls.Add(bOk);
            if (moveDlg.ShowDialog() == DialogResult.OK && cbDest.SelectedIndex >= 0)
            {
                var dest = sessions.FirstOrDefault(x => x.UnitName == cbDest.SelectedItem.ToString());
                if (dest != null)
                {
                    var isTrouble = MessageBox.Show($"Apakah {src.UnitName} TROUBLE?", "Cek Kondisi", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    dest.Status = src.Status == "TROUBLE" ? "IDLE" : src.Status;
                    if (dest.Status == "IDLE" && src.SelectedPrice != null) dest.Status = src.SelectedPrice.Type == "Pascabayar" ? "RUNNING_OPEN" : "RUNNING_TIMER";
                    dest.SelectedPrice = src.SelectedPrice; dest.StartTime = src.StartTime; dest.TargetEndTime = src.TargetEndTime;
                    dest.CurrentRupiah = src.CurrentRupiah; dest.Orders = new List<OrderFnb>(src.Orders); dest.StaffUsername = src.StaffUsername;
                    if (src.PausedRemaining.HasValue && dest.TargetEndTime.HasValue) dest.TargetEndTime = DateTime.Now.Add(src.PausedRemaining.Value);
                    HardwareControl.SendRelay(src.UnitId, false); src.Orders.Clear(); src.PausedRemaining = null; src.Status = (isTrouble == DialogResult.Yes) ? "TROUBLE" : "IDLE";
                    HardwareControl.SendRelay(dest.UnitId, true); RefreshStaffGrid();
                }
            }
        }

        private void BtnAddFnb_Click(object sender, EventArgs e)
        {
            if (cbUnits.SelectedIndex < 0) return;
            var s = sessions[cbUnits.SelectedIndex];
            if (s.Status == "IDLE") { MessageBox.Show("Unit belum aktif."); return; }
            Form fnbForm = new Form { Text = "Pilih Menu", Size = new Size(300, 400), StartPosition = FormStartPosition.CenterParent };
            ListBox lb = new ListBox { Dock = DockStyle.Top, Height = 300 };
            var items = DataManager.LoadFnb();
            items.ForEach(x => lb.Items.Add($"{x.Name} - Rp{x.Price} (Stok: {x.Stock})"));
            Button bAdd = new Button { Text = "Tambah", Dock = DockStyle.Bottom, Height = 50 };
            bAdd.Click += (ss, ee) => {
                if (lb.SelectedIndex >= 0)
                {
                    var item = items[lb.SelectedIndex];
                    if (item.Stock <= 0) { MessageBox.Show("Habis!"); return; }
                    if (s.SelectedPrice?.Type != "Pascabayar" && MessageBox.Show($"Bayar {item.Name} sekarang?", "Bayar F&B", MessageBoxButtons.YesNo) == DialogResult.No) return;
                    item.Stock--; DataManager.SaveFnb(items); s.Orders.Add(new OrderFnb { ItemId = item.Id, ItemName = item.Name, PriceEach = item.Price, Qty = 1, Delivered = false });
                    fnbForm.Close();
                }
            };
            fnbForm.Controls.Add(lb); fnbForm.Controls.Add(bAdd); fnbForm.ShowDialog();
        }

        private void BtnFnbDelivered_Click(object sender, EventArgs e)
        {
            if (cbUnits.SelectedIndex < 0) return;
            var s = sessions[cbUnits.SelectedIndex];
            foreach (var o in s.Orders) o.Delivered = true;
            MessageBox.Show("Semua F&B ditandai TERKIRIM.");
        }
        #endregion

        #region History & Admin
        void InitHistory(TabPage p)
        {
            var btnPrint = new Button { Text = "Cetak Laporan", Top = 15, Left = 15, Width = 200, Height = 35, BackColor = Color.LightBlue };
            btnPrint.Click += (s, e) => PrintReport();
            gridHistory = new DataGridView { Top = 60, Left = 10, Width = 950, Height = 550, ReadOnly = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            p.Controls.Add(btnPrint); p.Controls.Add(gridHistory); LoadHistoryGrid();
        }
        void LoadHistoryGrid() => gridHistory.DataSource = DataManager.LoadTransactions().OrderByDescending(x => x.Timestamp).ToList();
        void PrintReport()
        {
            PrintDocument pd = new PrintDocument();
            pd.PrintPage += (s, ev) => {
                float y = 40; Font f = new Font("Arial", 10);
                ev.Graphics.DrawString("LAPORAN TRANSAKSI", new Font("Arial", 14, FontStyle.Bold), Brushes.Black, 250, y); y += 40;
                foreach (var t in (List<TransactionLog>)gridHistory.DataSource) { ev.Graphics.DrawString($"{t.Timestamp:dd/MM HH:mm} - {t.UnitName} - Rp{t.TotalBill:N0}", f, Brushes.Black, 50, y); y += 20; }
            };
            new PrintDialog { Document = pd }.ShowDialog();
        }

        void InitAdmin(TabPage p)
        {
            TabControl adminTabs = new TabControl { Dock = DockStyle.Fill };
            TabPage tUnit = new TabPage("Setting Unit"); InitAdmin_Unit(tUnit); adminTabs.TabPages.Add(tUnit);
            TabPage tPrice = new TabPage("Manajemen Harga"); InitAdmin_Prices(tPrice); adminTabs.TabPages.Add(tPrice);
            TabPage tFnb = new TabPage("Manajemen F&B"); InitAdmin_Fnb(tFnb); adminTabs.TabPages.Add(tFnb);
            TabPage tUser = new TabPage("Manajemen User"); InitAdmin_Users(tUser); adminTabs.TabPages.Add(tUser);
            p.Controls.Add(adminTabs);
        }
        void InitAdmin_Unit(TabPage p)
        {
            p.Controls.Add(new Label { Text = "Jumlah Unit:", Top = 20, Left = 20 });
            numUnits = new NumericUpDown { Top = 18, Left = 150, Minimum = 1, Maximum = 50, Value = config.TotalUnits };
            var btnSaveCount = new Button { Text = "Set Jumlah", Top = 17, Left = 280 };
            btnSaveCount.Click += (s, e) => { config.TotalUnits = (int)numUnits.Value; DataManager.SaveConfig(config); MessageBox.Show("Disimpan. Restart App."); };
            p.Controls.Add(new Label { Text = "Nama Unit:", Top = 60, Left = 20 });
            gridUnitNames = new DataGridView { Top = 90, Left = 20, Width = 400, Height = 400, AllowUserToAddRows = false };
            gridUnitNames.Columns.Add("idx", "Index"); gridUnitNames.Columns.Add("name", "Nama");
            for (int i = 0; i < 50; i++) { string n = (i < config.UnitNames.Count) ? config.UnitNames[i] : $"Unit {i + 1}"; gridUnitNames.Rows.Add(i + 1, n); }
            var btnSaveNames = new Button { Text = "Simpan Nama", Top = 500, Left = 20, Width = 400, Height = 40 };
            btnSaveNames.Click += (s, e) => {
                config.UnitNames.Clear(); foreach (DataGridViewRow r in gridUnitNames.Rows) if (r.Cells[1].Value != null) config.UnitNames.Add(r.Cells[1].Value.ToString());
                DataManager.SaveConfig(config); MessageBox.Show("Nama Disimpan. Restart App.");
            };
            p.Controls.AddRange(new Control[] { numUnits, btnSaveCount, gridUnitNames, btnSaveNames });
        }
        void InitAdmin_Prices(TabPage p)
        {
            gridAdminPrices = new DataGridView { Dock = DockStyle.Top, Height = 500, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            gridAdminPrices.DataSource = new BindingSource { DataSource = new BindingList<PricePackage>(DataManager.LoadPrices()) };
            var btnSave = new Button { Text = "Simpan Harga", Dock = DockStyle.Bottom, Height = 50, BackColor = Color.LightGreen };
            btnSave.Click += (s, e) => {
                if (gridAdminPrices.DataSource is BindingSource bs && bs.DataSource is BindingList<PricePackage> list) { DataManager.SavePrices(list); RefreshPriceCombo(); MessageBox.Show("Tersimpan!"); }
            };
            p.Controls.Add(gridAdminPrices); p.Controls.Add(btnSave);
        }
        void InitAdmin_Fnb(TabPage p)
        {
            gridAdminFnb = new DataGridView { Dock = DockStyle.Top, Height = 500, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            gridAdminFnb.DataSource = new BindingSource { DataSource = new BindingList<FnbItem>(DataManager.LoadFnb()) };
            var btnSave = new Button { Text = "Simpan F&B", Dock = DockStyle.Bottom, Height = 50, BackColor = Color.LightGreen };
            btnSave.Click += (s, e) => {
                if (gridAdminFnb.DataSource is BindingSource bs && bs.DataSource is BindingList<FnbItem> list) { DataManager.SaveFnb(list); MessageBox.Show("Tersimpan!"); }
            };
            p.Controls.Add(gridAdminFnb); p.Controls.Add(btnSave);
        }
        void InitAdmin_Users(TabPage p)
        {
            gridAdminUsers = new DataGridView { Dock = DockStyle.Top, Height = 500, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            gridAdminUsers.DataSource = new BindingSource { DataSource = new BindingList<User>(DataManager.LoadUsers()) };
            var btnSave = new Button { Text = "Simpan User", Dock = DockStyle.Bottom, Height = 50, BackColor = Color.LightGreen };
            btnSave.Click += (s, e) => {
                if (gridAdminUsers.DataSource is BindingSource bs && bs.DataSource is BindingList<User> list) { DataManager.SaveUsers(list); MessageBox.Show("Tersimpan!"); }
            };
            p.Controls.Add(gridAdminUsers); p.Controls.Add(btnSave);
        }
        #endregion
    }
    #endregion
}