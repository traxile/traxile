﻿using log4net;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Media;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace TraXile
{
    public enum ACTIVITY_TYPES
    {
        MAP,
        HEIST,
        LABYRINTH,
        SIMULACRUM,
        BLIGHTED_MAP,
        DELVE,
        TEMPLE
    }

    public partial class MainW : Form
    {
        private string _poeLogFilePath;
        private string _currentArea;
        private string _currentInstanceEndpoint;
        private int _lastHash = 0;
        private double _logLinesTotal;
        private double _logLinesRead;
        private Thread _logParseThread;
        private Thread _eventThread;
        private DateTime _inAreaSince;
        private DateTime _lastDeathTime;
        private DateTime _initStartTime;
        private DateTime _initEndTime;
        private EVENT_TYPES _lastEventType;
        private TrackedActivity _currentActivity;
        private bool _eventQueueInitizalized;
        private bool _isMapZana;
        private bool _exit;
        private bool _listViewInitielaized;
        private bool _elderFightActive;
        private bool _showGridInActLog;
        private bool _restoreMode;
        private bool _labDashboardUpdateRequested;
        private int _shaperKillsInFight;
        private int _nextAreaLevel;
        private int _currentAreaLevel;
        private int _timeCapLab = 3600;
        private int _timeCapMap = 3600;
        private int _timeCapHeist = 3600;
        private SqliteConnection _dbConnection;
        private bool _historyInitialized;
        private List<string> _knownPlayerNames;
        private BindingList<string> _backups;
        private Dictionary<int, string> _dict;
        private Dictionary<string, int> _numericStats;
        private Dictionary<string, string> _statNamesLong;
        private List<string> labs;
        private LoadScreen _loadScreenWindow;
        private List<TrackedActivity> _eventHistory;
        private ConcurrentQueue<TrackingEvent> _eventQueue;
        private List<ActivityTag> _tags;
        private Dictionary<string, Label> _tagLabels, _tagLabelsConfig;
        private EventMapping _eventMapping;
        private DefaultMappings _defaultMappings;
        private List<string> _parsedActivities;
        private ILog _log;
        private bool _showGridInStats;
        private bool _UpdateCheckDone;
        private string _lastSimuEndpoint;
        private TxSettingsManager _mySettings;

        private ListViewManager _lvmStats, _lvmActlog;
        private bool _restoreOk = true;
        private string _failedRestoreReason = "";
        private string _dbPath;
        private string _cachePath;
        private string _myAppData;
        private bool _mapDashboardUpdateRequested;
        private bool _labDashboardHideUnknown;
        private bool _globalDashboardUpdateRequested;
        private bool _heistDashboardUpdateRequested;

        /// <summary>
        /// Setting Property for LogFilePath
        /// </summary>
        public string SettingPoeLogFilePath
        {
            get { return ReadSetting("poe_logfile_path", null); }
            set {AddUpdateAppSettings("poe_logfile_path", value); }
        }

        /// <summary>
        /// Tag list property
        /// </summary>
        public List<ActivityTag> Tags
        {
            get { return _tags; }
            set { _tags = value; }
        }

        /// <summary>
        /// Main Window Constructor
        /// </summary>
        public MainW()
        {
            //TEST: Create folder in userdata
            _myAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\" + APPINFO.NAME;
            _dbPath = _myAppData + @"\data.db";
            _cachePath = _myAppData + @"\stats.cache";
            _mySettings = new TxSettingsManager(_myAppData + @"\config.xml");
            _poeLogFilePath = _mySettings.ReadSetting("poe_logfile_path", null);

            if(!Directory.Exists(_myAppData))
            {
                Directory.CreateDirectory(_myAppData);
            }
                       
            this.Visible = false;
            InitializeComponent();
            Init();

        }

        /// <summary>
        /// Check if a new version is available on GitHub and ask for update.
        /// </summary>
        /// <param name="b_notify_ok"></param>
        private void CheckForUpdate(bool b_notify_ok = false)
        {
            try
            {
                string GITHUB_API = "https://api.github.com/repos/{0}/{1}/releases/latest";
                WebClient webClient = new WebClient();
                webClient.Headers.Add("User-Agent", "Unity web player");
                Uri uri = new Uri(string.Format(GITHUB_API, "dermow", "TraXile"));
                string releases = webClient.DownloadString(uri);
                int iIndex = releases.IndexOf("tag_name");
                string sVersion =  releases.Substring(iIndex + 11, 5);

                int MyMajor = Convert.ToInt32(APPINFO.VERSION.Split('.')[0]);
                int MyMinor = Convert.ToInt32(APPINFO.VERSION.Split('.')[1]);
                int MyPatch = Convert.ToInt32(APPINFO.VERSION.Split('.')[2]);

                int RemoteMajor = Convert.ToInt32(sVersion.Split('.')[0]);
                int RemoteMinor = Convert.ToInt32(sVersion.Split('.')[1]);
                int RemotePatch = Convert.ToInt32(sVersion.Split('.')[2]);

                bool bUpdate = false;
                if(RemoteMajor > MyMajor)
                {
                    bUpdate = true;
                }
                else if(RemoteMajor == MyMajor && RemoteMinor > MyMinor)
                {
                    bUpdate = true;
                }
                else if(RemoteMajor == MyMajor && RemoteMinor == MyMinor && RemotePatch > MyPatch)
                {
                    bUpdate = true;
                }

                if (bUpdate)
                {
                    if(MessageBox.Show("There is a new version available for TraXile (current=" + APPINFO.VERSION + ", new=" + sVersion + ")"
                        + Environment.NewLine + Environment.NewLine + "Update now?", "Update", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.Arguments = sVersion;
                        psi.FileName = Application.StartupPath + @"\TraXile.Updater.exe";
                        Process.Start(psi);
                    }
                }
                else
                {
                    if(b_notify_ok)
                        MessageBox.Show("Your version: " + APPINFO.VERSION 
                            + Environment.NewLine + "Latest version: " + sVersion + Environment.NewLine + Environment.NewLine 
                            + "Your version is already up to date :)");
                }
            }
            catch(Exception ex)
            {
                _log.Error("Could not check for Update: " + ex.Message);
            }
        }

        /// <summary>
        /// Do main initialization
        /// ONLY CALL ONCE! S
        /// </summary>
        private void Init()
        {
            this.Opacity = 0;

            _log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
            _log.Info("Application started");

            _mySettings.LoadFromXml();

            _lvmStats = new ListViewManager(listViewStats);
            _lvmActlog = new ListViewManager(listViewActLog);

            _eventMapping = new EventMapping();
            _defaultMappings = new DefaultMappings();
            _parsedActivities = new List<string>();

            SaveVersion();
            CheckForUpdate();
            _UpdateCheckDone = true;
            ReadSettings();

            comboBox2.SelectedItem = ReadSetting("actlog.maxitems", "500");

            try
            {
                DoBackupRestoreIfPrepared();        
            }
            catch(Exception ex)
            {
                _restoreMode = true;
                _restoreOk = false;
                _failedRestoreReason = ex.Message;
                _log.Error("FailedRestore -> " + ex.Message);
                _log.Debug(ex.ToString());
            }

            listViewActLog.Columns[0].Width = 120;
            listViewActLog.Columns[1].Width = 50;
            listViewActLog.Columns[2].Width = 110;
            listViewActLog.Columns[3].Width = 100;
            listViewActLog.Columns[4].Width = 50;
            listViewStats.Columns[0].Width = 500;
            listViewStats.Columns[1].Width = 300;

            listView1.Columns[0].Width = 300;

            chart1.ChartAreas[0].AxisX.LineColor = Color.Red;
            chart1.ChartAreas[0].AxisY.LineColor = Color.Red;
            chart1.ChartAreas[0].AxisX.LabelStyle.ForeColor = Color.Red;
            chart1.ChartAreas[0].AxisY.LabelStyle.ForeColor = Color.Red;
            chart1.ChartAreas[0].AxisX.Interval = 1;
            chart1.ChartAreas[0].AxisX.IntervalType = DateTimeIntervalType.Days;
            chart1.ChartAreas[0].AxisX.IntervalOffset = 1;
            chart1.Series[0].XValueType = ChartValueType.DateTime;
            chart1.Series[0].LabelForeColor = Color.White;
            chart1.Series[0].LabelBackColor = Color.Black;
            chart1.Series[0].LabelBorderColor = Color.Black;
            chart1.Series[0].Color = Color.White;
            chart1.Legends[0].Enabled = false;

            chart2.BackColor = Color.Black;
            chart2.ChartAreas[0].BackColor = Color.Black;
            chart2.ChartAreas[0].AxisX.LineColor = Color.Red;
            chart2.ChartAreas[0].AxisY.LineColor = Color.Red;
            chart2.ChartAreas[0].AxisX.LabelStyle.ForeColor = Color.Red;
            chart2.ChartAreas[0].AxisY.LabelStyle.ForeColor = Color.Red;
            chart2.ChartAreas[0].AxisX.Interval = 1;
            chart2.ChartAreas[0].AxisX.IntervalType = DateTimeIntervalType.Number;
            chart2.ChartAreas[0].AxisX.IntervalOffset = 1;
            chart2.Series[0].XValueType = ChartValueType.Int32;
            chart2.Legends[0].Enabled = false;
            chart2.Series[0].IsValueShownAsLabel = true;
            chart2.Series[0].LabelForeColor = Color.White;
            chart2.Series[0].Color = Color.White;

            chart3.BackColor = Color.Black;
            chart3.ChartAreas[0].BackColor = Color.Black;
            chart3.ChartAreas[0].AxisX.LineColor = Color.Red;
            chart3.ChartAreas[0].AxisY.LineColor = Color.Red;
            chart3.ChartAreas[0].AxisX.LabelStyle.ForeColor = Color.Red;
            chart3.ChartAreas[0].AxisY.LabelStyle.ForeColor = Color.Red;
            chart3.ChartAreas[0].AxisX.Interval = 1;
            chart3.ChartAreas[0].AxisX.IntervalType = DateTimeIntervalType.Number;
            chart3.ChartAreas[0].AxisX.IntervalOffset = 1;
            chart3.Series[0].XValueType = ChartValueType.Int32;
            chart3.Series[0].YValueType = ChartValueType.Double;
            chart3.Legends[0].Enabled = false;
            chart3.Series[0].IsValueShownAsLabel = true;
            chart3.Series[0].LabelForeColor = Color.White;
            chart3.Series[0].Color = Color.White;

            chart4.BackColor = Color.Black;
            chart4.ChartAreas[0].BackColor = Color.Black;
            chart4.ChartAreas[0].AxisX.LineColor = Color.Red;
            chart4.ChartAreas[0].AxisY.LineColor = Color.Red;
            chart4.ChartAreas[0].AxisX.LabelStyle.ForeColor = Color.Red;
            chart4.ChartAreas[0].AxisY.LabelStyle.ForeColor = Color.Red;
            chart4.ChartAreas[0].AxisX.Interval = 1;
            chart4.ChartAreas[0].AxisX.IntervalOffset = 1;
            chart4.Series[0].XValueType = ChartValueType.String;
            chart4.Series[0].YValueType = ChartValueType.Double;
            chart4.Legends[0].Enabled = false;
            chart4.Series[0].IsValueShownAsLabel = true;
            chart4.Series[0].LabelForeColor = Color.White;
            chart4.Series[0].Color = Color.White;

            chart5.BackColor = Color.Black;
            chart5.ChartAreas[0].BackColor = Color.Black;
            chart5.ChartAreas[0].AxisX.LineColor = Color.Red;
            chart5.ChartAreas[0].AxisY.LineColor = Color.Red;
            chart5.ChartAreas[0].AxisX.LabelStyle.ForeColor = Color.Red;
            chart5.ChartAreas[0].AxisY.LabelStyle.ForeColor = Color.Red;
            chart5.ChartAreas[0].AxisX.Interval = 1;
            chart5.ChartAreas[0].AxisX.IntervalOffset = 1;
            chart5.Series[0].XValueType = ChartValueType.String;
            chart5.Series[0].YValueType = ChartValueType.Double;
            chart5.Legends[0].Enabled = false;
            chart5.Series[0].IsValueShownAsLabel = true;
            chart5.Series[0].LabelForeColor = Color.White;
            chart5.Series[0].Color = Color.White;

            chart6.BackColor = Color.Black;
            chart6.ChartAreas[0].BackColor = Color.Black;
            chart6.ChartAreas[0].AxisX.LineColor = Color.Red;
            chart6.ChartAreas[0].AxisY.LineColor = Color.Red;
            chart6.ChartAreas[0].AxisX.LabelStyle.ForeColor = Color.Red;
            chart6.ChartAreas[0].AxisY.LabelStyle.ForeColor = Color.Red;
            chart6.ChartAreas[0].AxisX.Interval = 1;
            chart6.ChartAreas[0].AxisX.IntervalOffset = 1;
            chart6.Series[0].XValueType = ChartValueType.String;
            chart6.Series[0].YValueType = ChartValueType.Double;
            chart6.Legends[0].Enabled = false;
            chart6.Series[0].IsValueShownAsLabel = true;
            chart6.Series[0].LabelForeColor = Color.White;
            chart6.Series[0].Color = Color.White;

            chart7.BackColor = Color.Black;
            chart7.ChartAreas[0].BackColor = Color.Black;
            chart7.ChartAreas[0].AxisX.LineColor = Color.Red;
            chart7.ChartAreas[0].AxisY.LineColor = Color.Red;
            chart7.ChartAreas[0].AxisX.LabelStyle.ForeColor = Color.Red;
            chart7.ChartAreas[0].AxisY.LabelStyle.ForeColor = Color.Red;
            chart7.ChartAreas[0].AxisX.Interval = 1;
            chart7.ChartAreas[0].AxisX.IntervalOffset = 1;
            chart7.Series[0].XValueType = ChartValueType.String;
            chart7.Series[0].YValueType = ChartValueType.Double;
            chart7.Legends[0].Enabled = false;
            chart7.Series[0].IsValueShownAsLabel = true;
            chart7.Series[0].LabelForeColor = Color.White;
            chart7.Series[0].Color = Color.White;

            chart8.BackColor = Color.Black;
            chart8.ChartAreas[0].BackColor = Color.Black;
            chart8.ChartAreas[0].AxisX.LineColor = Color.Red;
            chart8.ChartAreas[0].AxisY.LineColor = Color.Red;
            chart8.ChartAreas[0].AxisX.LabelStyle.ForeColor = Color.Red;
            chart8.ChartAreas[0].AxisY.LabelStyle.ForeColor = Color.Red;
            chart8.ChartAreas[0].AxisX.Interval = 1;
            chart8.ChartAreas[0].AxisX.IntervalOffset = 1;
            chart8.Series[0].XValueType = ChartValueType.String;
            chart8.Series[0].YValueType = ChartValueType.Double;
            chart8.Legends[0].Enabled = true;
            chart8.Series[0].IsValueShownAsLabel = true;
            chart8.Series[0].LabelForeColor = Color.White;
            chart8.Series[0].Color = Color.White;

            var ca = chart1.ChartAreas["ChartArea1"].CursorX;
            ca.IsUserEnabled = true;
            ca.IsUserSelectionEnabled = true;

            textBox1.Text = ReadSetting("PoELogFilePath");
            textBox1.Enabled = false;

            comboBox1.SelectedIndex = 1;

            _dict = new Dictionary<int, string>();
            _eventQueue = new ConcurrentQueue<TrackingEvent>();
            _eventHistory = new List<TrackedActivity>();
            _knownPlayerNames = new List<string>();
            _backups = new BindingList<string>();
            _currentArea = "-";
            _inAreaSince = DateTime.Now;
            _eventQueueInitizalized = false;
            _tagLabels = new Dictionary<string, Label>();
            _tagLabelsConfig = new Dictionary<string, Label>();
            _lastSimuEndpoint = "";
            _tags = new List<ActivityTag>();

            this.Text = APPINFO.NAME;
            _initStartTime = DateTime.Now;

            if(String.IsNullOrEmpty(SettingPoeLogFilePath))
            {
                FileSelectScreen fs = new FileSelectScreen(this);
                fs.ShowDialog();
            }

            _loadScreenWindow = new LoadScreen();
            _loadScreenWindow.Show(this);

            InitDatabase();
            
            _lastEventType = EVENT_TYPES.APP_STARTED;
            InitNumStats();

            foreach (KeyValuePair<string, int> kvp in _numericStats)
            {
                ListViewItem lvi = new ListViewItem(GetStatLongName(kvp.Key));
                lvi.SubItems.Add("0");
                _lvmStats.AddLvItem(lvi, "stats_" + kvp.Key);
            }

            _eventQueue.Enqueue(new TrackingEvent(EVENT_TYPES.APP_STARTED) { EventTime = DateTime.Now, LogLine = "Application started." });

            ReadStatsCache();
            ReadKnownPlayers();
            LoadCustomTags();
            ResetMapHistory();
            LoadLayout();

            if (!_historyInitialized)
            {
                ReadActivityLogFromSQLite();
            }

            // Thread for Log Parsing and Enqueuing
            _logParseThread = new Thread(new ThreadStart(LogParsing))
            {
                IsBackground = true
            };
            _logParseThread.Start();

            // Thread for Queue processing / Dequeuing
            _eventThread = new Thread(new ThreadStart(EventHandling))
            {
                IsBackground = true
            };
            _eventThread.Start();

            // Request initial Dashboard update
            _labDashboardUpdateRequested = true;
            _mapDashboardUpdateRequested = true;
            _heistDashboardUpdateRequested = true;
            _globalDashboardUpdateRequested = true;
        }

        /// <summary>
        /// Save the current GUI Layout to config
        /// </summary>
        private void SaveLayout()
        {
            foreach(ColumnHeader ch in listViewActLog.Columns)
            {
                if(ch.Width > 0)
                {
                    AddUpdateAppSettings("layout.listview.cols." + ch.Name + ".width", ch.Width.ToString());
                }
            }
            if(this.Width > 50 && this.Height > 50)
            {
                AddUpdateAppSettings("layout.window.width", this.Width.ToString());
                AddUpdateAppSettings("layout.window.height", this.Height.ToString());
            }

            AddUpdateAppSettings("layout.splitpanel.width", splitContainer1.SplitterDistance.ToString());
        }

        /// <summary>
        /// Load GUI layout from config
        /// </summary>
        private void LoadLayout()
        {
            foreach (ColumnHeader ch in listViewActLog.Columns)
            {
                int w = Convert.ToInt32(ReadSetting("layout.listview.cols." + ch.Name + ".width"));
                if(w > 0)
                {
                    ch.Width = w;
                }
            }

            int iWidth = Convert.ToInt32(ReadSetting("layout.window.width"));
            int iHeight = Convert.ToInt32(ReadSetting("layout.window.height"));

            if(iWidth > 50 && iHeight > 50)
            {
                this.Width = iWidth;
                this.Height = iHeight;
            }

            this.splitContainer1.SplitterDistance = Convert.ToInt32(ReadSetting("layout.splitpanel.width", "600"));
            
        }

        /// <summary>
        ///  Initialize all default tags
        /// </summary>
        private void InitDefaultTags()
        {
            List<ActivityTag> tmpTags;
            tmpTags = new List<ActivityTag>();
            tmpTags.Add(new ActivityTag("blight") { BackColor = Color.LightGreen, ForeColor = Color.Black, ShowInListView = true });
            tmpTags.Add(new ActivityTag("delirium") { BackColor = Color.WhiteSmoke, ForeColor = Color.Black, ShowInListView = true });
            tmpTags.Add(new ActivityTag("einhar") { BackColor = Color.Red, ForeColor = Color.Black, ShowInListView = true });
            tmpTags.Add(new ActivityTag("incursion") { BackColor = Color.GreenYellow, ForeColor = Color.Black, ShowInListView = true });
            tmpTags.Add(new ActivityTag("syndicate") { BackColor = Color.Gold, ForeColor = Color.Black, ShowInListView = true });
            tmpTags.Add(new ActivityTag("zana") { BackColor = Color.Blue, ForeColor = Color.White, ShowInListView = true });
            tmpTags.Add(new ActivityTag("niko") { BackColor = Color.OrangeRed, ForeColor = Color.Black, ShowInListView = true });
            tmpTags.Add(new ActivityTag("zana-map") { BackColor = Color.Blue, ForeColor = Color.Black, ShowInListView = true });
            tmpTags.Add(new ActivityTag("expedition") { BackColor = Color.Turquoise, ForeColor = Color.Black, ShowInListView = true });
            tmpTags.Add(new ActivityTag("rog") { BackColor = Color.Turquoise, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("gwennen") { BackColor = Color.Turquoise, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("dannig") { BackColor = Color.Turquoise, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("tujen") { BackColor = Color.Turquoise, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("karst") { BackColor = Color.IndianRed, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("tibbs") { BackColor = Color.IndianRed, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("isla") { BackColor = Color.IndianRed, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("tullina") { BackColor = Color.IndianRed, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("niles") { BackColor = Color.IndianRed, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("nenet") { BackColor = Color.IndianRed, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("vinderi") { BackColor = Color.IndianRed, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("gianna") { BackColor = Color.IndianRed, ForeColor = Color.Black });
            tmpTags.Add(new ActivityTag("huck") { BackColor = Color.IndianRed, ForeColor = Color.Black });

            foreach (ActivityTag tag in tmpTags)
            {
                try
                {
                    SqliteCommand cmd = _dbConnection.CreateCommand();
                    cmd.CommandText = "insert into tx_tags (tag_id, tag_display, tag_bgcolor, tag_forecolor, tag_type, tag_show_in_lv) values " +
                                  "('" + tag.ID + "', '" + tag.DisplayName + "', '" + tag.BackColor.ToArgb() + "', '" + tag.ForeColor.ToArgb() + "', 'default', " + (tag.ShowInListView ? "1" : "0") + ")";
                    cmd.ExecuteNonQuery();
                    _log.Info("Default tag '" + tag.ID + "' added to database");
                }
                catch(SqliteException e)
                {
                    if(e.Message.Contains("SQLite Error 19"))
                    {
                        _log.Info("Default tag '" + tag.ID + "' already in database, nothing todo");
                    }
                    else
                    {
                        _log.Error(e.ToString());
                    }
                }
               
            }
        }

        /// <summary>
        /// Load user created tags
        /// </summary>
        private void LoadCustomTags()
        {
            SqliteDataReader sqlReader;
            SqliteCommand cmd;

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "SELECT * FROM tx_tags ORDER BY tag_id DESC";
            sqlReader = cmd.ExecuteReader();

            while (sqlReader.Read())
            {
                string sID = sqlReader.GetString(0);
                string sType = sqlReader.GetString(4);
                ActivityTag tag = new ActivityTag(sID, sType == "custom" ? false : true);
                tag.DisplayName = sqlReader.GetString(1);
                tag.BackColor = Color.FromArgb(Convert.ToInt32(sqlReader.GetString(2)));
                tag.ForeColor = Color.FromArgb(Convert.ToInt32(sqlReader.GetString(3)));
                tag.ShowInListView = sqlReader.GetInt32(5) == 1;
                _tags.Add(tag);
            }
        }

        /// <summary>
        /// Render the taglist for Config Tab
        /// </summary>
        /// <param name="b_reinit"></param>
        private void RenderTagsForConfig(bool b_reinit = false)
        {
            if (b_reinit)
            {
                groupBox3.Controls.Clear();
                _tagLabelsConfig.Clear();
            }

            int iOffsetX = 10;
            int ioffsetY = 23;

            int iX = iOffsetX;
            int iY = ioffsetY;
            int iLabelWidth = 100;
            int iMaxCols = 5;

            int iCols = (groupBox3.Width-20) / iLabelWidth;
            if (iCols > iMaxCols) iCols = iMaxCols;
            int iCurrCols = 0;

            for (int i = 0; i < _tags.Count; i++)
            {
                ActivityTag tag = _tags[i];
                Label lbl = new Label();
                lbl.Width = iLabelWidth;

                if (iCurrCols > (iCols - 1))
                {
                    iY += 28;
                    iX = iOffsetX;
                    iCurrCols = 0;
                }

                if (!_tagLabelsConfig.ContainsKey(tag.ID))
                {
                    lbl.Text = tag.DisplayName;
                    lbl.TextAlign = ContentAlignment.MiddleCenter;
                    lbl.BackColor = tag.BackColor;
                    lbl.ForeColor = tag.ForeColor;
                    lbl.MouseHover += tagLabel_MouseOver;
                    lbl.MouseLeave += tagLabel_MouseLeave;
                    lbl.MouseClick += Lbl_MouseClick1;
                    lbl.Location = new Point(iX, iY);

                    groupBox3.Controls.Add(lbl);
                    _tagLabelsConfig.Add(tag.ID, lbl);
                }

                iX += lbl.Width + 5;
                iCurrCols++;
            }
        }

        /// <summary>
        ///  Tag label moouse click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Lbl_MouseClick1(object sender, MouseEventArgs e)
        {
            ActivityTag tag = GetTagByDisplayName(((Label)sender).Text);
            textBox4.Text = tag.ID;
            textBox5.Text = tag.DisplayName;
            checkBox4.Checked = tag.ShowInListView;
            label63.ForeColor = tag.ForeColor;
            label63.BackColor = tag.BackColor;
            label63.Text = tag.DisplayName;
        }

        /// <summary>
        /// Tag label mouse leave
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void tagLabel_MouseLeave(object sender, EventArgs e)
        {
            ((Label)sender).BorderStyle = BorderStyle.None;
        }

        /// <summary>
        /// Tag Label mouse over
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void tagLabel_MouseOver(object sender, EventArgs e)
        {
            ((Label)sender).BorderStyle = BorderStyle.Fixed3D;
        }

        /// <summary>
        /// RenderTags in tracking tab
        /// </summary>
        /// <param name="b_reinit"></param>
        private void RenderTagsForTracking(bool b_reinit = false)
        {
            if (b_reinit)
            {
                groupBox8.Controls.Clear();
                _tagLabels.Clear();
            }

            int iOffsetX = 10;
            int ioffsetY = 20;
            int iLabelWidth = 100;
            int iMaxCols = 5;

            int iX = iOffsetX;
            int iY = ioffsetY;

            int iCols = groupBox8.Width / iLabelWidth;
            if (iCols > iMaxCols) iCols = iMaxCols;
            int iCurrCols = 0;

            for (int i = 0; i < _tags.Count; i++)
            {
                ActivityTag tag = _tags[i];
                Label lbl = new Label();
                lbl.Width = iLabelWidth;

                if (iCurrCols > (iCols - 1))
                {
                    iY += 28;
                    iX = iOffsetX;
                    iCurrCols = 0;
                }

                if(!_tagLabels.ContainsKey(tag.ID))
                {
                    lbl.Text = tag.DisplayName;
                    lbl.TextAlign = ContentAlignment.MiddleCenter;
                    lbl.BackColor = Color.Gray;
                    lbl.ForeColor = Color.LightGray;
                    lbl.Location = new Point(iX, iY);
                    lbl.MouseHover += tagLabel_MouseOver;
                    lbl.MouseLeave += tagLabel_MouseLeave;
                    lbl.MouseClick += Lbl_MouseClick;
                    
                    groupBox8.Controls.Add(lbl);
                    _tagLabels.Add(tag.ID, lbl);
                }
                else
                {
                    if(_currentActivity != null)
                    {
                        TrackedActivity mapToCheck = _isMapZana ? _currentActivity.ZanaMap : _currentActivity;

                        if(mapToCheck.Tags.Contains(tag.ID))
                        {
                            _tagLabels[tag.ID].BackColor = tag.BackColor;
                            _tagLabels[tag.ID].ForeColor = tag.ForeColor;
                        }
                        else
                        {
                            _tagLabels[tag.ID].BackColor = Color.Gray;
                            _tagLabels[tag.ID].ForeColor = Color.LightGray;
                        }
                    }
                    else
                    {
                        _tagLabels[tag.ID].BackColor = Color.Gray;
                        _tagLabels[tag.ID].ForeColor = Color.LightGray;
                    }
                }

                iX += lbl.Width + 5;
                iCurrCols++;
            }
        }

        /// <summary>
        /// Find matching tag for given display name
        /// </summary>
        /// <param name="s_display_name"></param>
        /// <returns></returns>
        public ActivityTag GetTagByDisplayName(string s_display_name)
        {
            foreach(ActivityTag t in _tags)
            {
                if (t.DisplayName == s_display_name)
                    return t;
            }

            return null;
        }

        /// <summary>
        /// Mouse click handler for label
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Lbl_MouseClick(object sender, MouseEventArgs e)
        {
            ActivityTag tag = GetTagByDisplayName(((Label)sender).Text);
            if(!tag.IsDefault)
            {
                if(_currentActivity != null)
                {
                    if(_isMapZana && _currentActivity.ZanaMap != null)
                    {
                        if (_currentActivity.ZanaMap.HasTag(tag.ID))
                        {
                            _currentActivity.ZanaMap.RemoveTag(tag.ID);
                        }
                        else
                        {
                            _currentActivity.ZanaMap.AddTag(tag.ID);
                        }
                    }
                    else
                    {
                        if(_currentActivity.HasTag(tag.ID))
                        {
                            _currentActivity.RemoveTag(tag.ID);
                        }
                        else
                        {
                            _currentActivity.AddTag(tag.ID);
                        }
                        
                    }
                }
            }
        }

        /// <summary>
        /// Mouse over handler for label
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Lbl_MouseHover(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Reload the Poe Logfile
        /// </summary>
        public void ReloadLogFile()
        {
            ResetStats();
            this._eventQueueInitizalized = false;
            _lastHash = 0;
            SaveStatsCache();
            Application.Restart();
        }

        /// <summary>
        /// Initialize the stats
        /// </summary>
        private void InitNumStats()
        {
            _numericStats = new Dictionary<string, int>
            {
                { "AreaChanges", 0 },
                { "BaranStarted", 0 },
                { "BaranKilled", 0 },
                { "CatarinaTried", 0 },
                { "CatarinaKilled", 0 },
                { "TotalKilledCount", 0 },
                { "DroxStarted", 0 },
                { "DroxKilled", 0 },
                { "EinharCaptures", 0 },
                { "ElderTried", 0 },
                { "ElderKilled", 0 },
                { "ExpeditionEncounters", 0 },
                { "ExpeditionEncounters_Rog", 0 },
                { "ExpeditionEncounters_Tujen", 0 },
                { "ExpeditionEncounters_Gwennen", 0 },
                { "ExpeditionEncounters_Dannig", 0 },
                { "HighestLevel", 0 },
                { "HunterKilled", 0 },
                { "HunterStarted", 0 },
                { "LabsFinished", 0 },
                { "LabsStarted", 0 },
                { "LevelUps", 0 },
                { "MavenStarted", 0 },
                { "MavenKilled", 0 },
                { "TotalMapsDone", 0 },
                { "TotalHeistsDone", 0 },
                { "ShaperTried", 0 },
                { "ShaperKilled", 0 },
                { "SimulacrumCleared", 0 },
                { "SimulacrumStarted", 0 },
                { "SirusStarted", 0 },
                { "SirusKilled", 0 },
                { "TemplesDone", 0 },
                { "TrialMasterStarted", 0 },
                { "TrialMasterKilled", 0 },
                { "TrialMasterTookReward", 0 },
                { "TrialMasterVictory", 0 },
                { "TrialMasterSuccess", 0 },
                { "VeritaniaKilled", 0 },
                { "VeritaniaStarted", 0 },
            };

            _statNamesLong = new Dictionary<string, string>
            {
                { "TotalMapsDone", "Total maps done" },
                { "TotalHeistsDone", "Total heists done" },
                { "ElderKilled", "Elder killed" },
                { "ShaperKilled", "Shaper killed" },
                { "SirusStarted", "Sirus tried" },
                { "SirusKilled", "Sirus killed" },
                { "HunterKilled", "Hunter killed (not reliable*)" },
                { "HunterStarted", "Hunter tried" },
                { "VeritaniaKilled", "Veritania killed (not reliable*)" },
                { "VeritaniaStarted", "Veritania tried" },
                { "BaranStarted", "Baran tried" },
                { "BaranKilled", "Baran killed (not reliable*)" },
                { "DroxStarted", "Drox tried" },
                { "DroxKilled", "Drox killed (not reliable*)" },
                { "HighestLevel", "Highest level reached" },
                { "TrialMasterStarted", "Trialmaster-Fight tried" },
                { "TrialMasterKilled", "Trialmaster killed" },
                { "MavenStarted", "Maven tried" },
                { "MavenKilled", "Maven killed" },
                { "TotalKilledCount", "Death count" },
                { "EinharCaptures", "Einhar beasts captured" },
                { "TrialMasterTookReward", "Ultimatum: took rewards" },
                { "TrialMasterVictory", "Ultimatum: cleared all rounds" },
                { "TrialMasterSuccess", "Ultimatum: did not fail" },
                { "ShaperTried", "Shaper tried" },
                { "ElderTried", "Elder tried" },
                { "CatarinaTried", "Catarina tried" },
                { "CatarinaKilled", "Catarina killed" },
                { "LevelUps", "Level Ups" },
                { "SimulacrumStarted", "Simulacrum started" },
                { "SimulacrumCleared", "Simulacrum 100% done" },
                { "LabsFinished", "Finished labs" },
                { "TemplesDone", "Temples done" },
                { "LabsStarted", "Labs started" },
                { "ExpeditionEncounters", "Expedition encounters" },
                { "ExpeditionEncounters_Rog", "Expedition encounters: Rog" },
                { "ExpeditionEncounters_Tujen", "Expedition encounters: Tujen" },
                { "ExpeditionEncounters_Gwennen", "Expedition encounters: Gwennen" },
                { "ExpeditionEncounters_Dannig", "Expedition encounters: Dannig" },
            };

            labs = new List<string>
            {
                "Unknown",
                "The Labyrinth",
                "The Merciless Labyrinth",
                "The Cruel Labyrinth",
                "Uber-Lab",
                "Advanced Uber-Lab"
            };

            foreach (string s in _defaultMappings.HEIST_AREAS)
            {
                string sName = s.Replace("'", "");
                if (!_numericStats.ContainsKey("HeistsFinished_" + sName))
                    _numericStats.Add("HeistsFinished_" + sName, 0);
                if (!_statNamesLong.ContainsKey("HeistsFinished_" + sName))
                    _statNamesLong.Add("HeistsFinished_" + sName, "Heists done: " + sName);
            }

            foreach (string s in labs)
            {
                string sName = s.Replace("'", "");
                if (!_numericStats.ContainsKey("LabsCompleted_" + sName))
                    _numericStats.Add("LabsCompleted_" + sName, 0);
                if (!_statNamesLong.ContainsKey("LabsCompleted_" + sName))
                    _statNamesLong.Add("LabsCompleted_" + sName, "Labs completed: " + sName);
            }

            foreach (string s in _defaultMappings.MAP_AREAS)
            {
                string sName = s.Replace("'", "");
                if (!_numericStats.ContainsKey("MapsFinished_" + sName))
                    _numericStats.Add("MapsFinished_" + sName, 0);
                if (!_statNamesLong.ContainsKey("MapsFinished_" + sName))
                    _statNamesLong.Add("MapsFinished_" + sName, "Maps done: " + sName);
            }

            for (int i = 0; i <= 16; i++)
            {
                string sShort = "MapTierFinished_T" + i.ToString();
                string sLong = i > 0 ?  ("Maps done: T" + i.ToString()) : "Maps done: Tier unknown";
                if (!_numericStats.ContainsKey(sShort))
                    _numericStats.Add(sShort, 0);
                if (!_statNamesLong.ContainsKey(sShort))
                    _statNamesLong.Add(sShort, sLong);
            }

            foreach (string s in _defaultMappings.SIMU_AREAS)
            {
                string sName = s.Replace("'", "");
                if (!_numericStats.ContainsKey("SimulacrumFinished_" + sName))
                    _numericStats.Add("SimulacrumFinished_" + sName, 0);
                if (!_statNamesLong.ContainsKey("SimulacrumFinished_" + sName))
                    _statNamesLong.Add("SimulacrumFinished_" + sName, "Simulacrum done: " + sName);
            }
        }

        /// <summary>
        /// Get shortname for stat
        /// </summary>
        /// <param name="s_key"></param>
        /// <returns></returns>
        private string GetStatShortName(string s_key)
        {
            foreach(KeyValuePair<string,string> kvp in _statNamesLong)
            {
                if (kvp.Value == s_key)
                    return kvp.Key;
            }
            return null;
        }

        /// <summary>
        /// Get longname for a stat
        /// </summary>
        /// <param name="s_key"></param>
        /// <returns></returns>
        private string GetStatLongName(string s_key)
        {
            if(_statNamesLong.ContainsKey(s_key))
            {
                return _statNamesLong[s_key];
            }
            else
            {
                return s_key;
            }
        }

        /// <summary>
        /// Reset stats
        /// </summary>
        public void ResetStats()
        {
            this.ClearStatsDB();
        }

        /// <summary>
        /// Empty stats DB
        /// </summary>
        private void ClearStatsDB()
        {
            SqliteCommand cmd;

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "drop table tx_stats";
            cmd.ExecuteNonQuery();

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "create table if not exists tx_stats " +
                "(timestamp int, " +
                "stat_name text, " +
                "stat_value int)";
            cmd.ExecuteNonQuery();

            InitNumStats();
            SaveStatsCache();

            _log.Info("Stats cleared.");
        }

        /// <summary>
        /// Clear the activity log
        /// </summary>
        private void ClearActivityLog()
        {
            SqliteCommand cmd;

            _eventHistory.Clear();
            listViewActLog.Items.Clear();

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "drop table tx_activity_log";
            cmd.ExecuteNonQuery();

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "create table if not exists tx_activity_log " +
                 "(timestamp int, " +
                 "act_type text, " +
                 "act_area text, " +
                 "act_stopwatch int, " +
                 "act_deathcounter int," +
                 "act_ulti_rounds int," +
                 "act_is_zana int," +
                 "act_tags" + ")";
            cmd.ExecuteNonQuery();

            InitNumStats();
            SaveStatsCache();

            _log.Info("Activity log cleared.");
        }

        /// <summary>
        /// Read existing backups to list in GUI
        /// </summary>
        private void ReadBackupList()
        {
            if(Directory.Exists(_myAppData + @"\backups"))
            {
                foreach(string s in Directory.GetDirectories(_myAppData + @"\backups"))
                {
                    foreach(string s2 in Directory.GetDirectories(s))
                    {
                        string s_name = s2.Replace(_myAppData, "");
                        
                        if(!_backups.Contains(s_name))
                            _backups.Add(s_name);
                    }
                }
            }
        }

        /// <summary>
        /// Initialize the databse
        /// TODO: Move datbase handling to own class
        /// </summary>
        private void InitDatabase()
        {
            _dbConnection = new SqliteConnection("Data Source=" + _dbPath);
            _dbConnection.Open();

            //Create Tables
            SqliteCommand cmd;

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "create table if not exists tx_activity_log " +
                "(timestamp int, " +
                "act_type text, " +
                "act_area text, " +
                "act_stopwatch int, " +
                "act_deathcounter int," +
                "act_ulti_rounds int," +
                "act_is_zana int," + 
                "act_tags" + ")";
            cmd.ExecuteNonQuery();

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "create table if not exists tx_tags " +
                "(tag_id text, " +
                "tag_display text," +
                "tag_bgcolor text, " +
                "tag_forecolor text," +
                "tag_type text," +
                "tag_show_in_lv int default 0)";
            cmd.ExecuteNonQuery();

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "create unique index if not exists tx_tag_id on tx_tags(tag_id)";
            cmd.ExecuteNonQuery();

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "create table if not exists tx_stats " +
                "(timestamp int, " +
                "stat_name text, " +
                "stat_value int)";
            cmd.ExecuteNonQuery();

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "create table if not exists tx_stats_cache " +
                "(" +
                "stat_name text, " +
                "stat_value int)";
            cmd.ExecuteNonQuery();

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "create table if not exists tx_known_players " +
                "(" +
                "player_name )";
            cmd.ExecuteNonQuery();
            InitDefaultTags();

            PatchDatabase();
            _log.Info("Database initialized.");
        }

        /// <summary>
        /// Apply patches to database
        /// TODO: Move datbase handling to own class
        /// </summary>
        private void PatchDatabase()
        {
            SqliteCommand cmd;
            // Update 0.3.4
            try
            {
                cmd = _dbConnection.CreateCommand();
                cmd.CommandText = "alter table tx_activity_log add column act_tags text";
                cmd.ExecuteNonQuery();
                _log.Info("PatchDatabase 0.3.4 -> " + cmd.CommandText);
            }
            catch
            {
            }

            // Update 0.4.5
            try
            {
                cmd = _dbConnection.CreateCommand();
                cmd.CommandText = "alter table tx_activity_log add column act_area_level int default 0";
                cmd.ExecuteNonQuery();
                _log.Info("PatchDatabase 0.4.5 -> " + cmd.CommandText);
            }
            catch
            {
            }

            // Update 0.5.2
            try
            {
                cmd = _dbConnection.CreateCommand();
                cmd.CommandText = "alter table tx_activity_log add column act_success int default 0";
                cmd.ExecuteNonQuery();
                _log.Info("PatchDatabase 0.5.2 -> " + cmd.CommandText);
            }
            catch
            {
            }

            // Update 0.5.2
            try
            {
                cmd = _dbConnection.CreateCommand();
                cmd.CommandText = "alter table tx_tags add column tag_show_in_lv int default 0";
                cmd.ExecuteNonQuery();
                _log.Info("PatchDatabase 0.5.2 -> " + cmd.CommandText);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Track known players. Needed to find out if death events are for your own 
        /// char or not. If a player name enters your area, It could not be you :)
        /// </summary>
        /// <param name="s_name"></param>
        private void AddKnownPlayerIfNotExists(string s_name)
        {
            if(!_knownPlayerNames.Contains(s_name))
            {
                _knownPlayerNames.Add(s_name);
                SqliteCommand cmd;
                cmd = _dbConnection.CreateCommand();
                cmd.CommandText = "insert into tx_known_players (player_name) VALUES ('" + s_name + "')";
                cmd.ExecuteNonQuery();
                _log.Info("KnownPlayerAdded -> name: " + s_name);
            }
        }

        /// <summary>
        /// Delete entry from activity log
        /// </summary>
        /// <param name="l_timestamp"></param>
        private void DeleteActLogEntry(long l_timestamp)
        {
            SqliteCommand cmd;
            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "delete from tx_activity_log where timestamp = " + l_timestamp.ToString();
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Read list of known players
        /// </summary>
        private void ReadKnownPlayers()
        {
            SqliteDataReader sqlReader;
            SqliteCommand cmd;

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "SELECT * FROM tx_known_players";
            sqlReader = cmd.ExecuteReader();

            while (sqlReader.Read())
            {
                _knownPlayerNames.Add(sqlReader.GetString(0));
            }
        }

        /// <summary>
        /// Save activity to database
        /// </summary>
        /// <param name="i_ts"></param>
        /// <param name="s_type"></param>
        /// <param name="s_area"></param>
        /// <param name="i_area_level"></param>
        /// <param name="i_stopwatch"></param>
        /// <param name="i_death_counter"></param>
        /// <param name="i_ulti_rounds"></param>
        /// <param name="b_zana"></param>
        /// <param name="l_tags"></param>
        private void SaveToActivityLog(long i_ts, string s_type, string s_area, int i_area_level, int i_stopwatch, int i_death_counter, int i_ulti_rounds, bool b_zana, List<string> l_tags, bool b_success)
        {
            //replace ' in area
            s_area = s_area.Replace("'", "");
            string sTags = "";

            for (int i = 0; i < l_tags.Count; i++)
            {
                sTags += l_tags[i];
                if (i < (l_tags.Count - 1))
                    sTags += "|";
            }

            SqliteCommand cmd;
            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "insert into tx_activity_log " +
               "(timestamp, " +
               "act_type, " +
               "act_area, " +
               "act_area_level, " +
               "act_stopwatch, " +
               "act_deathcounter, " +
               "act_ulti_rounds," +
               "act_is_zana," +
               "act_tags," +
               "act_success) VALUES (" +
               i_ts.ToString() 
                 + ", '" + s_type 
                 + "', '" + s_area
                 + "', '" + i_area_level.ToString()
                 + "', " + i_stopwatch 
                 + ", " + i_death_counter 
                 + ", " + i_ulti_rounds 
                 + ", " + (b_zana ? "1" : "0")
                 + ", '" + sTags + "'"
                 + ", " + (b_success ? "1" : "0") + ")";

            cmd.ExecuteNonQuery();

            _parsedActivities.Add(i_ts.ToString() + "_" + s_area);
        }

        private ACTIVITY_TYPES GetActTypeFromString(string s_type)
        {
            switch (s_type)
            {
                case "map":
                    return ACTIVITY_TYPES.MAP;
                case "heist":
                    return ACTIVITY_TYPES.HEIST;
                case "simulacrum":
                    return ACTIVITY_TYPES.SIMULACRUM;
                case "blighted_map":
                    return ACTIVITY_TYPES.BLIGHTED_MAP;
                case "labyrinth":
                    return ACTIVITY_TYPES.LABYRINTH;
                case "delve":
                    return ACTIVITY_TYPES.DELVE;
                case "temple":
                    return ACTIVITY_TYPES.TEMPLE;
            }
            return ACTIVITY_TYPES.MAP;
        }

        /// <summary>
        /// Read the activity log from Database
        /// </summary>
        private void ReadActivityLogFromSQLite()
        {
            SqliteDataReader sqlReader;
            SqliteCommand cmd;
            string[] arrTags;

            cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "SELECT * FROM tx_activity_log ORDER BY timestamp DESC";
            sqlReader = cmd.ExecuteReader();

            while(sqlReader.Read())
            {
                TimeSpan ts = TimeSpan.FromSeconds(sqlReader.GetInt32(3));
                string sType = sqlReader.GetString(1);
                ACTIVITY_TYPES aType = GetActTypeFromString(sType);
               


                TrackedActivity map = new TrackedActivity
                {
                    Started = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(sqlReader.GetInt32(0)).ToLocalTime(),
                    TimeStamp = sqlReader.GetInt32(0),
                    CustomStopWatchValue = String.Format("{0:00}:{1:00}:{2:00}", ts.Hours, ts.Minutes, ts.Seconds),
                    TotalSeconds = Convert.ToInt32(ts.TotalSeconds),
                    Type = aType,
                    Area = sqlReader.GetString(2),
                    DeathCounter = sqlReader.GetInt32(4),
                    TrialMasterCount = sqlReader.GetInt32(5)
                };

                try
                {
                    map.AreaLevel = sqlReader.GetInt32(8);
                }
                catch
                {
                    map.AreaLevel = 0;
                }

                try
                {
                    string sTags = sqlReader.GetString(7);
                    arrTags = sTags.Split('|');
                }
                catch
                {
                    arrTags = new string[0];
                }

                for(int i = 0; i < arrTags.Length; i++)
                {
                    map.AddTag(arrTags[i]);
                }

                //mapHistory
                _eventHistory.Add(map);
            }
            _historyInitialized = true;
            ResetMapHistory();
        }

        /// <summary>
        /// Main method for log parsing thread
        /// </summary>
        private void LogParsing()
        {
            while(true)
            {
                Thread.Sleep(1000);
                if(SettingPoeLogFilePath != null)
                {
                    ParseLogFile();
                  
                }
            }
        }

        /// <summary>
        /// Get line count from Client.txt. Used for progress calculation
        /// </summary>
        /// <returns></returns>
        private int GetLogFileLineCount()
        {
            int iCount = 0;
            FileStream fs1 = new FileStream(SettingPoeLogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            TextReader reader1 = new StreamReader(fs1);
            while ((reader1.ReadLine()) != null)
            {
                iCount++;
            }
            reader1.Close();
            return iCount;
        }


        /// <summary>
        /// Parse the logfile
        /// </summary>
        private void ParseLogFile()
        {
            _log.Info("Started logfile parsing. Last hash was " + _lastHash.ToString());

            _logLinesTotal = Convert.ToDouble(GetLogFileLineCount());

            var fs = new FileStream(SettingPoeLogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            bool bNewContent = _lastHash == 0;

            using (StreamReader reader = new StreamReader(fs))
            {
                string line;
                int lineHash = 0;

                // Keep file open
                while (!_exit)
                {
                    line = reader.ReadLine();
                    
                    if (line == null)
                    {
                        if (!_eventQueueInitizalized)
                        {
                            _currentActivity = null;
                            _isMapZana = false;
                            _initEndTime = DateTime.Now;
                            TimeSpan tsInitDuration = (_initEndTime - _initStartTime);
                            _eventQueue.Enqueue(new TrackingEvent(EVENT_TYPES.APP_READY) 
                            { 
                                EventTime = DateTime.Now, 
                                LogLine = "Application initialized in " 
                                  + Math.Round(tsInitDuration.TotalSeconds, 2) + " seconds." 
                            });
                            _lastHash = lineHash;
                        }
                        _eventQueueInitizalized = true;
                        bNewContent = true;

                        Thread.Sleep(100);
                        continue;
                    }

                    lineHash = line.GetHashCode();

                    if (_dict.ContainsKey(lineHash))
                        continue;

                    if(lineHash == _lastHash || _lastHash == 0)
                    {
                        bNewContent = true;
                    }

                    if (!bNewContent)
                    {
                        _logLinesRead++;
                        continue;
                    }

                    _lastHash = lineHash;

                    foreach (KeyValuePair<string,EVENT_TYPES> kv in _eventMapping.MAP)
                    {
                        if (line.Contains(kv.Key))
                        {
                            if (!_dict.ContainsKey(lineHash))
                            {
                                TrackingEvent ev = new TrackingEvent(kv.Value)
                                {
                                    LogLine = line
                                };
                                try
                                {
                                    ev.EventTime = DateTime.Parse(line.Split(' ')[0] + " " + line.Split(' ')[1]);
                                }
                                catch
                                {
                                    ev.EventTime = DateTime.Now;
                                }
                                _dict.Add(lineHash, "init");

                                if(!_eventQueueInitizalized)
                                {
                                    HandleSingleEvent(ev, true);
                                }
                                else
                                {
                                    _eventQueue.Enqueue(ev);
                                }
                                
                            }
                        }
                    }
                    _logLinesRead++;
                }
            }
        }

        /// <summary>
        /// Handle events - Read Queue
        /// </summary>
        private void EventHandling()
        {
            while (true)
            {
                Thread.Sleep(1);

                if (_eventQueueInitizalized)
                {
                    while (_eventQueue.TryDequeue(out TrackingEvent deqEvent))
                    {
                        HandleSingleEvent(deqEvent);
                    }
                }
            }
        }

        /// <summary>
        /// Check if a given area is a Map.
        /// </summary>
        /// <param name="sArea"></param>
        /// <param name="sSourceArea"></param>
        /// <returns></returns>
        private bool CheckIfAreaIsMap(string sArea, string sSourceArea = "")
        {
            // Laboratory could be map or heist...
            if (sArea == "Laboratory")
            {
                if (sSourceArea == "The Rogue Harbour")
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }

            foreach (string s in _defaultMappings.MAP_AREAS)
            {
               if (s.Trim().Equals(sArea.Trim()))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if a given area is a Heist
        /// </summary>
        /// <param name="sArea"></param>
        /// <param name="sSourceArea"></param>
        /// <returns></returns>
        private bool CheckIfAreaIsHeist(string sArea, string sSourceArea = "")
        {
            // Laboratory could be map or heist...
            if (sArea == "Laboratory")
            {
                if(sSourceArea == "The Rogue Harbour")
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            foreach (string s in _defaultMappings.HEIST_AREAS)
            {
                if (s.Trim().Equals(sArea.Trim()))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if a given area is simulacrum
        /// </summary>
        /// <param name="sArea"></param>
        /// <returns></returns>
        private bool CheckIfAreaIsSimu(string sArea)
        {
            foreach (string s in _defaultMappings.SIMU_AREAS)
            {
                if (s.Trim().Equals(sArea.Trim()))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Process a command entered via ingame chat
        /// </summary>
        /// <param name="s_command"></param>
        private void HandleChatCommand(string s_command)
        {
            _log.Info("ChatCommand -> " + s_command);
            string[] spl = s_command.Split(' ');
            string sMain = "";
            string sArgs = "";

            sMain = spl[0];

            if(spl.Length > 1)
            {
                sArgs = spl[1];
            }

            TrackedActivity currentAct = null;
            if (_currentActivity != null)
            {
                if (_isMapZana && _currentActivity.ZanaMap != null)
                {
                    currentAct = _currentActivity.ZanaMap;
                }
                else
                {
                    currentAct = _currentActivity;
                }
            }

            switch (sMain)
            {
                case "tag":
                    if(currentAct != null)
                    {
                        MethodInvoker mi = delegate
                        {
                            AddTagAutoCreate(sArgs, currentAct);
                        };
                        this.Invoke(mi);
                    }
                    break;
                case "untag":
                    if (currentAct != null)
                    {
                        MethodInvoker mi = delegate
                        {
                            RemoveTagFromActivity(sArgs, currentAct);
                        };
                        this.Invoke(mi);

                    }
                    break;
                case "pause":
                    if (_currentActivity != null)
                    {
                        if (_isMapZana && _currentActivity.ZanaMap != null)
                        {
                            if (!_currentActivity.ZanaMap.ManuallyPaused)
                            {
                                _currentActivity.ZanaMap.Pause();
                            }
                        }
                        else
                        {
                            if (!_currentActivity.ManuallyPaused)
                            {
                                _currentActivity.Pause();
                            }
                        }
                    }
                    break;
                case "resume":
                    if (_currentActivity != null)
                    {
                        if (_isMapZana && _currentActivity.ZanaMap != null)
                        {
                            if (_currentActivity.ZanaMap.ManuallyPaused)
                            {
                                _currentActivity.ZanaMap.Resume();
                            }
                        }
                        else
                        {
                            if (_currentActivity.ManuallyPaused)
                            {
                                _currentActivity.Resume();
                            }
                        }
                    }
                    break;
                case "finish":
                    if (currentAct != null && !_isMapZana)
                    {
                        MethodInvoker mi = delegate
                        {
                            FinishActivity(_currentActivity, null, ACTIVITY_TYPES.MAP, DateTime.Now);
                        };
                        this.Invoke(mi);
                    }
                    break;
            }
        }

        /// <summary>
        /// Handle area change. Core logic for nearly all tracking
        /// </summary>
        /// <param name="ev"></param>
        private void HandleAreaChangeEvent(TrackingEvent ev)
        {
            string sSourceArea = _currentArea;
            string sTargetArea = GetAreaNameFromEvent(ev);
            string sAreaName = GetAreaNameFromEvent(ev);
            bool bSourceAreaIsMap = CheckIfAreaIsMap(sSourceArea);
            bool bTargetAreaIsMap = CheckIfAreaIsMap(sTargetArea, sSourceArea);
            bool bTargetAreaIsHeist = CheckIfAreaIsHeist(sTargetArea, sSourceArea);
            bool bTargetAreaIsSimu = false;
            bool bTargetAreaMine = sTargetArea == "Azurite Mine";
            bool bTargetAreaTemple = sTargetArea == "The Temple of Atzoatl";
            bool bTargetAreaIsLab = sTargetArea == "Estate Path" || sTargetArea == "Estate Walkways" || sTargetArea == "Estate Crossing";
            long lTS = ((DateTimeOffset)ev.EventTime).ToUnixTimeSeconds();

            _inAreaSince = ev.EventTime;

            IncrementStat("AreaChanges", ev.EventTime, 1);

            if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.LABYRINTH)
            {
                _currentActivity.LastEnded = ev.EventTime;
            }

            if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.DELVE)
            {
                _currentActivity.LastEnded = ev.EventTime;
            }

            //Simu?
            if (_defaultMappings.SIMU_AREAS.Contains(sAreaName))
            {
                bTargetAreaIsSimu = true;
                if (_currentInstanceEndpoint != _lastSimuEndpoint)
                {
                    IncrementStat("SimulacrumStarted", ev.EventTime, 1);
                    _lastSimuEndpoint = _currentInstanceEndpoint;

                    _currentActivity = new TrackedActivity
                    {
                        Area = sTargetArea,
                        Type = ACTIVITY_TYPES.SIMULACRUM,
                        AreaLevel = _nextAreaLevel,
                        Started = ev.EventTime,
                        TimeStamp = lTS,
                        InstanceEndpoint = _currentInstanceEndpoint
                    };
                    _nextAreaLevel = 0;
                }
            }

            // Special calculation for Elder fight - he has no start dialoge.
            if (sAreaName == "Absence of Value and Meaning".Trim())
            {
                if (!_elderFightActive)
                {
                    IncrementStat("ElderTried", ev.EventTime, 1);
                }
                _elderFightActive = true;
            }

            ACTIVITY_TYPES actType = ACTIVITY_TYPES.MAP;
            if (bTargetAreaIsMap)
            {
                actType = ACTIVITY_TYPES.MAP;
            }
            else if (bTargetAreaIsHeist)
            {
                actType = ACTIVITY_TYPES.HEIST;
            }
            else if (bTargetAreaIsSimu)
            {
                actType = ACTIVITY_TYPES.SIMULACRUM;
            }
            else if (bTargetAreaIsLab)
            {
                actType = ACTIVITY_TYPES.LABYRINTH;
            }
            else if (bTargetAreaMine)
            {
                actType = ACTIVITY_TYPES.DELVE;
            }
            else if (bTargetAreaTemple)
            {
                actType = ACTIVITY_TYPES.TEMPLE;
            }

            //Lab started?
            if (actType == ACTIVITY_TYPES.LABYRINTH && sSourceArea == "Aspirants Plaza")
            {
                string sLabName = "Labyrinth";

                switch (_nextAreaLevel)
                {
                    case 33:
                        sLabName = "The Labyrinth";
                        break;
                    case 55:
                        sLabName = "The Cruel Labyrinth";
                        break;
                    case 68:
                        sLabName = "The Merciless Labyrinth";
                        break;
                    case 75:
                        sLabName = "Uber-Lab";
                        break;
                    case 83:
                        sLabName = "Advanced Uber-Lab";
                        break;
                    default:
                        sLabName = "Unknown";
                        break;
                }

                // Finish activity
                if(_currentActivity != null)
                {
                    FinishActivity(_currentActivity, null, ACTIVITY_TYPES.MAP, ev.EventTime);
                }

                _currentActivity = new TrackedActivity
                {
                    Area = sLabName,
                    AreaLevel = _nextAreaLevel,
                    Type = actType,
                    Started = ev.EventTime,
                    TimeStamp = lTS,
                    InstanceEndpoint = _currentInstanceEndpoint
                };

                IncrementStat("LabsStarted", ev.EventTime, 1);

            }

            //Lab Trial entered
            {
                if(_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.LABYRINTH && sTargetArea == "Aspirants Trial")
                {
                    _currentActivity.TrialCount++;
                }
            }

            //Lab cancelled?
            if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.LABYRINTH)
            {
                if (sTargetArea.Contains("Hideout") || _defaultMappings.CAMP_AREAS.Contains(sTargetArea))
                {
                    FinishActivity(_currentActivity, null, ACTIVITY_TYPES.LABYRINTH, DateTime.Now);
                }
            }

            // Delving?
            if ((_currentActivity == null || _currentActivity.Type != ACTIVITY_TYPES.DELVE) && actType == ACTIVITY_TYPES.DELVE)
            {
                // Finish activity
                if (_currentActivity != null)
                {
                    FinishActivity(_currentActivity, null, ACTIVITY_TYPES.MAP, ev.EventTime);
                }

                _currentActivity = new TrackedActivity
                {
                    Area = "Azurite Mine",
                    Type = actType,
                    Started = ev.EventTime,
                    TimeStamp = lTS,
                    InstanceEndpoint = _currentInstanceEndpoint
                };
            }

            // Update Delve level
            if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.DELVE && actType == ACTIVITY_TYPES.DELVE)
            {
                if (_nextAreaLevel > _currentActivity.AreaLevel)
                {
                    _currentActivity.AreaLevel = _nextAreaLevel;
                }
            }

            // End Delving?
            if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.DELVE && !bTargetAreaMine)
            {
                FinishActivity(_currentActivity, null, ACTIVITY_TYPES.DELVE, DateTime.Now);
            }

            // PAUSE RESUME Handling
            if (bTargetAreaIsMap || bTargetAreaIsHeist || bTargetAreaIsSimu)
            {
                if (_currentActivity != null)
                {
                    if (_defaultMappings.CAMP_AREAS.Contains(sSourceArea) || sSourceArea.Contains("Hideout"))
                    {
                        if (sTargetArea == _currentActivity.Area && _currentInstanceEndpoint == _currentActivity.InstanceEndpoint)
                        {
                            _currentActivity.EndPauseTime(ev.EventTime);
                        }
                    }
                }
            }

            if (bTargetAreaIsMap || bTargetAreaIsHeist || bTargetAreaIsSimu || bTargetAreaIsLab || bTargetAreaMine || bTargetAreaTemple)
            {
                _elderFightActive = false;
                _shaperKillsInFight = 0;

                if (_currentActivity == null)
                {
                    _currentActivity = new TrackedActivity
                    {
                        Area = sTargetArea,
                        Type = actType,
                        AreaLevel = _nextAreaLevel,
                        Started = ev.EventTime,
                        TimeStamp = lTS,
                        InstanceEndpoint = _currentInstanceEndpoint
                    };
                    _nextAreaLevel = 0;
                }
                else
                {
                    if (bTargetAreaIsSimu || bTargetAreaIsMap)
                    {
                        _currentActivity.PortalsUsed++;
                    }
                }
                if (!_currentActivity.ManuallyPaused)
                    _currentActivity.StartStopWatch();

                if (bSourceAreaIsMap && bTargetAreaIsMap)
                {
                    if (!_isMapZana)
                    {
                        // entered Zana Map
                        _isMapZana = true;
                        _currentActivity.StopStopWatch();
                        if (_currentActivity.ZanaMap == null)
                        {
                            _currentActivity.ZanaMap = new TrackedActivity
                            {
                                Type = ACTIVITY_TYPES.MAP,
                                Area = sTargetArea,
                                AreaLevel = _nextAreaLevel,
                                Started = ev.EventTime,
                                TimeStamp = lTS,
                            };
                            _currentActivity.ZanaMap.AddTag("zana-map");
                            _nextAreaLevel = 0;
                        }
                        if (!_currentActivity.ZanaMap.ManuallyPaused)
                            _currentActivity.ZanaMap.StartStopWatch();
                    }
                    else
                    {
                        _isMapZana = false;

                        // leave Zana Map
                        if (_currentActivity.ZanaMap != null)
                        {
                            _isMapZana = false;
                            _currentActivity.ZanaMap.StopStopWatch();
                            _currentActivity.ZanaMap.LastEnded = ev.EventTime;
                            if (!_currentActivity.ManuallyPaused)
                                _currentActivity.StartStopWatch();
                        }
                    }
                }
                else
                {
                    _isMapZana = false; //TMP_DEBUG

                    // Do not track Lab-Trials
                    if ((!sSourceArea.Contains("Trial of")) && (_currentActivity.Type != ACTIVITY_TYPES.LABYRINTH) && (_currentActivity.Type != ACTIVITY_TYPES.DELVE))
                    {
                        if (sTargetArea != _currentActivity.Area || _currentInstanceEndpoint != _currentActivity.InstanceEndpoint)
                        {
                            FinishActivity(_currentActivity, sTargetArea, actType, DateTime.Parse(ev.LogLine.Split(' ')[0] + " " + ev.LogLine.Split(' ')[1]));
                        }
                    }
                }
            }
            else
            {
                if (_currentActivity != null && _currentActivity.Type != ACTIVITY_TYPES.LABYRINTH)
                {
                    _currentActivity.StopStopWatch();
                    _currentActivity.LastEnded = ev.EventTime;

                    // PAUSE TIME
                    if (new ACTIVITY_TYPES[] { ACTIVITY_TYPES.MAP, ACTIVITY_TYPES.HEIST, ACTIVITY_TYPES.SIMULACRUM }.Contains(_currentActivity.Type))
                    {
                        if (_defaultMappings.CAMP_AREAS.Contains(sTargetArea) || sTargetArea.Contains("Hideout"))
                        {
                            _currentActivity.StartPauseTime(ev.EventTime);
                        }
                    }

                    if (_currentActivity.ZanaMap != null)
                    {
                        _currentActivity.ZanaMap.StopStopWatch();
                        _currentActivity.ZanaMap.LastEnded = ev.EventTime;
                    }
                }
            }

            _currentArea = sAreaName;
        }

        /// <summary>
        /// Handle player died event
        /// </summary>
        /// <param name="ev"></param>
        private void HandlePlayerDiedEvent(TrackingEvent ev)
        {
            string sPlayerName = ev.LogLine.Split(' ')[8];
            if (!_knownPlayerNames.Contains(sPlayerName))
            {
                IncrementStat("TotalKilledCount", ev.EventTime, 1);
                _lastDeathTime = DateTime.Now;

                // Lab?
                if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.LABYRINTH)
                {
                    _currentActivity.DeathCounter = 1;
                    FinishActivity(_currentActivity, null, ACTIVITY_TYPES.LABYRINTH, DateTime.Now);
                }

                // do not count deaths outsite activities to them
                if (CheckIfAreaIsMap(_currentArea) == false
                    && CheckIfAreaIsHeist(_currentArea) == false
                    && CheckIfAreaIsSimu(_currentArea) == false)
                {
                    return;
                }

                if (_currentActivity != null)
                {
                    if (_isMapZana)
                    {
                        if (_currentActivity.ZanaMap != null)
                        {
                            _currentActivity.ZanaMap.DeathCounter++;
                        }
                    }
                    else
                    {
                        _currentActivity.DeathCounter++;
                    }
                }


            }

        }

        /// <summary>
        /// Handle single event. Routes more complex calcs to dedicated methods.
        /// </summary>
        /// <param name="ev"></param>
        /// <param name="bInit"></param>
        private void HandleSingleEvent(TrackingEvent ev, bool bInit = false)
        {
            try
            {
                switch (ev.EventType)
                {
                   case EVENT_TYPES.ABNORMAL_DISCONNECT:
                        if (_currentActivity != null)
                        {
                            _log.Info("Abnormal disconnect found in log. Finishing Map.");
                            FinishActivity(_currentActivity, null, ACTIVITY_TYPES.MAP, ev.EventTime);
                        }
                        break;

                    case EVENT_TYPES.POE_CLIENT_START:
                        if(_currentActivity != null)
                        {
                            _currentActivity.IsFinished = true;
                            if(_currentActivity.ZanaMap != null)
                            {
                                _currentActivity.ZanaMap.IsFinished = true;
                            }
                            FinishActivity(_currentActivity, null, ACTIVITY_TYPES.MAP, ev.EventTime);
                        }
                        break;
                    case EVENT_TYPES.CHAT_CMD_RECEIVED:
                        string sCmd = ev.LogLine.Split(new string[] { "::" }, StringSplitOptions.None)[1];
                        if (_eventQueueInitizalized)
                        {
                            HandleChatCommand(sCmd);
                        }
                        break;
                    case EVENT_TYPES.ENTERED_AREA:
                        HandleAreaChangeEvent(ev);
                        break;
                    case EVENT_TYPES.PLAYER_DIED:
                        HandlePlayerDiedEvent(ev);
                        break;
                    case EVENT_TYPES.ELDER_KILLED:
                        IncrementStat("ElderKilled", ev.EventTime, 1);
                        _elderFightActive = false;
                        break;
                    case EVENT_TYPES.SHAPER_KILLED:
                        // shaper has 3x the same kill dialogue
                        _shaperKillsInFight++;
                        if (_shaperKillsInFight == 3)
                        {
                            IncrementStat("ShaperKilled", ev.EventTime, 1);
                            _shaperKillsInFight = 0;
                        }
                        break;
                    case EVENT_TYPES.SIRUS_FIGHT_STARTED:
                        IncrementStat("SirusStarted", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.SIRUS_KILLED:
                        IncrementStat("SirusKilled", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.INSTANCE_CONNECTED:
                        _currentInstanceEndpoint = GetEndpointFromInstanceEvent(ev);
                        break;
                    case EVENT_TYPES.VERITANIA_FIGHT_STARTED:
                        IncrementStat("VeritaniaStarted", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.BARAN_FIGHT_STARTED:
                        IncrementStat("BaranStarted", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.DROX_FIGHT_STARTED:
                        IncrementStat("DroxStarted", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.HUNTER_FIGHT_STARTED:
                        IncrementStat("HunterStarted", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.MAVEN_FIGHT_STARTED:
                        IncrementStat("MavenStarted", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.TRIALMASTER_STARTED:
                        IncrementStat("TrialMasterStarted", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.TRIALMASTER_VICTORY:
                        IncrementStat("TrialMasterSuccess", ev.EventTime, 1);
                        IncrementStat("TrialMasterVictory", ev.EventTime, 1);
                        if (_currentActivity != null)
                        {
                            _currentActivity.TrialMasterSuccess = true;
                            _currentActivity.TrialMasterFullFinished = true;
                        }
                        break;
                    case EVENT_TYPES.TRIALMASTER_TOOK_REWARD:
                        IncrementStat("TrialMasterTookReward", ev.EventTime, 1);
                        IncrementStat("TrialMasterSuccess", ev.EventTime, 1);
                        if (_currentActivity != null)
                        {
                            _currentActivity.TrialMasterSuccess = true;
                            _currentActivity.TrialMasterFullFinished = false;
                        }
                        break;
                    case EVENT_TYPES.MAVEN_KILLED:
                        IncrementStat("MavenKilled", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.TRIALMASTER_KILLED:
                        IncrementStat("TrialMasterKilled", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.VERITANIA_KILLED:
                        if (_lastEventType != EVENT_TYPES.VERITANIA_KILLED)
                        {
                            IncrementStat("VeritaniaKilled", ev.EventTime, 1);
                        }
                        break;
                    case EVENT_TYPES.DROX_KILLED:
                        if (_lastEventType != EVENT_TYPES.DROX_KILLED)
                        {
                            IncrementStat("DroxKilled", ev.EventTime, 1);
                        }
                        break;
                    case EVENT_TYPES.BARAN_KILLED:
                        if (_lastEventType != EVENT_TYPES.BARAN_KILLED)
                        {
                            IncrementStat("BaranKilled", ev.EventTime, 1);
                        }
                        break;
                    case EVENT_TYPES.HUNTER_KILLED:
                        if (_lastEventType != EVENT_TYPES.HUNTER_KILLED)
                        {
                            IncrementStat("HunterKilled", ev.EventTime, 1);
                        }
                        break;
                    case EVENT_TYPES.TRIALMASTER_ROUND_STARTED:
                        if (_currentActivity != null)
                        {
                            _currentActivity.TrialMasterCount += 1;
                        }
                        break;
                    case EVENT_TYPES.EINHAR_BEAST_CAPTURE:
                        IncrementStat("EinharCaptures", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.SHAPER_FIGHT_STARTED:
                        IncrementStat("ShaperTried", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.PARTYMEMBER_ENTERED_AREA:
                        AddKnownPlayerIfNotExists(ev.LogLine.Split(' ')[8]);
                        break;
                    case EVENT_TYPES.CATARINA_FIGHT_STARTED:
                        IncrementStat("CatarinaTried", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.CATARINA_KILLED:
                        IncrementStat("CatarinaKilled", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.DELIRIUM_ENCOUNTER:

                        if (CheckIfAreaIsMap(_currentArea) && _currentActivity != null)
                        {
                            if (_isMapZana && _currentActivity.ZanaMap != null)
                            {
                                _currentActivity.ZanaMap.AddTag("delirium");
                            }
                            else
                            {
                                _currentActivity.AddTag("delirium");
                            }
                        }
                        break;
                    case EVENT_TYPES.BLIGHT_ENCOUNTER:
                        if (CheckIfAreaIsMap(_currentArea) && _currentActivity != null)
                        {
                            if (_isMapZana && _currentActivity.ZanaMap != null)
                            {
                                _currentActivity.ZanaMap.AddTag("blight");
                            }
                            else
                            {
                                _currentActivity.AddTag("blight");
                            }
                        }
                        break;
                    case EVENT_TYPES.EINHAR_ENCOUNTER:
                        if (CheckIfAreaIsMap(_currentArea) && _currentActivity != null)
                        {
                            if (_isMapZana && _currentActivity.ZanaMap != null)
                            {
                                _currentActivity.ZanaMap.AddTag("einhar");
                            }
                            else
                            {
                                _currentActivity.AddTag("einhar");
                            }
                        }
                        break;
                    case EVENT_TYPES.INCURSION_ENCOUNTER:
                        if (CheckIfAreaIsMap(_currentArea) && _currentActivity != null)
                        {
                            if (_isMapZana && _currentActivity.ZanaMap != null)
                            {
                                _currentActivity.ZanaMap.AddTag("incursion");
                            }
                            else
                            {
                                _currentActivity.AddTag("incursion");
                            }
                        }
                        break;
                    case EVENT_TYPES.NIKO_ENCOUNTER:
                        if (CheckIfAreaIsMap(_currentArea) && _currentActivity != null)
                        {
                            if (_isMapZana && _currentActivity.ZanaMap != null)
                            {
                                _currentActivity.ZanaMap.AddTag("niko");
                            }
                            else
                            {
                                _currentActivity.AddTag("niko");
                            }
                        }
                        break;
                    case EVENT_TYPES.ZANA_ENCOUNTER:
                        if (CheckIfAreaIsMap(_currentArea) && _currentActivity != null)
                        {
                            _currentActivity.AddTag("zana");
                        }
                        break;
                    case EVENT_TYPES.SYNDICATE_ENCOUNTER:
                        if (CheckIfAreaIsMap(_currentArea) && _currentActivity != null)
                        {
                            if (_isMapZana && _currentActivity.ZanaMap != null)
                            {
                                _currentActivity.ZanaMap.AddTag("syndicate");
                            }
                            else
                            {
                                _currentActivity.AddTag("syndicate");
                            }
                        }
                        break;
                    case EVENT_TYPES.LEVELUP:
                        bool bIsMySelf = true;
                        foreach (string s in _knownPlayerNames)
                        {
                            if (ev.LogLine.Contains(s))
                            {
                                bIsMySelf = false;
                                break;
                            }
                        }
                        if (bIsMySelf)
                        {
                            IncrementStat("LevelUps", ev.EventTime, 1);
                            string[] spl = ev.LogLine.Split(' ');
                            int iLevel = Convert.ToInt32(spl[spl.Length - 1]);
                            if (iLevel > _numericStats["HighestLevel"])
                            {
                                SetStat("HighestLevel", ev.EventTime, iLevel);
                            }
                        }
                        break;
                    case EVENT_TYPES.SIMULACRUM_FULLCLEAR:
                        IncrementStat("SimulacrumCleared", ev.EventTime, 1);
                        break;
                    case EVENT_TYPES.LAB_FINISHED:
                        if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.LABYRINTH)
                        {
                            IncrementStat("LabsFinished", ev.EventTime, 1);
                            IncrementStat("LabsCompleted_" + _currentActivity.Area, ev.EventTime, 1);
                            _currentActivity.Success = true;
                            FinishActivity(_currentActivity, null, ACTIVITY_TYPES.MAP, ev.EventTime);
                        }
                        break;
                    case EVENT_TYPES.LAB_START_INFO_RECEIVED:
                     
                        break;
                    case EVENT_TYPES.NEXT_AREA_LEVEL_RECEIVED:
                        string sLvl = ev.LogLine.Split(new string[] { "Generating level " }, StringSplitOptions.None)[1]
                            .Split(' ')[0];
                        _nextAreaLevel = Convert.ToInt32(sLvl);
                        _currentAreaLevel = _nextAreaLevel;
                        break;
                    case EVENT_TYPES.EXP_DANNIG_ENCOUNTER:
                        if (_currentActivity != null && !_currentActivity.HasTag("dannig") && _currentActivity.Type == ACTIVITY_TYPES.MAP)
                        {
                            IncrementStat("ExpeditionEncounters", ev.EventTime, 1);
                            IncrementStat("ExpeditionEncounters_Dannig", ev.EventTime, 1);
                            AddTagAutoCreate("expedition", _currentActivity);
                            _currentActivity.Tags.Add("expedition");
                            _currentActivity.Tags.Add("dannig");
                        }
                        break;
                    case EVENT_TYPES.EXP_GWENNEN_ENCOUNTER:
                        if (_currentActivity != null && !_currentActivity.HasTag("gwennen") && _currentActivity.Type == ACTIVITY_TYPES.MAP)
                        {
                            IncrementStat("ExpeditionEncounters", ev.EventTime, 1);
                            IncrementStat("ExpeditionEncounters_Gwennen", ev.EventTime, 1);
                            _currentActivity.Tags.Add("expedition");
                            _currentActivity.Tags.Add("gwennen");
                        }
                        break;
                    case EVENT_TYPES.EXP_TUJEN_ENCOUNTER:
                        if (_currentActivity != null && !_currentActivity.HasTag("tujen") && _currentActivity.Type == ACTIVITY_TYPES.MAP)
                        {
                            IncrementStat("ExpeditionEncounters", ev.EventTime, 1);
                            IncrementStat("ExpeditionEncounters_Tujen", ev.EventTime, 1);
                            _currentActivity.Tags.Add("expedition");
                            _currentActivity.Tags.Add("tujen");
                        }
                        break;
                    case EVENT_TYPES.EXP_ROG_ENCOUNTER:
                        if (_currentActivity != null && !_currentActivity.HasTag("rog") && _currentActivity.Type == ACTIVITY_TYPES.MAP)
                        {
                            IncrementStat("ExpeditionEncounters", ev.EventTime, 1);
                            IncrementStat("ExpeditionEncounters_Rog", ev.EventTime, 1);
                            _currentActivity.Tags.Add("expedition");
                            _currentActivity.Tags.Add("rog");
                        }
                        break;
                    case EVENT_TYPES.HEIST_GIANNA_SPEAK:
                        if(_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.HEIST)
                        {
                            _currentActivity.AddTag("gianna");
                        }
                        break;
                    case EVENT_TYPES.HEIST_HUCK_SPEAK:
                        if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.HEIST)
                        {
                            _currentActivity.AddTag("huck");
                        }
                        break;
                    case EVENT_TYPES.HEIST_ISLA_SPEAK:
                        if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.HEIST)
                        {
                            _currentActivity.AddTag("isla");
                        }
                        break;
                    case EVENT_TYPES.HEIST_NENET_SPEAK:
                        if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.HEIST)
                        {
                            _currentActivity.AddTag("nenet");
                        }
                        break;
                    case EVENT_TYPES.HEIST_NILES_SPEAK:
                        if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.HEIST)
                        {
                            _currentActivity.AddTag("niles");
                        }
                        break;
                    case EVENT_TYPES.HEIST_TIBBS_SPEAK:
                        if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.HEIST)
                        {
                            _currentActivity.AddTag("tibbs");
                        }
                        break;
                    case EVENT_TYPES.HEIST_TULLINA_SPEAK:
                        if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.HEIST)
                        {
                            _currentActivity.AddTag("tullina");
                        }
                        break;
                    case EVENT_TYPES.HEIST_VINDERI_SPEAK:
                        if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.HEIST)
                        {
                            _currentActivity.AddTag("vinderi");
                        }
                        break;
                    case EVENT_TYPES.HEIST_KARST_SPEAK:
                        if (_currentActivity != null && _currentActivity.Type == ACTIVITY_TYPES.HEIST)
                        {
                            _currentActivity.AddTag("karst");
                        }
                        break;

                }

                // Sometimes conqueror fire their death speech twice...
                EVENT_TYPES[] checkConqTypes =
                    {
                    EVENT_TYPES.VERITANIA_FIGHT_STARTED,
                    EVENT_TYPES.VERITANIA_KILLED,
                    EVENT_TYPES.HUNTER_KILLED ,
                    EVENT_TYPES.HUNTER_FIGHT_STARTED,
                    EVENT_TYPES.HUNTER_KILLED,
                    EVENT_TYPES.BARAN_FIGHT_STARTED,
                    EVENT_TYPES.BARAN_KILLED,
                    EVENT_TYPES.DROX_FIGHT_STARTED,
                    EVENT_TYPES.DROX_KILLED,
                };

                if (checkConqTypes.Contains<EVENT_TYPES>(ev.EventType))
                {
                    _lastEventType = ev.EventType;
                }

                if (!bInit) TextLogEvent(ev);
            }
            catch(Exception ex)
            {
                _log.Warn("ParseError -> Ex.Message: " + ex.Message + ", LogLine: " + ev.LogLine);
                _log.Debug(ex.ToString());
            }
        }

        /// <summary>
        /// Increment a stat with an defined value. Updates the database.
        /// 
        /// </summary>
        /// <param name="s_key"></param>
        /// <param name="dt"></param>
        /// <param name="i_value">default=1</param>
        private void IncrementStat(string s_key, DateTime dt, int i_value = 1)
        {
            _numericStats[s_key] += i_value;

            SqliteCommand cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "INSERT INTO tx_stats (timestamp, stat_name, stat_value) VALUES (" + ((DateTimeOffset)dt).ToUnixTimeSeconds() + ", '" + s_key + "', " + _numericStats[s_key] + ")";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Update a stat with a fixed value. Updates the database
        /// </summary>
        /// <param name="s_key"></param>
        /// <param name="dt"></param>
        /// <param name="i_value"></param>
        private void SetStat(string s_key, DateTime dt, int i_value)
        {
            _numericStats[s_key] = i_value;

            SqliteCommand cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "INSERT INTO tx_stats (timestamp, stat_name, stat_value) VALUES (" + ((DateTimeOffset)dt).ToUnixTimeSeconds() + ", '" + s_key + "', " + _numericStats[s_key] + ")";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Extract the instance endpoint from a log line.
        /// </summary>
        /// <param name="ev"></param>
        /// <returns></returns>
        private string GetEndpointFromInstanceEvent(TrackingEvent ev)
        {
            return ev.LogLine.Split(new String[] { "Connecting to instance server at "}, StringSplitOptions.None)[1];
        }

        /// <summary>
        /// Finishs the current activity.
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="sNextMap">next map to start. Set to null if there is none</param>
        /// <param name="sNextMapType"></param>
        /// <param name="dtNextMapStarted"></param>
        private void FinishActivity(TrackedActivity activity, string sNextMap, ACTIVITY_TYPES sNextMapType, DateTime dtNextMapStarted)
        {
            _currentActivity.StopStopWatch();

            TimeSpan ts;
            TimeSpan tsZana;
            int iSeconds = 0;
            int iSecondsZana = 0;


            // Filter out invalid labs (discnnect etc)
            if(activity.Type == ACTIVITY_TYPES.LABYRINTH)
            {
                if(activity.Area == "Unknown")
                {
                    // no area changes logged for some old lab runs :/
                    activity.Success = activity.DeathCounter == 0;
                }

                // Labs must be successfull or death counter 1
                if (activity.Success != true && activity.DeathCounter == 0)
                {
                    _log.Info("Filtered out lab run [time=" + activity.Started + ", area: " + activity.Area + "]. Reason Success=False AND DeathCounter = 0. Maybe disconnect or game crash while lab.");
                    _currentActivity = null;
                    return;
                }
               
            }


            if(!_eventQueueInitizalized)
            {
                ts = (activity.LastEnded - activity.Started);
                try
                {
                    iSeconds = Convert.ToInt32(ts.TotalSeconds);
                    iSeconds -= Convert.ToInt32(activity.PausedTime);
                }
                catch
                {
                    iSeconds = 0;
                }
                
                if (activity.ZanaMap != null)
                {
                    tsZana = (activity.ZanaMap.LastEnded - activity.ZanaMap.Started);
                    iSecondsZana = Convert.ToInt32(tsZana.TotalSeconds);
                }
            }
            else
            {
                ts = activity.StopWatchTimeSpan;
                iSeconds = Convert.ToInt32(ts.TotalSeconds);
                if (activity.ZanaMap != null)
                {
                    tsZana = activity.ZanaMap.StopWatchTimeSpan;
                    iSecondsZana = Convert.ToInt32(tsZana.TotalSeconds);
                }
            }

            _currentActivity.TotalSeconds = iSeconds;
            if(!_eventHistory.Contains(_currentActivity))
            {
                _eventHistory.Insert(0, _currentActivity);
            }
            
            TimeSpan tsMain = TimeSpan.FromSeconds(iSeconds);
            activity.CustomStopWatchValue = String.Format("{0:00}:{1:00}:{2:00}",
                      tsMain.Hours, tsMain.Minutes, tsMain.Seconds);

            if(!_parsedActivities.Contains(activity.TimeStamp.ToString() + "_" + activity.Area))
            {
                AddMapLvItem(activity);
                SaveToActivityLog(((DateTimeOffset)activity.Started).ToUnixTimeSeconds(), GetStringFromActType(activity.Type), activity.Area, activity.AreaLevel, iSeconds, activity.DeathCounter, activity.TrialMasterCount, false, activity.Tags, activity.Success);
              
                // Refresh ListView
                if (_eventQueueInitizalized) DoSearch();
            }
            
          
            if (activity.ZanaMap != null)
            {
                TimeSpan tsZanaMap = TimeSpan.FromSeconds(iSecondsZana);
                activity.ZanaMap.CustomStopWatchValue = String.Format("{0:00}:{1:00}:{2:00}",
                       tsZanaMap.Hours, tsZanaMap.Minutes, tsZanaMap.Seconds);
                _eventHistory.Insert(0, _currentActivity.ZanaMap);

                if (!_parsedActivities.Contains(activity.ZanaMap.TimeStamp.ToString() + "_" + activity.ZanaMap.Area))
                {
                    AddMapLvItem(activity.ZanaMap, true);
                    SaveToActivityLog(((DateTimeOffset)activity.ZanaMap.Started).ToUnixTimeSeconds(), GetStringFromActType(activity.ZanaMap.Type), activity.ZanaMap.Area, activity.ZanaMap.AreaLevel, iSecondsZana, activity.ZanaMap.DeathCounter, activity.ZanaMap.TrialMasterCount, true, activity.ZanaMap
                        .Tags, activity.ZanaMap.Success);

                    // Refresh ListView
                    if (_eventQueueInitizalized) DoSearch();
                }
            }

            if (sNextMap != null)
            {
                _currentActivity = new TrackedActivity
                {
                    Area = sNextMap,
                    Type = sNextMapType,
                    AreaLevel = _nextAreaLevel,
                    InstanceEndpoint = _currentInstanceEndpoint,
                    Started = dtNextMapStarted
                };
                _nextAreaLevel = 0;
                _currentActivity.StartStopWatch();
            }
            else
            {
                _currentActivity = null;
            }

            if(activity.Type == ACTIVITY_TYPES.HEIST)
            {
                IncrementStat("TotalHeistsDone", activity.Started, 1);
                IncrementStat("HeistsFinished_" + activity.Area, activity.Started, 1);
            }
            else if(activity.Type == ACTIVITY_TYPES.MAP)
            {
                IncrementStat("TotalMapsDone", activity.Started, 1);
                IncrementStat("MapsFinished_" + activity.Area, activity.Started, 1);
                IncrementStat("MapTierFinished_T" + activity.MapTier.ToString(), activity.Started, 1);

                if(activity.ZanaMap != null)
                {
                    IncrementStat("TotalMapsDone", activity.ZanaMap.Started, 1);
                    IncrementStat("MapsFinished_" + activity.ZanaMap.Area, activity.ZanaMap.Started, 1);
                    IncrementStat("MapTierFinished_T" + activity.ZanaMap.MapTier.ToString(),activity.ZanaMap.Started, 1);
                }
            }
            else if (activity.Type == ACTIVITY_TYPES.SIMULACRUM)
            {
                IncrementStat("SimulacrumFinished_" + activity.Area, activity.Started, 1);
            }
            else if (activity.Type == ACTIVITY_TYPES.TEMPLE)
            {
                IncrementStat("TemplesDone", activity.Started, 1);
            }

            if(_eventQueueInitizalized)
            {
                RenderGlobalDashboard();
                RenderHeistDashboard();
                RenderLabDashboard();
                RenderMappingDashboard();
            }
        }

        /// <summary>
        /// Read the statistics cache
        /// </summary>
        private void ReadStatsCache()
        {
            if(File.Exists(_cachePath))
            {
                StreamReader r = new StreamReader(_cachePath);
                string line;
                string statID = "";
                int statValue = 0;
                int iLine = 0;
                while ((line = r.ReadLine()) != null)
                {
                    if (iLine == 0)
                    {
                        _lastHash = Convert.ToInt32(line.Split(';')[1]);
                    }
                    else
                    {
                        statID = line.Split(';')[0];
                        statValue = Convert.ToInt32(line.Split(';')[1]);
                        if(_numericStats.ContainsKey(statID))
                        {
                            _numericStats[line.Split(';')[0]] = statValue;
                            _log.Info("StatsCacheRead -> " + statID  + "=" + statValue.ToString());
                        }
                        else
                        {
                            _log.Warn("StatsCacheRead -> Unknown stat '" + statID + "' in stats.cache, maybe from an older version.");
                        }
                        
                    }

                    iLine++;
                }
                r.Close();
            }
        }

        /// <summary>
        /// Write the statistics cache
        /// </summary>
        private void SaveStatsCache()
        {
            StreamWriter wrt = new StreamWriter(_cachePath);
            wrt.WriteLine("last;" + _lastHash.ToString());
            foreach(KeyValuePair<string, int> kvp in _numericStats)
            {
                wrt.WriteLine(kvp.Key + ";" + kvp.Value);
            }
            wrt.Close();
        }

        /// <summary>
        /// Simply save the current app version to VERSION.txt
        /// </summary>
        private void SaveVersion()
        {
            StreamWriter wrt = new StreamWriter(_myAppData + @"\VERSION.txt");
            wrt.WriteLine(APPINFO.VERSION);
            wrt.Close();
        }

        /// <summary>
        /// Dump an event to logfile
        /// </summary>
        /// <param name="ev"></param>
        private void LogEvent(TrackingEvent ev)
        {
            _log.Info(ev.ToString());
        }

        /// <summary>
        /// Add event to EventLog
        /// </summary>
        /// <param name="ev"></param>
        private void TextLogEvent(TrackingEvent ev)
        {
            this.Invoke((MethodInvoker)delegate
            {
                textBoxLogView.Text += ev.ToString() + Environment.NewLine;
            });
        }

        /// <summary>
        /// Reset and reload the Activity-History ListView
        /// </summary>
        public void ResetMapHistory()
        {
            _lvmActlog.ClearLvItems();
            _lvmActlog.Columns.Clear();

            ColumnHeader
                chTime = new ColumnHeader() { Name = "actlog_time", Text = "Time", Width = Convert.ToInt32(ReadSetting("layout.listview.cols.actlog_time.width", "60")) },
                chType = new ColumnHeader() { Name = "actlog_type", Text = "Type", Width = Convert.ToInt32(ReadSetting("layout.listview.cols.actlog_type.width", "60")) },
                chArea = new ColumnHeader() { Name = "actlog_area", Text = "Area", Width = Convert.ToInt32(ReadSetting("layout.listview.cols.actlog_area.width", "60")) },
                chLvl = new ColumnHeader() { Name = "actlog_lvl", Text = "Level/Tier", Width = Convert.ToInt32(ReadSetting("layout.listview.cols.actlog_lvl.width", "60")) },
                chStopwatch = new ColumnHeader() { Name = "actlog_stopwatch", Text = "Stopwatch", Width = Convert.ToInt32(ReadSetting("layout.listview.cols.actlog_stopwatch.width", "60")) },
                chDeath = new ColumnHeader() { Name = "actlog_death", Text = "Deaths", Width = Convert.ToInt32(ReadSetting("layout.listview.cols.actlog_death.width", "60")) };


            _lvmActlog.Columns.Add(chTime);
            _lvmActlog.Columns.Add(chType);
            _lvmActlog.Columns.Add(chArea);
            _lvmActlog.Columns.Add(chLvl);
            _lvmActlog.Columns.Add(chStopwatch);
            _lvmActlog.Columns.Add(chDeath);


            foreach (ActivityTag tag in _tags)
            {
                if(tag.ShowInListView)
                {
                    ColumnHeader ch = new ColumnHeader()
                    {
                        Name = "actlog_tag_" + tag.ID,
                        Text = tag.DisplayName,
                        Width = Convert.ToInt32(ReadSetting("layout.listview.cols.actlog_tag_" + tag.ID + ".width", "60"))
                    };
                    _lvmActlog.Columns.Add(ch);
                }
            }

            AddActivityLvItems();

        }

        /// <summary>
        /// Add maximum numberof ListViewItems to Listview
        /// TODO: filter before adding!!!
        /// </summary>
        private void AddActivityLvItems()
        {
            foreach (TrackedActivity act in _eventHistory)
            {
                AddMapLvItem(act, act.IsZana, -1, false);
            }
            _lvmActlog.FilterByRange(0, Convert.ToInt32(ReadSetting("actlog.maxitems", "500")));
        }

        /// <summary>
        /// Add listview Item for single activity
        /// </summary>
        /// <param name="map"></param>
        /// <param name="bZana"></param>
        /// <param name="iPos"></param>
        /// <param name="b_display"></param>
        private void AddMapLvItem(TrackedActivity map, bool bZana = false, int iPos = 0, bool b_display = true)
        {
            this.Invoke((MethodInvoker)delegate
            {
                ListViewItem lvi = new ListViewItem(map.Started.ToString());
                string sName = map.Area;
                string sTier = "";

                if(map.AreaLevel == 0)
                {
                    sTier = "-";
                }
                else if(map.Type == ACTIVITY_TYPES.MAP)
                {
                    sTier = "T" + map.MapTier.ToString();
                }
                else
                {
                    sTier = map.AreaLevel.ToString();
                }

                if (bZana)
                    sName += " (Zana)";
                lvi.SubItems.Add(GetStringFromActType(map.Type));
                lvi.SubItems.Add(map.Area);
                lvi.SubItems.Add(sTier);
                lvi.SubItems.Add(map.StopWatchValue.ToString());
                lvi.SubItems.Add(map.DeathCounter.ToString());

                foreach(ActivityTag t in _tags)
                {
                    if(t.ShowInListView)
                    {
                        lvi.SubItems.Add(map.Tags.Contains(t.ID) ? "X" : "");
                    }
                }

                if(iPos == -1)
                {
                    _lvmActlog.AddLvItem(lvi, map.TimeStamp + "_" + map.Area, b_display);
                }
                else
                {
                    _lvmActlog.InsertLvItem(lvi, map.TimeStamp + "_" + map.Area, iPos, b_display);
                }
                
            });
        }

        /// <summary>
        /// Find matching activity to Item name
        /// </summary>
        /// <param name="s_name"></param>
        /// <returns></returns>
        private TrackedActivity GetActivityFromListItemName(string s_name)
        {
            foreach(TrackedActivity ta in _eventHistory)
            {
                if (ta.TimeStamp + "_" + ta.Area == s_name)
                    return ta;
            }
            return null;
        }

        /// <summary>
        /// Convert an activity type to string
        /// </summary>
        /// <param name="a_type"></param>
        /// <returns></returns>
        private string GetStringFromActType(ACTIVITY_TYPES a_type)
        {
            return a_type.ToString().ToLower();
        }

        /// <summary>
        /// Extract an arae name out of a log line
        /// </summary>
        /// <param name="ev"></param>
        /// <returns></returns>
        private string GetAreaNameFromEvent(TrackingEvent ev)
        {
            string sArea = ev.LogLine.Split(new string[] { "You have entered" }, StringSplitOptions.None)[1]
                .Replace(".", "").Trim();
            return sArea.Replace("'", "");
        }
     
        /// <summary>
        /// Handle the GUI updates
        /// </summary>
        private void UpdateGUI()
        {
            TimeSpan tsAreaTime = (DateTime.Now - this._inAreaSince);
            checkBox1.Checked = _showGridInActLog;
            checkBox2.Checked = _showGridInStats;
            ReadBackupList();
            listBox1.DataSource = _backups;
            

            if (_eventQueueInitizalized)
            {
                _loadScreenWindow.Close();
                this.Invoke((MethodInvoker)delegate
                {
                    this.Show();

                    if(_restoreMode)
                    {
                        _restoreMode = false;
                        if (_restoreOk)
                        {
                            MessageBox.Show("Successfully restored from Backup!");
                        }
                        else
                        {
                            MessageBox.Show("Error restoring from backup: " +
                                Environment.NewLine +
                                Environment.NewLine + _failedRestoreReason +
                                Environment.NewLine);

                        }
                    }

                    RenderTagsForTracking();
                    RenderTagsForConfig();
                    textBox1.Text = ReadSetting("poe_logfile_path");

                    label74.Text = "items: " + _lvmActlog.listView.Items.Count.ToString();

                  
                   

                    if(listViewStats.Items.Count > 0)
                    {
                        for (int i = 0; i < _numericStats.Count; i++)
                        {
                            KeyValuePair<string, int> kvp = _numericStats.ElementAt(i);
                            _lvmStats.GetLvItem("stats_" + kvp.Key).SubItems[1].Text = kvp.Value.ToString();
                        }
                    }

                    if(!_listViewInitielaized)
                    {
                        DoSearch();
                        _listViewInitielaized = true;
                    }
                    
                    labelCurrArea.Text = _currentArea;
                    labelCurrentAreaLvl.Text = _currentAreaLevel > 0 ? _currentAreaLevel.ToString() : "-";
                    labelLastDeath.Text = _lastDeathTime.Year > 2000 ? _lastDeathTime.ToString() : "-";
                    
                    if(_currentArea.Contains("Hideout"))
                    {
                        labelCurrActivity.Text = "In Hideout";
                    }
                    else
                    {
                        if (_currentActivity != null)
                        {
                            labelCurrActivity.Text = _currentActivity.Type.ToString();
                        }
                        else
                        {
                            labelCurrActivity.Text = "Nothing";
                        }
                    }


                    if (_currentActivity != null)
                    {
                        string sTier = "";

                       

                        if (_currentActivity.Type == ACTIVITY_TYPES.SIMULACRUM)
                        {
                            _currentActivity.AreaLevel = 75;
                        }

                        if (_currentActivity.AreaLevel > 0)
                        {
                            if(_currentActivity.Type == ACTIVITY_TYPES.MAP)
                            {
                                sTier = "T" + _currentActivity.MapTier.ToString();
                            }
                            else
                            {
                                sTier = "Lvl. " + _currentActivity.AreaLevel.ToString();
                            }
                        }
                       

                        if (_isMapZana && _currentActivity.ZanaMap != null)
                        {
                            labelStopWatch.Text = _currentActivity.ZanaMap.StopWatchValue.ToString();
                            labelTrackingArea.Text = _currentActivity.ZanaMap.Area + " (" + sTier + ", Zana)";
                            labelTrackingDied.Text = _currentActivity.ZanaMap.DeathCounter.ToString();
                            labelTrackingType.Text = GetStringFromActType(_currentActivity.Type).ToUpper();
                            pictureBox19.Hide();
                        }
                        else
                        {
                            labelStopWatch.Text = _currentActivity.StopWatchValue.ToString();
                            labelTrackingArea.Text = _currentActivity.Area + " (" + sTier + ")"; ;
                            labelTrackingType.Text = GetStringFromActType(_currentActivity.Type).ToUpper();
                            labelTrackingDied.Text = _currentActivity.DeathCounter.ToString();
                            pictureBox19.Show();
                        }
                    }
                    else
                    {
                        labelTrackingDied.Text = "0";
                        labelTrackingArea.Text = "-";
                        labelStopWatch.Text = "00:00:00";
                        labelTrackingType.Text = "Enter an ingame activity to auto. start tracking.";
                    }

                    labelElderStatus.ForeColor = _numericStats["ElderKilled"] > 0 ? Color.Green : Color.Red;
                    labelElderStatus.Text = _numericStats["ElderKilled"] > 0 ? "Yes" : "No";
                    labelElderKillCount.Text = _numericStats["ElderKilled"].ToString() + "x";
                    labelElderTried.Text = _numericStats["ElderTried"].ToString() + "x";

                    labelShaperStatus.ForeColor = _numericStats["ShaperKilled"] > 0 ? Color.Green : Color.Red;
                    labelShaperStatus.Text = _numericStats["ShaperKilled"] > 0 ? "Yes" : "No";
                    labelShaperKillCount.Text = _numericStats["ShaperKilled"].ToString() + "x";
                    labelShaperTried.Text = _numericStats["ShaperTried"].ToString() + "x";

                    labelSirusStatus.ForeColor = _numericStats["SirusKilled"] > 0 ? Color.Green : Color.Red;
                    labelSirusStatus.Text = _numericStats["SirusKilled"] > 0 ? "Yes" : "No";
                    labelSirusKillCount.Text = _numericStats["SirusKilled"].ToString() + "x";
                    labelSirusTries.Text = _numericStats["SirusStarted"].ToString() + "x";

                    label80.ForeColor = _numericStats["CatarinaKilled"] > 0 ? Color.Green : Color.Red;
                    label80.Text = _numericStats["CatarinaKilled"] > 0 ? "Yes" : "No";
                    label78.Text = _numericStats["CatarinaKilled"].ToString() + "x";
                    label82.Text = _numericStats["CatarinaTried"].ToString() + "x";

                    labelVeritaniaStatus.ForeColor = _numericStats["VeritaniaKilled"] > 0 ? Color.Green : Color.Red;
                    labelVeritaniaKillCount.Text = _numericStats["VeritaniaKilled"].ToString() + "x";
                    labelVeritaniaStatus.Text = _numericStats["VeritaniaKilled"] > 0 ? "Yes" : "No";
                    labelVeritaniaTries.Text = _numericStats["VeritaniaStarted"].ToString() + "x";

                    labelHunterStatus.ForeColor = _numericStats["HunterKilled"] > 0 ? Color.Green : Color.Red;
                    labelHunterStatus.Text = _numericStats["HunterKilled"] > 0 ? "Yes" : "No";
                    labelHunterKillCount.Text = _numericStats["HunterKilled"].ToString() + "x";
                    labelHunterTries.Text = _numericStats["HunterStarted"].ToString() + "x";

                    labelDroxStatus.ForeColor = _numericStats["DroxKilled"] > 0 ? Color.Green : Color.Red;
                    labelDroxStatus.Text = _numericStats["DroxKilled"] > 0 ? "Yes" : "No";
                    labelDroxKillCount.Text = _numericStats["DroxKilled"].ToString() + "x";
                    labelDroxTries.Text = _numericStats["DroxStarted"].ToString() + "x";

                    labelBaranStatus.ForeColor = _numericStats["BaranKilled"] > 0 ? Color.Green : Color.Red;
                    labelBaranStatus.Text = _numericStats["BaranKilled"] > 0 ? "Yes" : "No";
                    labelBaranKillCount.Text = _numericStats["BaranKilled"].ToString() + "x";
                    labelBaranTries.Text = _numericStats["BaranStarted"].ToString() + "x";

                    labelTrialMasterStatus.ForeColor = _numericStats["TrialMasterKilled"] > 0 ? Color.Green : Color.Red;
                    labelTrialMasterStatus.Text = _numericStats["TrialMasterKilled"] > 0 ? "Yes" : "No";
                    labelTrialMasterKilled.Text = _numericStats["TrialMasterKilled"].ToString() + "x";
                    labelTrialMasterTried.Text = _numericStats["TrialMasterStarted"].ToString() + "x";

                    labelMavenStatus.ForeColor = _numericStats["MavenKilled"] > 0 ? Color.Green : Color.Red;
                    labelMavenStatus.Text = _numericStats["MavenKilled"] > 0 ? "Yes" : "No";
                    labelMavenKilled.Text = _numericStats["MavenKilled"].ToString() + "x";
                    labelMavenTried.Text = _numericStats["MavenStarted"].ToString() + "x";
                   

                    // MAP Dashbaord
                    if (_mapDashboardUpdateRequested)
                    {
                        RenderMappingDashboard();
                        _mapDashboardUpdateRequested = false;
                    }

                    // LAB Dashbaord
                    if (_labDashboardUpdateRequested)
                    {
                        RenderLabDashboard();
                        _labDashboardUpdateRequested = false;
                    }

                    // HEIST Dashbaord
                    if (_heistDashboardUpdateRequested)
                    {
                        RenderHeistDashboard();
                        _heistDashboardUpdateRequested = false;
                    }

                    // Global Dashbaord
                    if (_globalDashboardUpdateRequested)
                    {
                        RenderGlobalDashboard();
                        _globalDashboardUpdateRequested = false;
                    }


                });
            }
        }

        /// <summary>
        /// Read all settings
        /// </summary>
        private void ReadSettings()
        {
            this._showGridInActLog = Convert.ToBoolean(ReadSetting("ActivityLogShowGrid"));
            this._showGridInStats = Convert.ToBoolean(ReadSetting("StatsShowGrid"));
            this._timeCapLab = Convert.ToInt32(ReadSetting("TimeCapLab", "3600"));
            this._timeCapMap = Convert.ToInt32(ReadSetting("TimeCapMap", "3600"));
            this._timeCapHeist = Convert.ToInt32(ReadSetting("TimeCapHeist", "3600"));

            textBox9.Text = _timeCapMap.ToString();
            textBox10.Text = _timeCapLab.ToString();
            textBox11.Text = _timeCapHeist.ToString();

            listViewActLog.GridLines = _showGridInActLog;
            listViewStats.GridLines = _showGridInStats;
        }

        /// <summary>
        /// Read single setting
        /// </summary>
        /// <param name="key"></param>
        /// <param name="s_default"></param>
        /// <returns></returns>
        public string ReadSetting(string key, string s_default = null)
        {
            return _mySettings.ReadSetting(key, s_default);
        }

        /// <summary>
        /// Exit
        /// </summary>
        private void Exit()
        {
            _exit = true;
            if (_currentActivity != null)
                FinishActivity(_currentActivity, null, _currentActivity.Type, DateTime.Now);
            _log.Info("Exitting.");
            Application.Exit();
        }

        public void RenderLabDashboard()
        {
            Dictionary<string, int> labCounts;
            Dictionary<string, double> labAvgTimes;
            Dictionary<string, TrackedActivity> labBestTimes;

            labCounts = new Dictionary<string, int>();
            labAvgTimes = new Dictionary<string, double>();
            labBestTimes = new Dictionary<string, TrackedActivity>();

            foreach(string s in labs)
            {
                if (_labDashboardHideUnknown && s == "Unknown")
                    continue;

                labCounts.Add(s, 0);
                labAvgTimes.Add(s, 0);
                labBestTimes.Add(s, null);
            }

            // Lab counts
            foreach(TrackedActivity act in _eventHistory)
            {
                if(act.Type == ACTIVITY_TYPES.LABYRINTH && act.DeathCounter == 0)
                {
                    if(labCounts.ContainsKey(act.Area))
                    {
                        labCounts[act.Area]++;
                    }
                }
            }

            // Avg lab times
            foreach(string s in labs)
            {
                if (!labs.Contains(s))
                    continue;

                if (_labDashboardHideUnknown && s == "Unknown")
                    continue;

                int iSum = 0;
                int iCount = 0;

                foreach(TrackedActivity act in _eventHistory)
                {
                    if(act.Type == ACTIVITY_TYPES.LABYRINTH && act.DeathCounter == 0)
                    {
                        if(act.Area == s)
                        {
                            // Average
                            iCount++;

                            if(act.TotalSeconds < _timeCapLab)
                            {
                                iSum += act.TotalSeconds;
                            }
                            else
                            {
                                iSum += _timeCapLab;
                            }

                            // Top 
                            if(labBestTimes[s] == null)
                            {
                                labBestTimes[s] = act;
                            }
                            else
                            {
                                if(labBestTimes[s].TotalSeconds > act.TotalSeconds)
                                {
                                    labBestTimes[s] = act;
                                }
                            }
                        }
                    }
                }

                if(iSum > 0 && iCount > 0)
                {
                    labAvgTimes[s] = iSum / iCount;
                }
            }

            // UPdate Lab chart
            MethodInvoker mi = delegate
            {
                chart4.Series[0].Points.Clear();
                chart5.Series[0].Points.Clear();
                listView3.Items.Clear();
                foreach (KeyValuePair<string,int> kvp in labCounts)
                {
                    string sName = kvp.Key;
                    if(sName != "The Labyrinth")
                    {
                        sName = sName.Replace("The ", "").Replace(" Labyrinth", "");
                    }
                    if(sName == "Unknown")
                    {
                        sName += "*";
                    }
                    chart4.Series[0].Points.AddXY(sName, kvp.Value);
                    chart5.Series[0].Points.AddXY(sName, Math.Round(labAvgTimes[kvp.Key] / 60, 2));

                    ListViewItem lvi = new ListViewItem(kvp.Key);
                    
                    if(labBestTimes[kvp.Key] != null)
                    {
                        lvi.SubItems.Add(labBestTimes[kvp.Key].StopWatchValue);
                        lvi.SubItems.Add(labBestTimes[kvp.Key].Started.ToString());
                    }
                    else
                    {
                        lvi.SubItems.Add("-");
                        lvi.SubItems.Add("-");
                    }
                    listView3.Items.Add(lvi);


                }
            };
            this.BeginInvoke(mi);
            
        }

        public void RenderGlobalDashboard()
        {
            Dictionary<ACTIVITY_TYPES, double> typeList = new Dictionary<ACTIVITY_TYPES, double>
            {
                { ACTIVITY_TYPES.MAP, 0 },
                { ACTIVITY_TYPES.HEIST, 0 },
                { ACTIVITY_TYPES.DELVE, 0 },
                { ACTIVITY_TYPES.LABYRINTH, 0 },
                { ACTIVITY_TYPES.SIMULACRUM, 0 },
                { ACTIVITY_TYPES.TEMPLE, 0 },
            };

            Dictionary<ACTIVITY_TYPES, Color> colorList = new Dictionary<ACTIVITY_TYPES, Color>
            {
                { ACTIVITY_TYPES.MAP, Color.Green },
                { ACTIVITY_TYPES.HEIST, Color.Red },
                { ACTIVITY_TYPES.DELVE, Color.Orange },
                { ACTIVITY_TYPES.LABYRINTH, Color.DarkTurquoise },
                { ACTIVITY_TYPES.SIMULACRUM, Color.Gray },
                { ACTIVITY_TYPES.TEMPLE, Color.GreenYellow },
            };

            foreach (TrackedActivity act in _eventHistory)
            {
                int iCap = 3600;

                switch(act.Type)
                {
                    case ACTIVITY_TYPES.MAP:
                        iCap = _timeCapMap;
                        break;
                    case ACTIVITY_TYPES.LABYRINTH:
                        iCap = _timeCapLab;
                        break;
                    case ACTIVITY_TYPES.HEIST:
                        iCap = _timeCapHeist;
                        break;
                }

                // Filter out
                if(act.TotalSeconds < iCap)
                {
                    typeList[act.Type] += act.TotalSeconds;
                }
                else
                {
                    typeList[act.Type] += iCap;
                }
            }

            chart8.Series[0].Points.Clear();
            foreach(KeyValuePair<ACTIVITY_TYPES,double> kvp in typeList)
            {
                chart8.Series[0].Points.AddXY(kvp.Key.ToString(), Math.Round(kvp.Value / 60 / 60, 1));
                chart8.Series[0].Points.Last().Color = colorList[kvp.Key];
            }
        }

        public void RenderHeistDashboard()
        {
            List<KeyValuePair<string, int>> tmpList = new List<KeyValuePair<string, int>>();
            List<KeyValuePair<string, int>> top10 = new List<KeyValuePair<string, int>>();
            Dictionary<string, int> tmpListTags = new Dictionary<string, int>();
            List<KeyValuePair<string, int>> top10Tags = new List<KeyValuePair<string, int>>();

            foreach (string s in _defaultMappings.HEIST_AREAS)
            {
                tmpList.Add(new KeyValuePair<string, int>(s, _numericStats["HeistsFinished_" + s]));
            }

            tmpList.Sort(
                delegate (KeyValuePair<string, int> pair1,
                KeyValuePair<string, int> pair2)
                {
                    return pair1.Value.CompareTo(pair2.Value);
                });
            tmpList.Reverse();
            top10.AddRange(tmpList);
            listView4.Items.Clear();
            foreach (KeyValuePair<string, int> kvp in top10)
            {
                ListViewItem lvi = new ListViewItem(kvp.Key);
                lvi.SubItems.Add(kvp.Value.ToString());
                listView4.Items.Add(lvi);
            }

            // TAG CALC
            tmpList.Clear();
            foreach (ActivityTag tg in Tags)
            {
                tmpListTags.Add(tg.ID, 0);
            }

            foreach (TrackedActivity act in _eventHistory)
            {
                if (act.Type == ACTIVITY_TYPES.HEIST)
                {
                    foreach (string s in act.Tags)
                    {
                        if (!String.IsNullOrEmpty(s))
                        {
                            if(tmpListTags.ContainsKey(s))
                            {
                                int iVal = tmpListTags[s];
                                tmpListTags[s]++;
                            }
                           
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, int> kvp in tmpListTags)
            {
                tmpList.Add(new KeyValuePair<string, int>(kvp.Key, kvp.Value));
            }

            tmpList.Sort(
                delegate (KeyValuePair<string, int> pair1,
                KeyValuePair<string, int> pair2)
                {
                    return pair1.Value.CompareTo(pair2.Value);
                });
            tmpList.Reverse();
            top10Tags.AddRange(tmpList);
            listView5.Items.Clear();
            foreach (KeyValuePair<string, int> kvp in top10Tags)
            {
                if(kvp.Value > 0)
                {
                    ListViewItem lvi = new ListViewItem(kvp.Key);
                    lvi.SubItems.Add(kvp.Value.ToString());
                    listView5.Items.Add(lvi);
                }
               
            }

            Dictionary<int, double> levelAvgs = new Dictionary<int, double>();
            Dictionary<int, int> levelCounts = new Dictionary<int, int>();
            for (int i = 67; i <= 83; i++)
            {
                int iCount = 0;
                int iSum = 0;

                foreach(TrackedActivity act in _eventHistory)
                {
                    if (act.Type == ACTIVITY_TYPES.HEIST && act.AreaLevel == i)
                    {
                        iCount++;

                        if(act.TotalSeconds < _timeCapHeist)
                        {
                            iSum += act.TotalSeconds;
                        }
                        else
                        {
                            iSum += _timeCapHeist;
                        }
                        
                    }
                }

                levelAvgs.Add(i, (iCount > 0 && iSum > 0) ? (iSum / iCount) : 0);
                levelCounts.Add(i, iCount);
            }

            MethodInvoker mi = delegate
            {
                chart7.Series[0].Points.Clear();
                chart6.Series[0].Points.Clear();

                foreach (KeyValuePair<int, double> kvp in levelAvgs)
                {
                    chart7.Series[0].Points.AddXY(kvp.Key, Math.Round(kvp.Value / 60, 2));
                    chart6.Series[0].Points.AddXY(kvp.Key, levelCounts[kvp.Key]);
                }
            };
            this.Invoke(mi);
        }

        public void RenderMappingDashboard()
        {
            List<KeyValuePair<string,int>> tmpList = new List<KeyValuePair<string, int>>();
            List<KeyValuePair<string, int>> top10 = new List<KeyValuePair<string, int>>();
            Dictionary<string, int> tmpListTags = new Dictionary<string, int>();
            List<KeyValuePair<string, int>> top10Tags = new List<KeyValuePair<string, int>>();

            // MAP AREAS
            foreach (string s in _defaultMappings.MAP_AREAS)
            {
                tmpList.Add(new KeyValuePair<string, int>(s, _numericStats["MapsFinished_" + s]));
            }

            tmpList.Sort(
                delegate (KeyValuePair<string, int> pair1,
                KeyValuePair<string, int> pair2)
                {
                    return pair1.Value.CompareTo(pair2.Value);
                });
            tmpList.Reverse();
            top10.AddRange(tmpList.GetRange(0, 10));
            listView1.Items.Clear();
            foreach(KeyValuePair<string,int> kvp in top10)
            {
                ListViewItem lvi = new ListViewItem(kvp.Key);
                lvi.SubItems.Add(kvp.Value.ToString());
                listView1.Items.Add(lvi);
            }

            // TAG CALC
            tmpList.Clear();
            foreach (ActivityTag tg in Tags)
            {
                tmpListTags.Add(tg.ID, 0);
            }

            foreach(TrackedActivity act in _eventHistory)
            {
                if(act.Type == ACTIVITY_TYPES.MAP)
                {
                    foreach(string s in act.Tags)
                    {
                        if(!String.IsNullOrEmpty(s))
                        {
                            if(tmpListTags.ContainsKey(s))
                            {
                                int iVal = tmpListTags[s];
                                tmpListTags[s]++;
                            }
                        }
                    }
                }
            }

            foreach(KeyValuePair<string,int> kvp in tmpListTags)
            {
                tmpList.Add(new KeyValuePair<string, int>(kvp.Key, kvp.Value));
            }

            tmpList.Sort(
                delegate (KeyValuePair<string, int> pair1,
                KeyValuePair<string, int> pair2)
                {
                    return pair1.Value.CompareTo(pair2.Value);
                });
            tmpList.Reverse();
            top10Tags.AddRange(tmpList);
            listView2.Items.Clear();
            foreach (KeyValuePair<string, int> kvp in top10Tags)
            {
                if(kvp.Value > 0)
                {
                    ListViewItem lvi = new ListViewItem(kvp.Key);
                    lvi.SubItems.Add(kvp.Value.ToString());
                    listView2.Items.Add(lvi);
                }
            }

            double[] tierAverages = new double[]
            {
                0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
            };

            for(int i = 0; i < 16; i++)
            {
                int iSum = 0;
                int iCount= 0;

                foreach(TrackedActivity act in _eventHistory)
                {
                    if(act.Type == ACTIVITY_TYPES.MAP && act.MapTier == (i+1))
                    {
                        if (act.TotalSeconds < _timeCapMap)
                        {
                            iSum += act.TotalSeconds;
                        }
                        else
                        {
                            iSum += _timeCapMap;
                        }
                        
                        iCount++;
                    }
                }

                if(iSum > 0 && iCount > 0)
                {
                    tierAverages[i] = (iSum / iCount);
                }
                
            }

            MethodInvoker mi = delegate
            {
                chart2.Series[0].Points.Clear();
                for (int i = 1; i <= 16; i++)
                {
                    chart2.Series[0].Points.AddXY(i, _numericStats["MapTierFinished_T" + i.ToString()]);
                }

                chart3.Series[0].Points.Clear();
                for(int i = 0; i < tierAverages.Length; i++)
                {
                    chart3.Series[0].Points.AddXY(i+1, Math.Round(tierAverages[i] / 60, 2));
                }
            };
            this.Invoke(mi);
        }

        /// <summary>
        /// Add setting if not exists, otherwise update existing
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="b_log"></param>
        public void AddUpdateAppSettings(string key, string value, bool b_log = false)
        {
            _mySettings.AddOrUpdateSetting(key, value);
            _mySettings.WriteToXml();
        }

        /// <summary>
        /// Refresh the statistics chart
        /// </summary>
        private void RefreshChart()
        {
            chart1.Series[0].Points.Clear();
            switch (comboBox1.SelectedItem.ToString())
            {
                case "Last week":
                    chart1.ChartAreas[0].AxisX.Interval = 1;
                    FillChart(7);
                    break;
                case "Last 2 weeks":
                    chart1.ChartAreas[0].AxisX.Interval = 1;
                    FillChart(14);
                    break;
                case "Last 3 weeks":
                    chart1.ChartAreas[0].AxisX.Interval = 2;
                    FillChart(21);
                    break;
                case "Last month":
                    chart1.ChartAreas[0].AxisX.Interval = 3;
                    FillChart(31);
                    break;
                case "Last 2 month":
                    chart1.ChartAreas[0].AxisX.Interval = 6;
                    FillChart(62);
                    break;
                case "Last 3 month":
                    chart1.ChartAreas[0].AxisX.Interval = 9;
                    FillChart(93);
                    break;
                case "Last year":
                    chart1.ChartAreas[0].AxisX.Interval = 30;
                    FillChart(365);
                    break;
                case "Last 2 years":
                    chart1.ChartAreas[0].AxisX.Interval = 60;
                    FillChart(365 * 2);
                    break;
                case "Last 3 years":
                    chart1.ChartAreas[0].AxisX.Interval = 90;
                    FillChart(365 * 3);
                    break;
                case "All time":
                    chart1.ChartAreas[0].AxisX.Interval = 90;
                    FillChart(365 * 15);
                    break;
            }
        }

        /// <summary>
        /// Fill the sstatistics chart with data
        /// </summary>
        /// <param name="i_days_back"></param>
        private void FillChart(int i_days_back)
        {
            if(_lvmStats.listView.SelectedIndices.Count > 0)
            {
                chart1.Series[0].Points.Clear();
                DateTime dtStart = DateTime.Now.AddDays(i_days_back * -1);
                string sStatName = _lvmStats.listView.SelectedItems[0].Name.Replace("stats_", "");

                DateTime dt1, dt2;
                SqliteDataReader sqlReader;
                SqliteCommand cmd;
                long lTS1, lTS2;

                label38.Text = listViewStats.SelectedItems[0].Text;

                for (int i = 0; i <= i_days_back; i++)
                {
                    dt1 = dtStart.AddDays(i);
                    dt1 = new DateTime(dt1.Year, dt1.Month, dt1.Day);
                    dt2 = new DateTime(dt1.Year, dt1.Month, dt1.Day, 23, 59, 59);
                    lTS1 = ((DateTimeOffset)dt1).ToUnixTimeSeconds();
                    lTS2 = ((DateTimeOffset)dt2).ToUnixTimeSeconds();
                    cmd = _dbConnection.CreateCommand();

                    if (sStatName == "HighestLevel")
                    {
                        cmd.CommandText = "SELECT stat_value FROM tx_stats WHERE stat_name = '" + sStatName + "' AND timestamp BETWEEN " + lTS1 + " AND " + lTS2;
                    }
                    else
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM tx_stats WHERE stat_name = '" + sStatName + "' AND timestamp BETWEEN " + lTS1 + " AND " + lTS2;
                    }

                    sqlReader = cmd.ExecuteReader();
                    while (sqlReader.Read())
                    {
                        chart1.Series[0].Points.AddXY(dt1, sqlReader.GetInt32(0));
                    }
                }
            }
        }

        /// <summary>
        /// Get activity tag object for ID
        /// </summary>
        /// <param name="s_id"></param>
        /// <returns></returns>
        public ActivityTag GetTagByID(string s_id)
        {
            foreach(ActivityTag tag in _tags)
            {
                if (tag.ID == s_id)
                    return tag;
            }
            return null;
        }

        /// <summary>
        /// Create a TraXile backup
        /// </summary>
        /// <param name="s_name"></param>
        private void CreateBackup(string s_name)
        {
            string sBackupDir = _myAppData + @"/backups/" + s_name + @"/" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            System.IO.Directory.CreateDirectory(sBackupDir);
            
            if(System.IO.File.Exists(SettingPoeLogFilePath))
                System.IO.File.Copy(SettingPoeLogFilePath, sBackupDir + @"/Client.txt");
            if(System.IO.File.Exists(_cachePath))
                System.IO.File.Copy(_cachePath, sBackupDir + @"/stats.cache");
            if(System.IO.File.Exists(_dbPath))
                System.IO.File.Copy(_dbPath, sBackupDir + @"/data.db");
            if(System.IO.File.Exists("TraXile.exe.config"))
                System.IO.File.Copy("TraXile.exe.config", sBackupDir + @"/TraXile.exe.config");
        }

        /// <summary>
        /// Fully reset the application
        /// </summary>
        private void DoFullReset()
        {
            // Make logfile empty
            FileStream fs1 = new FileStream(SettingPoeLogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            ResetStats();
            ClearActivityLog();
            _lastHash = 0;
            File.Delete(_cachePath);
        }

        /// <summary>
        /// Open details for tracked activity
        /// </summary>
        /// <param name="ta"></param>
        private void OpenActivityDetails(TrackedActivity ta)
        {
            ActivityDetails ad = new ActivityDetails(ta, this);
            ad.Show();
        }

        /// <summary>
        /// Export Activity log to CSV
        /// </summary>
        /// <param name="sPath"></param>
        public void WriteActivitiesToCSV(string sPath)
        {
            StreamWriter wrt = new StreamWriter(sPath);
            TrackedActivity tm;

            //Write headline
            string sLine = "time;type;area;area_level;stopwatch;death_counter";
            wrt.WriteLine(sLine);

            for(int i = 0; i < _eventHistory.Count; i++)
            {
                tm = _eventHistory[i];
                sLine = "";
                sLine += tm.Started;
                sLine += ";" + tm.Type;
                sLine += ";" + tm.Area;
                sLine += ";" + tm.AreaLevel;
                sLine += ";" + tm.StopWatchValue;
                sLine += ";" + tm.DeathCounter;
                wrt.WriteLine(sLine);
            }
            wrt.Close();
        }

        /// <summary>
        /// Prepare backup restore before app restarts
        /// </summary>
        /// <param name="sPath"></param>
        private void PrepareBackupRestore(string sPath)
        {
            File.Copy(sPath + @"/stats.cache", _cachePath + ".restore");
            File.Copy(sPath + @"/data.db", _dbPath + ".restore");
            File.Copy(sPath + @"/Client.txt", Directory.GetParent(SettingPoeLogFilePath) + @"/_Client.txt.restore");
            _log.Info("Backup restore successfully prepared! Restarting Application");
            Application.Restart();
        }

        /// <summary>
        /// Check if backup restore is prepared and restore
        /// </summary>
        private void DoBackupRestoreIfPrepared()
        {
            if (File.Exists(_cachePath + ".restore"))
            {
                File.Delete(_cachePath);
                File.Move(_cachePath + ".restore", _cachePath);
                _log.Info("BackupRestored -> Source: _stats.cache.restore, Destination: " + _cachePath);
                _restoreMode = true;
            }

            if (File.Exists(_dbPath + ".restore"))
            {
                File.Delete(_dbPath);
                File.Move(_dbPath + ".restore", _dbPath);
                _log.Info("BackupRestored -> Source: _data.db.restore, Destination: data.db");
                _restoreMode = true;
            }

            try
            {
                if (File.Exists(Directory.GetParent(SettingPoeLogFilePath) + @"/_Client.txt.restore"))
                {
                    File.Delete(SettingPoeLogFilePath);
                    File.Move(Directory.GetParent(SettingPoeLogFilePath) + @"/_Client.txt.restore", SettingPoeLogFilePath);
                    _log.Info("BackupRestored -> Source: " + Directory.GetParent(SettingPoeLogFilePath) + @"/_Client.txt.restore" +
                        ", Destination: " + Directory.GetParent(SettingPoeLogFilePath) + @"/_Client.txt");
                    _restoreMode = true;
                }

            }
            catch (Exception ex)
            {
                _log.Error("Could not restore Client.txt, please make sure that Path of Exile is not running.");
                _log.Debug(ex.ToString());
            }
        }

        /// <summary>
        /// Check if a tag with given ID exists
        /// </summary>
        /// <param name="s_id"></param>
        /// <returns></returns>
        private bool CheckTagExists(string s_id)
        {
            foreach (ActivityTag tag in _tags)
            {
                if (tag.ID == s_id)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Add a new tag
        /// </summary>
        /// <param name="tag"></param>
        private void AddTag(ActivityTag tag)
        {
            _tags.Add(tag);

            SqliteCommand cmd = _dbConnection.CreateCommand();
            cmd.CommandText = "INSERT INTO tx_tags (tag_id, tag_display, tag_bgcolor, tag_forecolor, tag_type, tag_show_in_lv) VALUES "
                + "('" + tag.ID + "', '" + tag.DisplayName + "', '" + tag.BackColor.ToArgb() + "', '" + tag.ForeColor.ToArgb() + "', 'custom', " + (tag.ShowInListView ? "1" : "0") + ")";
            cmd.ExecuteNonQuery();

            listViewActLog.Columns.Add(tag.DisplayName);
            ResetMapHistory();
            RenderTagsForConfig(true);
            RenderTagsForTracking(true);
        }

        /// <summary>
        /// Timer for GUI updates
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void timer1_Tick(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(SettingPoeLogFilePath) || !_UpdateCheckDone)
            {
            }
            else
            {
                double dProgress = 0;
                if (!_eventQueueInitizalized)
                {
                    this.Hide();
                    if (_logLinesRead > 0)
                        dProgress = (_logLinesRead / _logLinesTotal) * 100;
                    _loadScreenWindow.progressBar.Value = Convert.ToInt32(dProgress);
                    _loadScreenWindow.progressLabel.Text = "Parsing logfile. This could take a while the first time.";
                    _loadScreenWindow.progressLabel2.Text = Math.Round(dProgress, 2).ToString() + "%";
                }
                else
                {
                    UpdateGUI();
                    this.Opacity = 100;
                }
             
            }
        }


        // =========> EVENT HANDLERS FOR GUI COMPONENTS
        // =======================================================

        private void button1_Click(object sender, EventArgs e)
        {
            textBoxLogView.Text = "";
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            if(_eventQueueInitizalized)
            {
                SaveStatsCache();
            }
        }

        private void MainW_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveLayout();
            Exit();
        }

        private void exitToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            Exit();
        }

        private void infoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutW f1 = new AboutW();
            f1.Show();
        }

        private void pictureBox14_Click_1(object sender, EventArgs e)
        {
            if (_currentActivity != null)
                _currentActivity.Resume();
        }

        private void pictureBox13_Click_1(object sender, EventArgs e)
        {
            if (_currentActivity != null)
                _currentActivity.Pause();
        }

        private void listView2_ColumnClick(object sender, ColumnClickEventArgs e)
        {
        }

        private void listView2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listViewStats.SelectedItems.Count > 0)
                RefreshChart();
        }
       

        private void comboBox1_TextChanged(object sender, EventArgs e)
        {
            button2.Focus();
            if(comboBox1.SelectedIndex > 5)
            {
                if (MessageBox.Show("Selecting more than 3 month could lead to high loading times. Continue?", "Warning", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    if (listViewStats.SelectedItems.Count > 0)
                        RefreshChart();
                }
                else
                {
                    comboBox1.SelectedIndex = 0;
                }
            }
            else
            {
                if (listViewStats.SelectedItems.Count > 0)
                    RefreshChart();
            }
           
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (listViewStats.SelectedItems.Count > 0)
                RefreshChart();
        }

        private void pictureBox17_Click(object sender, EventArgs e)
        {
            if (_currentActivity != null)
            {
                if (_isMapZana && _currentActivity.ZanaMap != null)
                {
                    if (_currentActivity.ZanaMap.ManuallyPaused)
                    {
                        _currentActivity.ZanaMap.Resume();
                    }
                }
                else
                {
                    if (_currentActivity.ManuallyPaused)
                    {
                        _currentActivity.Resume();
                    }
                }
            }
        }

        private void pictureBox18_Click(object sender, EventArgs e)
        {
            if(_currentActivity != null)
            {
                if(_isMapZana && _currentActivity.ZanaMap != null)
                {
                    if(!_currentActivity.ZanaMap.ManuallyPaused)
                    {
                        _currentActivity.ZanaMap.Pause();
                    }
                }
                else
                {
                    if(!_currentActivity.ManuallyPaused)
                    {
                        _currentActivity.Pause();
                    }
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if(listViewActLog.SelectedItems.Count == 1)
            {
                int iIndex = listViewActLog.SelectedIndices[0];
                long lTimestamp = _eventHistory[iIndex].TimeStamp;
                string sType = listViewActLog.Items[iIndex].SubItems[1].Text;
                string sArea = listViewActLog.Items[iIndex].SubItems[2].Text;

                if (MessageBox.Show("Do you really want to delete this Activity? " + Environment.NewLine
                    + Environment.NewLine
                    + "Type: " + sType + Environment.NewLine
                    + "Area: " + sArea + Environment.NewLine
                    + "Time: " + listViewActLog.Items[iIndex].SubItems[0].Text, "Delete?", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    listViewActLog.Items.Remove(listViewActLog.SelectedItems[0]);
                    _eventHistory.RemoveAt(iIndex);
                    DeleteActLogEntry(lTimestamp);
                }
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            ExportActvityList exp = new ExportActvityList(this);
            exp.Show();
        }

       

        private void button5_Click(object sender, EventArgs e)
        {
            if (listViewActLog.SelectedIndices.Count > 0)
            {
                int iIndex = listViewActLog.SelectedIndices[0];
                TrackedActivity act = GetActivityFromListItemName(listViewActLog.Items[iIndex].Name);
                if(act != null)
                    OpenActivityDetails(act);
            }

        }

        private void listView1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (listViewActLog.SelectedIndices.Count > 0)
            {
                int iIndex = listViewActLog.SelectedIndices[0];
                TrackedActivity act = GetActivityFromListItemName(listViewActLog.Items[iIndex].Name);
                if (act != null)
                    OpenActivityDetails(act);
            }
        }

        private void button8_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show("With this action, the statistics will be set to 0 without reloading the log. Continue?", "Warning", MessageBoxButtons.YesNo);
            if (dr == DialogResult.Yes)
            {
                ResetStats();
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show("For this action, the application needs to be restarted. Continue?", "Warning", MessageBoxButtons.YesNo);
            if (dr == DialogResult.Yes)
            {
                ReloadLogFile();
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show("For this action, the application needs to be restarted. Continue?", "Warning", MessageBoxButtons.YesNo);
            if (dr == DialogResult.Yes)
            {
                OpenFileDialog ofd = new OpenFileDialog();
                DialogResult dr2 = ofd.ShowDialog();
                if (dr2 == DialogResult.OK)
                {
                    AddUpdateAppSettings("poe_logfile_path", ofd.FileName, false);
                    ReloadLogFile();
                }
            }
        }

        private void button9_Click(object sender, EventArgs e)
        {
            ResetMapHistory();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            _showGridInActLog = checkBox1.Checked;
            AddUpdateAppSettings("ActivityLogShowGrid", checkBox1.Checked.ToString());
            listViewActLog.GridLines = _showGridInActLog;
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            _showGridInStats = checkBox2.Checked;
            AddUpdateAppSettings("StatsShowGrid", checkBox2.Checked.ToString());
            listViewStats.GridLines = _showGridInStats;
        }

        private void button15_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show("With this action your Path of Exile log will be flushed and all data and statistics in TraXile will be deleted." + Environment.NewLine
                + Environment.NewLine +  "It is recommendet to create a backup first - using the 'Create Backup' function. Do you want to create a backup before reset?", "Warning", MessageBoxButtons.YesNoCancel);

            if (dr == DialogResult.Yes)
                CreateBackup("Auto_Backup");

            if (dr != DialogResult.Cancel)
            {
                DoFullReset();
                MessageBox.Show("Reset successful! The Application will be restarted now.");
                Application.Restart();
            }
        }

        private void button16_Click(object sender, EventArgs e)
        {
            try
            {
                if(textBox6.Text == "")
                {
                    textBox6.Text = "Default";
                }

                if(textBox6.Text.Contains("/") || textBox6.Text.Contains("."))
                {
                    MessageBox.Show("Please do not define a path in the field name");
                }
                else
                {
                    CreateBackup(textBox6.Text);
                    MessageBox.Show("Backup successfully created!");
                }
               
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            
        }

      

        private void button17_Click(object sender, EventArgs e)
        {
            DialogResult dr;

            if(Process.GetProcessesByName("PathOfExileSteam").Length > 0 ||
                Process.GetProcessesByName("PathOfExile").Length > 0)
            {
                MessageBox.Show("It seems that PathOfExile is running at the moment. Please close it first.");
            }
           else
            {
                dr = MessageBox.Show("Do you really want to restore the selected Backup? The Application will be restarted. Please make sure that your PathOfExile Client is not running.", "Warning", MessageBoxButtons.YesNo);
                if (dr == DialogResult.Yes)
                {
                    PrepareBackupRestore(_myAppData + listBox1.SelectedItem.ToString());
                }
            }
        }

        private void panelTags_SizeChanged(object sender, EventArgs e)
        {
            if(_eventQueueInitizalized)
                RenderTagsForTracking(true);
        }

        private void panelEditTags_SizeChanged(object sender, EventArgs e)
        {
            if(_eventQueueInitizalized)
                RenderTagsForConfig(true);
        }

        public bool ValidateTagName(string s_name, bool b_showmessage = false)
        {
            bool bValid = true;
            char[] invalid = new char[] { '=', ',', ';', ' ' };

            if (String.IsNullOrEmpty(s_name))
                bValid = false;

            foreach(char c in invalid)
            {
                if(s_name.Contains(c))
                {
                    bValid = false;
                }
            }

            if(bValid == false && b_showmessage )
            {
                MessageBox.Show("Sorry. this is not a valid tag ID!");
            }

            return bValid;
        }

        private void button10_Click(object sender, EventArgs e)
        {
            if(ValidateTagName(textBox2.Text, true))
            {
                if (!CheckTagExists(textBox2.Text))
                {
                    AddTag(new ActivityTag(textBox2.Text, false) { DisplayName = textBox3.Text });
                    RenderTagsForConfig(true);
                    RenderTagsForTracking(true);
                    textBox2.Clear();
                }
                else
                {
                    MessageBox.Show("Tag '" + textBox2.Text + "' already exists.");
                }
            }
           
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            textBox3.Text = textBox2.Text;
        }

        private void button11_Click(object sender, EventArgs e)
        {
            colorDialog1.ShowDialog();
            label63.BackColor = colorDialog1.Color;
        }

        private void button12_Click(object sender, EventArgs e)
        {
            colorDialog1.ShowDialog();
            label63.ForeColor = colorDialog1.Color;
        }

        public void AddTagAutoCreate(string s_id, TrackedActivity act)
        {
            int iIndex = GetTagIndex(s_id);
            ActivityTag tag;

            if(ValidateTagName(s_id))
            {
                if (iIndex < 0)
                {
                    tag = new ActivityTag(s_id, false);
                    tag.BackColor = Color.White;
                    tag.ForeColor = Color.Black;
                    AddTag(tag);
                }
                else
                {
                    tag = _tags[iIndex];
                }

                if (!tag.IsDefault)
                {
                    act.AddTag(tag.ID);

                    string sTags = "";
                    // Update tags in DB // TODO
                    for (int i = 0; i < act.Tags.Count; i++)
                    {
                        sTags += act.Tags[i];
                        if (i < (act.Tags.Count - 1))
                            sTags += "|";
                    }
                    SqliteCommand cmd = _dbConnection.CreateCommand();
                    cmd.CommandText = "UPDATE tx_activity_log SET act_tags = '" + sTags + "' WHERE timestamp = " + act.TimeStamp.ToString();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void RemoveTagFromActivity(string s_id, TrackedActivity act)
        {
            ActivityTag tag = GetTagByID(s_id);
            if(tag != null && !tag.IsDefault)
            {
                act.RemoveTag(s_id);
                string sTags = "";

                // Update tags in DB // TODO
                for (int i = 0; i < act.Tags.Count; i++)
                {
                    sTags += act.Tags[i];
                    if (i < (act.Tags.Count - 1))
                        sTags += "|";
                    SqliteCommand cmd = _dbConnection.CreateCommand();
                    cmd.CommandText = "UPDATE tx_activity_log SET act_tags = '" + sTags + "' WHERE timestamp = " + act.TimeStamp.ToString();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void UpdateTag(string s_id, string s_display_name, string s_forecolor, string s_backcolor, bool b_show_in_hist)
        {
            int iTagIndex = GetTagIndex(s_id);

            if(iTagIndex >= 0)
            {
                _tags[iTagIndex].DisplayName = s_display_name;
                _tags[iTagIndex].ForeColor = Color.FromArgb(Convert.ToInt32(s_forecolor));
                _tags[iTagIndex].BackColor = Color.FromArgb(Convert.ToInt32(s_backcolor));
                _tags[iTagIndex].ShowInListView = b_show_in_hist;

                SqliteCommand cmd = _dbConnection.CreateCommand();
                cmd.CommandText = "UPDATE tx_tags SET tag_display = '" + s_display_name + "', tag_forecolor = '" + s_forecolor + "', tag_bgcolor = '" + s_backcolor + "', " +
                    "tag_show_in_lv = " + (b_show_in_hist ? "1" : "0") + " WHERE tag_id = '" + s_id + "'";
                cmd.ExecuteNonQuery();
            }

            RenderTagsForConfig(true);
            RenderTagsForTracking(true);
            ResetMapHistory();
        }

        private int GetTagIndex(string s_id)
        {
            for(int i = 0; i < _tags.Count; i++)
            {
                if(_tags[i].ID == s_id)
                {
                    return i;
                }
            }
            return -1;
        }

        private void button13_Click(object sender, EventArgs e)
        {
            UpdateTag(textBox4.Text, textBox5.Text, label63.ForeColor.ToArgb().ToString(), label63.BackColor.ToArgb().ToString(), checkBox4.Checked);
        }

        private void button14_Click(object sender, EventArgs e)
        {
            textBox4.Text = "";
            textBox5.Text = "";
            label63.BackColor = Color.White;
            label63.ForeColor = Color.Black;
            label63.Text = "MyCustomTag";
        }

        private void textBox5_TextChanged(object sender, EventArgs e)
        {
            label63.Text = textBox5.Text;
        }

        private void button19_Click(object sender, EventArgs e)
        {
            DeleteTag(textBox4.Text);
            textBox4.Text = "";
            textBox5.Text = "";
            label63.BackColor = Color.White;
            label63.ForeColor = Color.Black;
            label63.Text = "MyCustomTag";
        }

        private void button18_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show("Do you really want to delete the selected Backup?", "Warning", MessageBoxButtons.YesNo);
            if (dr == DialogResult.Yes)
            {
                DeleteBackup(_myAppData + listBox1.SelectedItem.ToString());
            }
        }

        private void button20_Click(object sender, EventArgs e)
        {
            textBox5.Text = textBox4.Text;
        }

        private void MainW_FormClosed(object sender, FormClosedEventArgs e)
        {

        }

        private void chatCommandsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ChatCommandHelp cmh = new ChatCommandHelp();
            cmh.ShowDialog();
        }

        private void exitToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            this.Exit();
        }

        private void contextMenuStrip1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void chatCommandsToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            ChatCommandHelp cmh = new ChatCommandHelp();
            cmh.ShowDialog();
        }

        private void textBox7_TextChanged(object sender, EventArgs e)
        {
            if(textBox7.Text == String.Empty)
            {
                _lvmStats.Reset();
            }
            _lvmStats.ApplyFullTextFilter(textBox7.Text);
        }

        private void textBox8_TextChanged(object sender, EventArgs e)
        {
          
            
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            SearchHelp sh = new SearchHelp();
            sh.Show();
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            textBox8.Text = "";
            DoSearch();
        }

        private void linkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            textBox7.Text = "";
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void checkForUpdateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CheckForUpdate(true);
        }

        private void infoToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            AboutW ab = new AboutW();
            ab.ShowDialog();
        }

        private void aboutToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            AboutW ab = new AboutW();
            ab.ShowDialog();
        }

        private void DeleteTag(string s_id)
        {
            int iIndex = GetTagIndex(s_id);
            if(iIndex >= 0)
            {
                ActivityTag tag = _tags[iIndex];

                if(tag.IsDefault)
                {
                    MessageBox.Show("Sorry. You cannot delete a default tag!");
                }
                else
                {
                    DialogResult dr = MessageBox.Show("Do you really want to delete the tag '" + s_id + "'?", "Warning", MessageBoxButtons.YesNo);
                    if(dr == DialogResult.Yes)
                    {
                        _tags.RemoveAt(iIndex);
                        SqliteCommand cmd = _dbConnection.CreateCommand();
                        cmd.CommandText = "DELETE FROM tx_tags WHERE tag_id = '" + s_id + "' AND tag_type != 'default'";
                        cmd.ExecuteNonQuery();
                    }
                }
                RenderTagsForConfig(true);
                RenderTagsForTracking(true);
                ResetMapHistory();
            }
        }

        private void DeleteBackup(string s_path)
        {
            Directory.Delete(s_path, true);
            _backups.Remove(listBox1.SelectedItem.ToString());
        }

        private void button21_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show("Your current Client.txt will be renamed and a new one will be created. " + Environment.NewLine
                + "The renamed version can be deleted or backed up afterwards. Continue?", "Warning", MessageBoxButtons.YesNo);
            if(dr == DialogResult.Yes)
            {
                try
                {
                    string sDt = DateTime.Now.ToString("yyyy-MM-dd-H-m-s");
                    string sBaseDir = new FileInfo(SettingPoeLogFilePath).DirectoryName;
                    File.Copy(SettingPoeLogFilePath, sBaseDir + @"\Client." + sDt + ".txt");
                    FileStream fs1 = new FileStream(SettingPoeLogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

                    _lastHash = 0;
                    SaveStatsCache();

                    MessageBox.Show("Client.txt rolled and cleared successful. The Application will be restarted now.");
                    Application.Restart();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
               
            }
        }

        private void chart2_Click(object sender, EventArgs e)
        {

        }

        private void wikiToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(APPINFO.WIKI_URL);
        }

        private void DoSearch()
        {
            if (textBox8.Text == String.Empty)
            {
                _lvmActlog.Reset();
                _lvmActlog.FilterByRange(0, Convert.ToInt32(ReadSetting("actlog.maxitems", "500")));
                
            }
            else if (textBox8.Text.Contains("tags=="))
            {
                List<string> itemNames = new List<string>();
                try
                {
                    string[] sTagFilter = textBox8.Text.Split(new string[] { "==" }, StringSplitOptions.None)[1].Split(',');
                    int iMatched = 0;
                    foreach (TrackedActivity ta in _eventHistory)
                    {
                        iMatched = 0;
                        foreach (string tag in sTagFilter)
                        {
                            if (ta.HasTag(tag))
                            {
                                iMatched++;
                            }
                            else
                            {
                                iMatched = 0;
                                break;
                            }
                        }
                        if (iMatched > 0)
                        {
                            itemNames.Add(ta.TimeStamp + "_" + ta.Area);
                        }
                    }
                    _lvmActlog.FilterByNameList(itemNames);
                }
                catch { }
            }
            else if (textBox8.Text.Contains("tags="))
            {
                List<string> itemNames = new List<string>();
                try
                {
                    string[] sTagFilter = textBox8.Text.Split('=')[1].Split(',');
                    int iMatched = 0;
                    foreach (TrackedActivity ta in _eventHistory)
                    {
                        iMatched = 0;
                        foreach (string tag in sTagFilter)
                        {
                            if (ta.HasTag(tag))
                            {
                                iMatched++;
                            }
                        }
                        if (iMatched > 0)
                        {
                            itemNames.Add(ta.TimeStamp + "_" + ta.Area);
                        }
                    }
                    _lvmActlog.FilterByNameList(itemNames);
                }
                catch { }
            }
            else
            {
                _lvmActlog.ApplyFullTextFilter(textBox8.Text);
            }
        }

        private void button22_Click(object sender, EventArgs e)
        {
            DoSearch();
        }

        private void listViewActLog_ColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e)
        {
            SaveLayout();
        }

        private void label74_Click(object sender, EventArgs e)
        {

        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
        }

        private void comboBox2_SelectionChangeCommitted(object sender, EventArgs e)
        {
            AddUpdateAppSettings("actlog.maxitems", comboBox2.SelectedItem.ToString());
            DoSearch();
        }

        private void label13_Click(object sender, EventArgs e)
        {

        }

        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void label8_Click(object sender, EventArgs e)
        {

        }

        private void labelElderKillCount_Click(object sender, EventArgs e)
        {

        }

        private void label15_Click(object sender, EventArgs e)
        {

        }

        private void labelShaperKillCount_Click(object sender, EventArgs e)
        {

        }

        private void label39_Click(object sender, EventArgs e)
        {

        }

        private void label37_Click(object sender, EventArgs e)
        {

        }

        private void label43_Click(object sender, EventArgs e)
        {

        }

        private void labelMavenTried_Click(object sender, EventArgs e)
        {

        }

        private void labelMavenKilled_Click(object sender, EventArgs e)
        {

        }

        private void label44_Click(object sender, EventArgs e)
        {

        }

        private void tableLayoutPanel10_Paint(object sender, PaintEventArgs e)
        {

        }

        private void tabPage1_Enter(object sender, EventArgs e)
        {
            RenderMappingDashboard();
        }

        private void tabPage1_Validated(object sender, EventArgs e)
        {

        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            _labDashboardHideUnknown = ((CheckBox)sender).Checked;
            RenderLabDashboard();
        }

        private void label90_Click(object sender, EventArgs e)
        {

        }

        private void button23_Click(object sender, EventArgs e)
        {
            try
            {
                int iMap = Convert.ToInt32(textBox9.Text);
                int iHeist = Convert.ToInt32(textBox11.Text);
                int iLab = Convert.ToInt32(textBox10.Text);

                if(iMap > 0 && iHeist > 0 && iLab > 0)
                {
                    _timeCapMap = iMap;
                    _timeCapLab = iLab;
                    _timeCapHeist = iHeist;

                    AddUpdateAppSettings("TimeCapMap", _timeCapMap.ToString());
                    AddUpdateAppSettings("TimeCapLab", _timeCapLab.ToString());
                    AddUpdateAppSettings("TimeCapHeist", _timeCapHeist.ToString());

                    RenderGlobalDashboard();
                    RenderMappingDashboard();
                    RenderHeistDashboard();
                    RenderLabDashboard();
                }
                else
                {
                    MessageBox.Show("Time cap values must be greater than 0");
                }
               
            }
            catch(Exception ex)
            {
                _log.Error(ex.ToString());
                MessageBox.Show("Invalid format for time cap. Only integers are supported");
            }
        }

        private void pictureBox19_Click(object sender, EventArgs e)
        {
            if (_currentActivity != null)
            {
               FinishActivity(_currentActivity, null, _currentActivity.Type, DateTime.Now);
            }
               
        }

        private void pictureBox15_Click_1(object sender, EventArgs e)
        {
            if (_currentActivity != null)
            {
                FinishActivity(_currentActivity, null, _currentActivity.Type, DateTime.Now);
            }
        }
       
    }
}