using Daims.SysCore;
using Daims.SysCore.Common;
using Daims.SysCore.Components;
using Daims.SysCore.Constant;
using Daims.SysCore.CustomMessageBox;
using Daims.SysCore.Database;
using Daims.SysCore.Enums;
using Daims.SysCore.ParameterDataList;
using Daims.SysCore.SimulationBuffer;
using Daims.SysCore.SysPerformanceInfo;
using Daims.SysCore.ThroughputMonitor;
using log4net;
using nDA.AnalysisWindow.TestUnit3;
using nDA.AnalysisWindow.View;
using nDA.Commons;
using nDA.DataManager.View;
using nDA.DataSourceManager.View;
using nDA.Displaytools.View;
using nDA.LocalPlayer;
using nDA.nDAMain.Model;
using nDA.nDAMain.TestUnit1;
using nDA.Properties;
using nDA.Recorder;
using nDA.Recorder.View;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Telerik.WinControls;
using Telerik.WinControls.Primitives;
using Telerik.WinControls.UI;

namespace nDA.nDAMain.View
{
    public partial class nDAMainView : ShapedForm
    {
        #region [ 멤버 상수 ]
        private const int minToSec = 60;
        private readonly string ID_ADMIN = "Admin";

        /// <summary>
        /// UDP 수신 상태 확인 라벨 업데이트 간격 상수
        /// </summary>
        private const int TM_LABEL_MAX = 3000;

        /// <summary>
        /// 플레이시간 라벨 업데이트 간격 상수
        /// </summary>
        private const int TM_CLOCK_MAX = 1000;

        /// <summary>
        /// FHD 가로 해상도, 모든 해상도 계산의 기준 해상도
        /// </summary>
        private readonly int RESOLUTION_WITH1920 = 1920;

        /// <summary>
        /// 해상도 관계없이 메인 UI의 기본 마진 사이즈
        /// </summary>
        private readonly int BASE_MARGIN = 5;

        /// <summary>
        /// 해상도에 따라 더해지는 마진 비율
        /// </summary>
        private readonly float BASE_RATIO = 0.3f;

        #endregion

        #region [ 클래스 멤버 변수 ]
        /// <summary>
        /// 메인의 재생 모드에 따라 분석창의 재생 모드를 변경하는 이벤트
        /// </summary>
        public Action OnChangedMainPlayStatus;

        private GlobalKeyboardEventListener keyboardEventListener;
        public System.Timers.Timer timeoutTimer;
        internal Controller.nDAMainController MainController;
        private bool isDebug;
        /// <summary>
        /// 클래스내에서 문자열 포멧팅에 사용되는 전역 문자열 버퍼
        /// </summary>
        private readonly StringBuilder strFormatter;

        /// <summary>
        /// 클래스내에서 UDP 속도를 표시하는곳에 사용
        /// </summary>
        private readonly StringBuilder strFpsFormatter;
        /// <summary>
        /// 비활성화 상태의 컨트롤들의 툴팁을 만들기 위한 RadToolTip
        /// </summary>
        private RadToolTip _toolTip = new RadToolTip();

        internal ObjectBrowserView ObjectBrowserForm;
        internal DataManagerView DataToolView;
        internal SettingView settingView;
        internal RecorderView recView;
        internal DataSourceSelectView dataSourceSelectView;
        internal SystemMonitoringView SystemMonitoringForm;
        internal UserManagerView UserManagerForm;
        private AnalysisWindowManagerView analysisWindowManager;
        //public MainPlaybackToolBar mainPlaybackToolBar;
        private StripChartTagView tagView;

        /// <summary>
        /// UDPServerClint 가 Exception에 의해 연결이 끊어진 경우인지를 나타내는 플래그
        /// true: 연결 끊어짐, false: 정상 가동 중
        /// </summary>
        internal bool UdpServerLostFlag;

        /// <summary>
        /// UDP 수신 상태 업데이트용 타이머
        /// </summary>
        private System.Windows.Forms.Timer UdpDataReceiveStateTimer;

        /// <summary>
        /// 플레이시간 업데이트용 타이머
        /// </summary>
        private System.Windows.Forms.Timer TimerClockUpdater;

        /// <summary>
        /// TagViewStartPosition이 지정되었는가 확인하기 위해 시작 위치를 저장
        /// </summary>
        private Point TagViewStartPosition;

        /// <summary>
        /// 메인 UI 변경 이벤트시 툴팁 표시용 변수
        /// </summary>
        private ToolTip tpMainStatus = new ToolTip();

        /// <summary>
        /// 메인이 최대화시 가로 크기를 저장하는 변수
        /// </summary>
        private int originalWith = 0;

        #endregion

        #region [ 클래스 초기화 및 종료 ]
        public nDAMainView()
        {
            InitializeComponent();

            strFormatter = new StringBuilder(ConstantVal.MaxStrBuffer);
            strFpsFormatter = new StringBuilder(ConstantVal.MaxStrBuffer);

            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            tlMain.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tlMain, true, null);
            tlTimebar.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tlTimebar, true, null);
            tlSwitcher.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tlSwitcher, true, null);

            Load            += nDAMainView_Load;
            Shown           += nDAMainView_Shown;
            FormClosing     += nDAMainView_FormClosing;
            FormClosed      += nDAMainView_FormClosed;

            UpdateDataSourceLabel();

            var version = Assembly.GetExecutingAssembly().GetName().Version;

            if (SharedData.GetInstance().DataSourceType != PlayDataSource.OFFLINE)
            {
                //관리자인 경우
                if (SharedData.GetInstance().CurrentUser.RoleName == ID_ADMIN)
                {
                    cmdButtonUser.Enabled = true;
                    cmdButtonState.Enabled = true;
                }
                else //일반 사용자인 경우
                {
                    //시스템 모니터 확인 가능하도록 수정
                    cmdButtonState.Enabled = true;
                }
            }
            else
            {
                SharedData.GetInstance().CurrentUser = new Daims.SysCore.User.UserInfo()
                {
                    RoleName = ID_ADMIN
                };

                cmdButtonState.Enabled = false;
                cmdButtonUser.Enabled = false;
            }

            Text = string.Format(CultureInfo.CreateSpecificCulture("en-US"), " nDA SW v.{0}.{1}", version.Major, version.Minor);

            //load시에 설정하도록 수정
            //SysPerformInfo.GetInstance().SetType((Performancetype)Enum.Parse(typeof(Performancetype), Settings.Default.PerformanceValue));
            CommandBarEventsReg();

            // 툴팁 세팅
            _toolTip.InitialDelay = 800;
            _toolTip.AutoPopDelay = 10000;

            //UDP 상태 표시기 타이머 초기화 및 시작
            InitUdpCheckTimer();

            //플레이시간 업데이트용 타이머 초기화 및 시작
            InitClockTimer();

            OnComponentsControlEvents();

            //시스템 모니터의 프로세스 정보를 알아오는 시간의 부팅 후 처음 프로그램을 실행시 오래 걸리기때문에
            //시간을 줄이기 위해 프로그램 시작 후, 한번 백그라운드로 호출해 준다. 
            //캐싱과 비슷한 역할을 하도록하는 방법이다. 
            Task.Run(() => SystemMonitoringLib.GetInst().GetProcessCPUInfo());
        }

        private void OnComponentsControlEvents()
        {
            MetaData.CommonEvent mouseEvent = new MetaData.CommonEvent();

            mouseEvent.FormCommonEventReg(this);
        }

        /// <summary>
        /// 메인 UI가 화면에 로드된 후 각 상태마다 수행하는 로직을 정의
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void nDAMainView_Shown(object sender, EventArgs e)
        {
            SetUIChangeByPlayMode(SharedData.GetInstance().DataSourceType);
        }

        private void nDAMainView_Load(object sender, EventArgs e)
        {
            SetMainUISize();

            //서버 IP 표시부에 네트워크 연결관련 상태를 표시하기 위해. DEBUG 에서만 사용.
            MainController = new Controller.nDAMainController(this);

#if (DEBUG)
            MainController.TcpEventUpdate.Update += OnTcpConnectionStatusUpdate;
#endif
            //자동 로그아웃이 설정.
            LoadAutoInfo();

            //AutoSaveInfo 생성 후 Performance를 갱신.
            if (SharedData.GetInstance().OffSimInfo.AutoSaveInfo.systemPerformance != null)
            {
                SysPerformInfo.GetInstance().SetType((Performancetype)Enum.Parse(typeof(Performancetype), SharedData.GetInstance().OffSimInfo.AutoSaveInfo.systemPerformance));
            }
            else
            {
                SysPerformInfo.GetInstance().SetType((Performancetype)Enum.Parse(typeof(Performancetype), Settings.Default.PerformanceValue));
            }       
            
            keyboardEventListener = new GlobalKeyboardEventListener();
            keyboardEventListener.KeyDown += KeyDownEventListener;
            GlobalMouseEventListener.MouseAction += MouseEventListener;
            UpdateDataSourceLabel();

            //현재 버튼의 상태를 초기화 한다. 
            OnPlayStopEnable(false);

            tagView = StripChartTagView.GetInstance();
            tagView.Show();
            tagView.Visible = false;
            TStripBtn_MarkViewer_EnabledChanged(null, null);

            //OFF Line에서 제어화면 부분 비활성화.
            if (SharedData.GetInstance().DataSourceType == PlayDataSource.OFFLINE)
            {
                DataSourceButtonUIChange(false);
                DataToolButtonUIChange(true);
                RecViewButtonUIChange(true);

                if (SharedData.GetInstance().OffSimInfo.SimulInfo != null)
                {
                    SharedData.GetInstance().OffSimInfo.SimulInfo = SharedData.GetInstance().OffSimInfo.SimulInfo;
                }

                if (SharedData.GetInstance().OffSimInfo.AwTemplateList?.Count > 0)
                {
                    cmdButtonOpen.Enabled = true;
                }
                else
                {
                    cmdButtonOpen.Enabled = false;
                }

                //플레이백바 시간 설정
                TimeSpan ts = SharedData.GetInstance().OffSimInfo.SimulInfo.GetEndTime() - SharedData.GetInstance().OffSimInfo.SimulInfo.GetStartTime();
                SetPlayTime(SharedData.GetInstance().OffSimInfo.SimulInfo.GetStartTime(), ts, 0);
                tbUpdateFlag = true;

                //TagEvent View에 리스트 넣기.
                var tagList = SharedData.GetInstance().OffSimInfo.TagEventList;
                if (tagList != null && tagList.Count > 0)
                {
                    foreach (var item in tagList)
                    {
                        if (item.Type == TagType.TIMETAG)
                        {
                            tagView.AddTag(item.Type, item.StartTime, true, item.Comment);
                            tagView.AddTag(item.Type, item.EndTime, false, item.Comment);
                        }
                        else
                        {
                            tagView.AddTag(item.Type, item.StartTime, false, item.Comment);
                        }
                    }
                }
            }
            else
            {
                timeoutTimeValue = (double)(SharedData.GetInstance().OffSimInfo.AutoSaveInfo.LogoutTime * minToSec);
                DataToolButtonUIChange(false);
                RecViewButtonUIChange(false);
            }

            //UDP 수신속도 표시 라벨을 기본 값으로 초기화 시킨다. 
            Utils.UpdateMsLabel(labUDPState, "UDP [ 0 ] Hz", MetaData.ConstantValues.CMAINUDP_TXTINACTIVE);

            PlayButtonEventsReg();
            CmdButtonEventsReg();
            ButtonEventsReg();
            LabelEventsReg();
            PlackbackBarEventsReg();

            CDebug.WriteTrace("\r\n");
            CDebug.WriteTrace("+--------------------------------------------------------------+");
            CDebug.WriteTrace($"[ All Right Reserve Daims Co. Ltd. ]", ConsoleColor.Yellow);
            CDebug.WriteTrace($"[ Last Build 2021/10/1, mackenzie@daims.co.kr ]", ConsoleColor.Yellow);
            CDebug.WriteTrace($"[ Application : nPrism Data Analyzer {Utils.GetAppVersionDisplay()} ]", ConsoleColor.Yellow);
            CDebug.WriteTrace("+--------------------------------------------------------------+");
            CDebug.WriteTrace("\r\n");

        }

        /// <summary>
        /// 메인 UI의 크기를 설정한다. 
        /// </summary>
        private void SetMainUISize()
        {
            int scWidth = Screen.PrimaryScreen.WorkingArea.Width;

            //모니터의 해상도에 따라 계산된 메인 UI의 마진값, 추후 전체 마진으로 설정해야함.
            MetaData.ConstantValues.FORM_MARGIN = GetFormMarginByResolution(scWidth);

            // MainView의 높이를 스크린 사이즈로 맞춘다.
            Width       = scWidth - (MetaData.ConstantValues.FORM_MARGIN * 2);

            //메인창이 이벤트에 의해 변경되어야하므로 아래의 프로퍼티 설정 불가
            //MinimumSize = new Size(scWidth - (ConstantVal.FORM_MARGIN * 2), ConstantVal.FORM_HEIGHT_DEFAULT);
            //MaximumSize = new Size(scWidth - (ConstantVal.FORM_MARGIN * 2), ConstantVal.FORM_HEIGHT_DEFAULT);

            Location = new Point(MetaData.ConstantValues.FORM_MARGIN, MetaData.ConstantValues.FORM_MARGIN);
        }

        /// <summary>
        /// 모니터 해상도를 기준으로 적정 마진값을 계산
        /// FHD(1920x1080)을 기준으로 FHD 이하의 모니터는 마진을 "BASE_MARGIN"값으로 상수값 고정
        /// FHD 이상의 모니터는 가로 해상도를 기준으로 "BASE_RATIO" 비율 만큼을 더하여 마진을 계산
        /// HD(1280): 5 pixel, FHD(1920): 5 pixel, QHD(2560): 13 pixel, FHD(3480): 17 pixel
        /// 이 밖에 비표준 해상도의 경우, 가로 크기를 베이스로 자동 계산됨.
        /// </summary>
        /// <param name="scWidth"></param>
        /// <returns></returns>
        private int GetFormMarginByResolution(int scWidth)
        {
            int opResult = BASE_MARGIN;

            if(scWidth <= RESOLUTION_WITH1920)
            {
                return opResult;
            } else
            {
                opResult = BASE_MARGIN + (int)Math.Ceiling((scWidth * BASE_RATIO) / 100);
            }

            return opResult;
        }

        /// <summary>
        /// 메인 UI 변경시 툴팁 표시용 함수
        /// </summary>
        /// <param name="text"></param>
        private void ShowTootTip(string text)
        {
            Point pLocation = PointToClient(new Point(MousePosition.X, MousePosition.Y));
            pLocation.Y += 10;

            tpMainStatus.IsBalloon = false;
            tpMainStatus.BackColor = Color.Yellow;

            tpMainStatus.Show($"{text}", this, pLocation, 2000);
        }

        /// <summary>
        /// 플레이 모드에 따라 플레이백바와 주변 UI를 변경하는 로직
        /// </summary>
        /// <param name="pMode"></param>
        private void SetUIChangeByPlayMode(PlayDataSource pMode)
        {
            switch (pMode)
            {
                case PlayDataSource.REALTIME:
                    tlTimebar.Visible = false;
                    tlMain.ColumnStyles[7].Width = 0;
                    break;

                case PlayDataSource.OFFLINE:
                case PlayDataSource.REPLAY:
                    tlTimebar.Visible = true;
                    tlMain.ColumnStyles[7].Width = 351;
                    break;
            }
        }

        #region [ 리사이즈 가능한 ShapedForm이 리사이즈시 번쩍임 방지 로직 ]

        const int WS_CLIPCHILDREN = 0x02000000; //자식 컨트롤이있는 영역에서 UC가 페인팅되지 않도록하는
        private int originalExStyle = -1;
        private readonly bool enableFormLevelDoubleBuffering = true;

        protected override CreateParams CreateParams
        {
            get
            {
                if (originalExStyle == -1)
                    originalExStyle = base.CreateParams.ExStyle;

                CreateParams cp = base.CreateParams;
                if (enableFormLevelDoubleBuffering)
                    cp.ExStyle |= WS_CLIPCHILDREN;
                else
                    cp.ExStyle = originalExStyle;

                return cp;
            }
        }

        #endregion

        /// <summary>
        /// 비정상적인 HQS 시간이 수신되는 경우 사용자에게 이를 알리고, Play 상태를 Stop 시킨다.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="value"></param>
        private void InvalidHqsTimeNotifyEvent_Update(object sender, object value)
        {
            if (!MessageDlg.GetInstance().Visible)
            {
                MessageDlg.GetInstance().SetWarning(global::nDA.MetaData.Properties.Resources.TitleWarning, global::nDA.MetaData.Properties.Resources.InfoInvalidTime, BoxType.CLOSE);
                MessageDlg.GetInstance().ShowDialog();
            }
        }

        /// <summary>
        /// 넷체커의 네트워크 연경 오류가 감지됐을때 호출되서, UI만 설정을 변경한다. 
        /// </summary>
        internal void OnUpdateNetworkDisconnect()
        {
            DataToolButtonUIChange(false);
            RecViewButtonUIChange(false);

            OnPlayStopButtonChange(MethodBase.GetCurrentMethod().Name, PlayMode.STOP);
            OnPlayStopEnable(false);
        }

        private void CommandBarEventsReg()
        {
            //로고 패널 이벤트 등록 - TopMost 설정/해제
            panLogo.MouseClick      += PanLogo_Click;

            //시작/중지 버튼 컨트롤
            cmdButtonPlayStop.Click += BtnPlayStop_Click;
            //새 분석기 생성
            cmdButtonNew.Click      += CommandBtnNew_Click;
            //분석기 열기
            cmdButtonOpen.Click     += BtnOpenAnalysisWindow_Click;
            //차트 브라우저 열기
            cmdButtonCharts.Click   += BtnDisplayTool_Click;
            //데이터 브라우저 열기
            cmdButtonData.Click     += BtnDataTool_Click;

            //소스 변경
            labSourceTime.Click += BtnDataSource_Click;

            cmdButtonRec.Click += CmdButtonOpenRecorder_Click;

            //태그 목록 열기
            cmdButtonMarker.Click   += MarkerView_Click;
            cmdButtonOptions.Click  += RBtn_Setting_Click;
            cmdButtonUser.Click     += RBtnUserManager_Click;
            cmdButtonState.Click    += RBtnSystemMonitor_Click;

            cmdbuttonInfo.Click     += CmdbuttonInfo_Click;
            //로그아웃 실행
            cmdButtonLogout.Click   += BtnLogout_Click;
        }

        private void PlackbackBarEventsReg()
        {
            PlaybackBar.MouseDown += PlaybackBar_MouseDown;
            PlaybackBar.MouseUp += PlaybackBar_MouseUp;
        }

        /// <summary>
        /// 스소전환 라벨 마우스 이벤트 UI 변경 등록
        /// </summary>
        private void PlayButtonEventsReg()
        {
            cmdButtonPlayStop.MouseEnter    += PlayButton_MouseEnter;
            cmdButtonPlayStop.MouseLeave    += PlayButton_MouseLeave;
            cmdButtonPlayStop.MouseDown     += PlayButton_MouseDown;
            cmdButtonPlayStop.MouseUp       += PlayButton_MouseUp;
        }

        /// <summary>
        /// 버튼의 이벤트에 따라 컬러 변경을 위한 공통 로직
        /// </summary>
        private void ButtonEventsReg()
        {
            foreach (var control in tlSwitcher.Controls)
            {
                if (control is RadButton)
                {
                    var button = control as RadButton;
                    button.MouseEnter   += Button_MouseEnter;
                    button.MouseLeave   += Button_MouseLeave;
                    button.MouseDown    += Button_MouseDown;
                    button.MouseMove    += Button_MouseMove;
                    button.MouseUp      += Button_MouseUp;

                    button.Enabled = false;
                    button.ButtonElement.BorderElement.BoxStyle = BorderBoxStyle.SingleBorder;
                }
            }
        }

        /// <summary>
        /// 스소전환 라벨 마우스 이벤트 UI 변경 등록
        /// </summary>
        private void LabelEventsReg()
        {
            labSourceTime.MouseEnter    += SourceLabel_MouseEnter;
            labSourceTime.MouseLeave    += SourceLabel_MouseLeave;
            labSourceTime.MouseDown     += SourceLabel_MouseDown;
            labSourceTime.MouseUp       += SourceLabel_MouseUp;
        }

        private void CmdButtonEventsReg()
        {
            foreach (RadItem control in cmdBarMainStrip.Items)
            {
                if (control is CommandBarButton)
                {
                    var button = control as CommandBarButton;

                    button.BorderColor      = Color.Transparent;
                    button.BorderInnerColor = Color.Transparent;

                    button.MouseEnter   += CmdButton_MouseEnter;
                    button.MouseLeave   += CmdButton_MouseLeave;
                    button.MouseDown    += CmdButton_MouseDown;
                    button.MouseUp      += CmdButton_MouseUp;
                    
                    CmdButton_EnabledChanged(button, null);
                    button.EnabledChanged += CmdButton_EnabledChanged;
                }

                if (control is CommandBarToggleButton)
                {
                    var button = control as CommandBarToggleButton;

                    button.BorderColor = Color.Transparent;
                    button.BorderInnerColor = Color.Transparent;

                    button.MouseEnter   += CmdButton_MouseEnter;
                    button.MouseLeave   += CmdButton_MouseLeave;
                    button.MouseDown    += CmdButton_MouseDown;
                    button.MouseUp      += CmdButton_MouseUp;

                    CmdButton_EnabledChanged(button, null);
                    button.EnabledChanged += CmdButton_EnabledChanged;
                }
            }

            foreach (RadItem control in cmdBarLogoutStrip.Items)
            {
                if (control is CommandBarButton)
                {
                    var button = control as CommandBarButton;

                    button.BorderColor      = Color.Transparent;
                    button.BorderInnerColor = Color.Transparent;

                    button.MouseEnter   += CmdButton_MouseEnter;
                    button.MouseLeave   += CmdButton_MouseLeave;
                    button.MouseDown    += CmdButton_MouseDown;
                    button.MouseUp      += CmdButton_MouseUp;

                    CmdButton_EnabledChanged(button, null);
                    button.EnabledChanged += CmdButton_EnabledChanged;
                }
            }
        }

        #region [ 플레이 버튼 이벤트별 UI ]

        private void PlayButton_MouseEnter(object sender, EventArgs e)
        {
            var target = sender as RadButton;
            cmdButtonPlayStop.ButtonElement.TextElement.ForeColor = MetaData.ConstantValues.CMAINSRC_TXTENTER;
        }

        private void PlayButton_MouseLeave(object sender, EventArgs e)
        {
            var target = sender as RadButton;
            cmdButtonPlayStop.ButtonElement.TextElement.ForeColor = MetaData.ConstantValues.CMAINPLAY_TXTNORMAL;
        }

        private void PlayButton_MouseDown(object sender, MouseEventArgs e)
        {
            var target = sender as RadButton;
            if (e.Button == MouseButtons.Left)
            {
                Rectangle rect = new Rectangle(target.Location, target.Size);
                int x = target.Location.X + e.X;
                int y = target.Location.Y + e.Y;
                Point p = new Point(x, y);
                if (rect.Contains(p))
                {
                    cmdButtonPlayStop.ButtonElement.TextElement.ForeColor = MetaData.ConstantValues.CMAINSRC_TXTDOWN;
                }
            }
        }

        private void PlayButton_MouseUp(object sender, MouseEventArgs e)
        {
            var target = sender as RadButton;
            if (e.Button == MouseButtons.Left)
            {
                Rectangle rect = new Rectangle(target.Location, target.Size);
                int x = target.Location.X + e.X;
                int y = target.Location.Y + e.Y;
                Point p = new Point(x, y);

                if (rect.Contains(p))
                {
                    cmdButtonPlayStop.ButtonElement.TextElement.ForeColor = MetaData.ConstantValues.CMAINPLAY_TXTNORMAL;
                }
            }
        }

        #endregion

        #region [ 커맨드바 버튼 이벤트별 UI ]
        private void CmdButton_MouseEnter(object sender, EventArgs e)
        {
            if(sender.GetType() == typeof(CommandBarButton))
            {
                var target = sender as CommandBarButton;
                target.BackColor    = MetaData.ConstantValues.CCMD_FILLENTER;
                target.BorderColor  = MetaData.ConstantValues.CCMD_BORDERENTER;

                //target.Opacity = 0.1;
            }
            else if (sender.GetType() == typeof(CommandBarToggleButton)) 
            {
                var target = sender as CommandBarToggleButton;
                target.BackColor    = MetaData.ConstantValues.CCMD_FILLENTER;
                target.BorderColor  = MetaData.ConstantValues.CCMD_BORDERENTER;
            }
        }

        private void CmdButton_MouseLeave(object sender, EventArgs e)
        {
            if (sender.GetType() == typeof(CommandBarButton))
            {
                var target = sender as CommandBarButton;
                target.BackColor = Color.Transparent; // MetaData.CommonColorValues.CCMD_FILLNORMAL;
                target.BorderColor = Color.Transparent; // MetaData.CommonColorValues.CCMD_FILLNORMAL;
            }
            else if (sender.GetType() == typeof(CommandBarToggleButton))
            {
                var target = sender as CommandBarToggleButton;
                target.BackColor    = Color.Transparent; // MetaData.CommonColorValues.CCMD_FILLNORMAL;
                target.BorderColor  = Color.Transparent; // MetaData.CommonColorValues.CCMD_FILLNORMAL;
            }
        }

        private void CmdButton_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (sender.GetType() == typeof(CommandBarButton))
                {
                    var target = sender as CommandBarButton;
                    target.BackColor    = MetaData.ConstantValues.CCMD_FILLDOWN;
                    target.BorderColor  = MetaData.ConstantValues.CCMD_BORDERDOWN;
                }
                else if (sender.GetType() == typeof(CommandBarToggleButton))
                {
                    var target = sender as CommandBarToggleButton;
                    target.BackColor    = MetaData.ConstantValues.CCMD_FILLDOWN;
                    target.BorderColor  = MetaData.ConstantValues.CCMD_BORDERDOWN;
                }
            }
        }

        private void CmdButton_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (sender.GetType() == typeof(CommandBarButton))
                {
                    var target = sender as CommandBarButton;
                    target.BackColor = Color.Transparent; // MetaData.CommonColorValues.CCMD_FILLNORMAL;
                    target.BorderColor = Color.Transparent; // MetaData.CommonColorValues.CCMD_FILLNORMAL;
                }
                else if (sender.GetType() == typeof(CommandBarToggleButton))
                {
                    var target = sender as CommandBarToggleButton;
                    target.BackColor = Color.Transparent; // MetaData.CommonColorValues.CCMD_FILLNORMAL;
                    target.BorderColor = Color.Transparent; // MetaData.CommonColorValues.CCMD_FILLNORMAL;
                }
            }
        }

        private void CmdButton_EnabledChanged(object sender, EventArgs e)
        {
            if (sender is CommandBarButton button)
            {
                button.ImageOpacity = button.Enabled ? 1.0 : 0.4;
                button.ForeColor = button.Enabled ? MetaData.ConstantValues.CMAINPLAY_TXTNORMAL : MetaData.ConstantValues.CBTN_TXTDISABLE;
            }
            else if (sender is CommandBarToggleButton tButton)
            {
                tButton.ImageOpacity = tButton.Enabled ? 1.0 : 0.4;
                tButton.ForeColor = tButton.Enabled ? MetaData.ConstantValues.CMAINPLAY_TXTNORMAL : MetaData.ConstantValues.CBTN_TXTDISABLE;
            }
        }
        #endregion

        #region [ 분석기 전환 버튼 이벤트별 UI ]

        private void Button_MouseEnter(object sender, EventArgs e)
        {
            var target = sender as RadButton;
            target.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLNORMAL;
        }

        private void Button_MouseLeave(object sender, EventArgs e)
        {
            var target = sender as RadButton;
            target.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLNORMAL;
        }

        private void Button_MouseDown(object sender, MouseEventArgs e)
        {
            var target = sender as RadButton;
            if (e.Button == MouseButtons.Left)
            {
                Rectangle rect = new Rectangle(target.Location, target.Size);
                int x = target.Location.X + e.X;
                int y = target.Location.Y + e.Y;
                Point p = new Point(x, y);
                if (rect.Contains(p))
                {
                    target.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLNORMAL;
                }
            }
        }

        private void Button_MouseMove(object sender, MouseEventArgs e)
        {
            var target = sender as RadButton;
            if (e.Button == MouseButtons.Left)
            {
                Rectangle rect = new Rectangle(target.Location, target.Size);
                int x = target.Location.X + e.X;
                int y = target.Location.Y + e.Y;
                Point p = new Point(x, y);
                if (rect.Contains(p))
                {
                    //target.ButtonElement.BorderElement.ForeColor = COLORFILL_SWITCHER_ENTER;
                    target.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLNORMAL;
                }
                else
                {
                    target.ButtonElement.ButtonFillElement.ResetValue(FillPrimitive.BackColorProperty, ValueResetFlags.Local);
                }
            }
        }

        private void Button_MouseUp(object sender, MouseEventArgs e)
        {
            var target = sender as RadButton;
            if (e.Button == MouseButtons.Left)
            {
                Rectangle rect = new Rectangle(target.Location, target.Size);
                int x = target.Location.X + e.X;
                int y = target.Location.Y + e.Y;
                Point p = new Point(x, y);

                if (rect.Contains(p))
                {
                    target.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLNORMAL;
                }
            }
        }

        #endregion

        #region [ 데이터 소스변경 라벨 이벤트별 UI ]


        private void SourceLabel_MouseEnter(object sender, EventArgs e)
        {
            var target = sender as RadLabel;
            target.BringToFront();
            target.ForeColor = MetaData.ConstantValues.CMAINSRC_TXTENTER;
        }

        private void SourceLabel_MouseLeave(object sender, EventArgs e)
        {
            var target = sender as RadLabel;
            
            target.ForeColor = MetaData.ConstantValues.CMAINSRC_TXTNORMAL;
        }

        private void SourceLabel_MouseDown(object sender, MouseEventArgs e)
        {
            var target = sender as RadLabel;
            if (e.Button == MouseButtons.Left)
            {
                Rectangle rect = new Rectangle(target.Location, target.Size);
                int x = target.Location.X + e.X;
                int y = target.Location.Y + e.Y;
                Point p = new Point(x, y);
                if (rect.Contains(p))
                {
                    target.ForeColor = MetaData.ConstantValues.CMAINSRC_TXTDOWN;
                }
            }
        }

        private void SourceLabel_MouseUp(object sender, MouseEventArgs e)
        {
            var target = sender as RadLabel;
            if (e.Button == MouseButtons.Left)
            {
                Rectangle rect = new Rectangle(target.Location, target.Size);
                int x = target.Location.X + e.X;
                int y = target.Location.Y + e.Y;
                Point p = new Point(x, y);

                if (rect.Contains(p))
                {
                    target.ForeColor = MetaData.ConstantValues.CMAINSRC_TXTNORMAL;
                }
            }
        }

        #endregion

        PopupLoginInfo InfoDlg = null;

        /// <summary>
        /// 나중에 적절한 위치로 이동시켜야한다. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CmdbuttonInfo_Click(object sender, EventArgs e)
        {
            InfoDlg = new PopupLoginInfo();
            InfoDlg.FormClosing += PopupDlg_FormClosing;
            InfoDlg.FormClosed  += PopupCreateControls_FormClosed;

            InfoDlg.SetInfo(Utils.GetAppVersionDisplay());

            //다이알로그 생성 포지션 설정 - 커서 포지션 백업            
            //InfoDlg.Location = Cursor.Position;
            //InfoDlg.Location = new Point(Cursor.Position.X - InfoDlg.Size.Width, Cursor.Position.Y);

            var target = (sender as CommandBarButton);
            InfoDlg.Location = new Point(Location.Y + Width - InfoDlg.Width, Location.Y + Height + 5);

            InfoDlg.Show();
            
        }

        private void PopupDlg_FormClosing(object sender, FormClosingEventArgs e)
        {

        }

        private void PopupCreateControls_FormClosed(object sender, FormClosedEventArgs e)
        {
            InfoDlg.Dispose();
            InfoDlg = null;
        }

        
        /// <summary>
        /// 활성화되고 선택된 Button의 UI 설정
        /// </summary>
        /// <param name="button"></param>
        private void RadButtonSelectedUI(RadButton button)
        {
            if (button.InvokeRequired)
            {
                Task.Run(() =>
                {
                    Invoke(new Action(delegate ()
                    {
                        button.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLNORMAL;
                        button.ButtonElement.BorderElement.ForeColor = MetaData.ConstantValues.CMAINSW_BORDERSELECT;
                        button.Enabled = true;
                    }));
                });
            }
            else
            {
                button.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLNORMAL;
                button.ButtonElement.BorderElement.ForeColor = MetaData.ConstantValues.CMAINSW_BORDERSELECT;
                button.Enabled = true;
            }
        }

        /// <summary>
        /// 활성화되고 선택되지 않은 Button의 UI 설정
        /// </summary>
        /// <param name="button"></param>

        private void RadButtonUnselectedUI(RadButton button)
        {
            if (button.InvokeRequired)
            {
                Task.Run(() =>
                {
                    Invoke(new Action(delegate ()
                    {
                        button.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLNORMAL;
                        button.ButtonElement.BorderElement.ForeColor = MetaData.ConstantValues.CMAINSW_BORDERUNSELECT;
                    }));
                });
            }
            else
            {
                button.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLNORMAL;
                button.ButtonElement.BorderElement.ForeColor = MetaData.ConstantValues.CMAINSW_BORDERUNSELECT;
            }
        }

        /// <summary>
        /// 비활성화된 Button의 UI 설정
        /// </summary>
        /// <param name="button"></param>
        private void RadButtonDisableUI(RadButton button)
        {            
            if (button.InvokeRequired)
            {
                Task.Run(() =>
                {
                    Invoke(new Action(delegate ()
                    {
                        button.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLDISABLE;
                        button.ButtonElement.BorderElement.ForeColor = MetaData.ConstantValues.CMAINSW_BORDERUNSELECT;
                        button.Enabled = false;
                    }));
                });
            }
            else
            {
                button.ButtonElement.ButtonFillElement.BackColor = MetaData.ConstantValues.CMAINSW_FILLDISABLE;
                button.ButtonElement.BorderElement.ForeColor = MetaData.ConstantValues.CMAINSW_BORDERUNSELECT;
                button.Enabled = false;
            }
        }

        /// <summary>
        /// 분석창이 생성될때마다 호출된다. 
        /// </summary>
        private void AssignSwitcherButton(AnalysisWindowView newAwInst)
        {
            if(SharedData.GetInstance().AnalysisWindowSharedData.Count != 0)
            {
                foreach (var control in tlSwitcher.Controls)
                {
                    if (control is RadButton button)
                    {
                        if (button.Tag == null)
                        {
                            button.Tag = newAwInst;
                            button.Click += SwitcherBtn_Click;                            
                            RadButtonSelectedUI(button);
                            //분석창에 버튼을 연결한 후, 두번째 버튼에는 동일한 분석창을 할당하지 않도록, 컨트롤 탐색 루프를 빠져나온다. 
                            break;
                        }
                        else
                        {
                            RadButtonUnselectedUI(button);
                        }
                    }                    
                }
            } 
        }

        /// <summary>
        /// 분석창이 선택(마우스 클릭)되었는지 상태를 확인하기 위한 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AnalysisWindow_Activated(object sender, EventArgs e)
        {
            if (!SharedData.GetInstance().onTimeClosingFlag)
            {
                foreach (var control in tlSwitcher.Controls)
                {
                    if (control is RadButton button)
                    {
                        if (button.Tag != null)
                        {
                            if ((sender as AnalysisWindowView).awUID.Equals((button.Tag as AnalysisWindowView).awUID, StringComparison.CurrentCultureIgnoreCase))
                            {
                                button.PerformClick();
                            }                            
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 분성창이 최소화되었는지 확인하기 위한 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AnalysisWindow_LocationChanged(object sender, EventArgs e)
        {
            AnalysisWindowView awInfo = sender as AnalysisWindowView;
            foreach (var control in tlSwitcher.Controls)
            {
                if (control is RadButton button)
                {
                    if (button.Tag != null)
                    {
                        if (awInfo.WindowState == FormWindowState.Minimized)
                        {
                            if (awInfo.awUID.Equals((button.Tag as AnalysisWindowView).awUID, StringComparison.CurrentCultureIgnoreCase))
                            {
                                RadButtonUnselectedUI(button);
                            }
                            else
                            {
                                if ((button.Tag as AnalysisWindowView).WindowState != FormWindowState.Minimized)
                                {
                                    RadButtonSelectedUI(button);
                                }                                
                            }
                        }
                        else
                        {
                            if (awInfo.awUID.Equals((button.Tag as AnalysisWindowView).awUID, StringComparison.CurrentCultureIgnoreCase))
                            {
                                RadButtonSelectedUI(button);
                            }
                            else
                            {
                                RadButtonUnselectedUI(button);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 스위처 버튼 클릭시 할당된 분석창을 맨앞으로 가져 오도록하는 버튼 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SwitcherBtn_Click(object sender, EventArgs e)
        {
            if(sender != null)
            {
                foreach (var control in tlSwitcher.Controls)
                {
                    if (control is RadButton button)
                    {
                        if (button.Tag != null)
                        {
                            if (button.Name.Equals((sender as RadButton).Name))
                            {
                                DoSwitchAnalyzer(button.Tag as AnalysisWindowView);
                                RadButtonSelectedUI(button);
                            }
                            else
                            {
                                RadButtonUnselectedUI(button);
                            }
                        }                            
                    }
                } 
            }
        }

        /// <summary>
        /// 분석창 스위처 클릭 이벤트에 따라 분석창을 앞으로 나오도록 UI를 변경하는 로직
        /// </summary>
        /// <param name="awInfo"></param>
        private void DoSwitchAnalyzer(AnalysisWindowView awInfo)
        {
            awInfo.LocationChanged -= AnalysisWindow_LocationChanged;
            awInfo.Activated -= AnalysisWindow_Activated;

            // AW에 등록된 이벤트와 버튼 클릭 이벤트가 충돌하여 이벤트를 삭제하고 로직 수행 후 다시 등록한다. 
            if (awInfo.InvokeRequired)
            {
                awInfo.Invoke(new MethodInvoker(() =>
                {
                    if (awInfo.WindowState == FormWindowState.Minimized)
                    {
                        awInfo.WindowState = awInfo.PrevWindowState;
                        awInfo.RBtn_MaximizeEnd_Click(null, null);
                    }
                    else
                    {
                        // 클릭된 분석창이 가장 앞으로 오도록 설정한다. Activate를 호출해야 정상 동작된다.
                        awInfo.Activate();
                        awInfo.BringToFront();
                    }                                                                   
                }));
            }
            else
            {
                if (awInfo.WindowState == FormWindowState.Minimized)
                {
                    awInfo.WindowState = awInfo.PrevWindowState;
                    awInfo.RBtn_MaximizeEnd_Click(null, null);
                }
                else
                {
                    // 클릭된 분석창이 가장 앞으로 오도록 설정한다. Activate를 호출해야 정상 동작된다.
                    awInfo.Activate();
                    awInfo.BringToFront();
                }
            }
            awInfo.LocationChanged += AnalysisWindow_LocationChanged;
            awInfo.Activated += AnalysisWindow_Activated;
        }

        /// <summary>
        /// 분석기 스위처에 설정한 내용을 제거한다. 
        /// </summary>
        /// <param name="newAwInst"></param>
        private void RemoveSwitcherButton(AnalysisWindowView newAwInst)
        {
            try
            {
                foreach (var control in tlSwitcher.Controls)
                {
                    if (control is RadButton button)
                    {
                        if (button.Tag != null)
                        {
                            if ((button.Tag as AnalysisWindowView).awUID.Equals(newAwInst.awUID, StringComparison.CurrentCultureIgnoreCase))
                            {
                                RadButtonDisableUI(button);
                                button.Tag = null;
                                button.Click -= SwitcherBtn_Click;
                            }
                            else
                            {
                                RadButtonSelectedUI(button);
                            }
                        }
                    }                    
                }
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        #endregion

        #region [ UI 이벤트 처리기 로직 ] 


        ///// <summary>
        ///// 어셈블리 버전 번호를 리턴
        ///// </summary>
        ///// <returns></returns>
        //public static string SetAppVersionDisplay()
        //{
        //    Version assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        //    DateTime buildDate = new DateTime(2000, 1, 1).AddDays(assemblyVersion.Build).AddSeconds(assemblyVersion.Revision * 2);
        //    return string.Format(   CultureInfo.CreateSpecificCulture("en-US"), "v. {0}", assemblyVersion);
        //}

        private void RBtn_Setting_Click(object sender, EventArgs e)
        {
            if (settingView == null)
            {
                settingView = new SettingView();
                settingView.FormClosed += ControlLimitSettingForm_FormClosed;
                settingView.StartPosition = FormStartPosition.CenterScreen;
                settingView.ShowDialog();
            }
            else
            {
                settingView.WindowState = FormWindowState.Normal;
                settingView.BringToFront();
            }
            CheckAutoLogout();
            SaveAutoInfo();
        }

        private void CmdButtonOpenRecorder_Click(object sender, EventArgs e)
        {
            if (recView == null)
            {
                recView = new RecorderView(MainController);
                recView.FormClosed += recView_FormClosed;

                Point LocOriginRecView = recView.Location;
                Screen screen = Screen.FromControl(this);

                LocOriginRecView.X = MetaData.ConstantValues.FORM_MARGIN;
                LocOriginRecView.Y = Screen.PrimaryScreen.WorkingArea.Size.Height - (recView.Height + MetaData.ConstantValues.ALPHA_MARGIN);

                recView.Location = LocOriginRecView;

                recView.Show();

                RawDataParser.GetInstance().IsExistRecorderFlag = true;
            }
            else
            {
                recView.WindowState = FormWindowState.Normal;
                recView.BringToFront();
            }
        }

        private void recView_FormClosed(object sender, FormClosedEventArgs e)
        {
            recView = null;
            RawDataParser.GetInstance().IsExistRecorderFlag = false;
        }

        /// <summary>
        /// 레코드뷰 버튼 활성 비활성 로직, 데이터 소스에 따라 다르다.
        /// </summary>
        /// <param name="isEnable"></param>
        public void RecViewButtonUIChange(bool isEnable)
        {
            if (cmdBarMain.InvokeRequired)
            {
                cmdBarMain.Invoke(new MethodInvoker(() =>
                {
                    cmdButtonRec.Enabled = isEnable;
                }));
            }
            else
            {
                cmdButtonRec.Enabled = isEnable;
            }
        }

        /// <summary>
        /// 분석창에 TStripBtn_MarkViewer 활성화 유무로 Marker 버튼을 활성화 여부를 판단한다.
        /// TStripBtn_MarkViewer의 visible은 false로 되어 있음 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TStripBtn_MarkViewer_EnabledChanged(object sender, EventArgs e)
        {
            bool isEnable = false;

            List<string> before_awKeys = new List<string>(SharedData.GetInstance().ActiveAnalysisWindowDic.Keys);

            if (before_awKeys.Count == 0)
            {
                TagViewStartPosition = Point.Empty;
            }

            foreach (var key in before_awKeys)
            {
                isEnable |= (SharedData.GetInstance().ActiveAnalysisWindowDic[key] as AnalysisWindowView).tStripBtn_MarkViewer.Enabled;

                if (TagViewStartPosition.IsEmpty && isEnable)
                {
                    TagViewStartPosition = (SharedData.GetInstance().ActiveAnalysisWindowDic[key] as AnalysisWindowView).Location;
                    Task.Run(() =>
                    {
                        Invoke(new Action(delegate ()
                        {
                            tagView.Location = TagViewStartPosition;
                        }));
                    });
                }
            }

            Task.Run(() =>
            {
                Invoke(new Action(delegate ()
                {
                    cmdButtonMarker.Enabled = isEnable;
                }));
            });
        }
        
        private void MarkerView_Click(object sender, EventArgs e)
        {
            OnStripChartTagViewShow();
        }

        private void OnStripChartTagViewShow()
        {
            if (tagView.InvokeRequired)
            {
                tagView.Invoke(new MethodInvoker(() =>
                {
                    tagView.Visible = !tagView.Visible;
                }));
            }
            else
            {
                tagView.Visible = !tagView.Visible;
            }

            if (tagView.Visible)
            {
                System.Windows.Forms.Timer InvalidateTimer = new System.Windows.Forms.Timer()
                {
                    Interval = 100
                };
                InvalidateTimer.Tick += InvalidateTimer_Tick;
                InvalidateTimer.Enabled = true;
            }
        }

        private void InvalidateTimer_Tick(object sender, EventArgs e)
        {
            if (tagView.InvokeRequired)
            {
                tagView.Invoke(new MethodInvoker(() =>
                {
                    tagView.Invalidate();
                }));
            }
            else
            {
                tagView.Invalidate();
            }

            (sender as System.Windows.Forms.Timer).Dispose();
        }

        private void RTGBtn_AutoSave_ToggleStateChanged(object sender, StateChangedEventArgs args)
        {
            RadToggleButton autoSaveButton = (RadToggleButton)sender;

            switch (args.ToggleState)
            {
                case Telerik.WinControls.Enumerations.ToggleState.On:
                    autoSaveButton.BackColor = Color.FromArgb(204, 153, 255);
                    autoSaveButton.ButtonElement.TextElement.ForeColor = Color.Black;
                    UserAutoSaveInfo.GetInstance().IsAutoSave = true;
                    break;
                case Telerik.WinControls.Enumerations.ToggleState.Off:
                    autoSaveButton.BackColor = Color.FromArgb(247, 247, 247);
                    UserAutoSaveInfo.GetInstance().IsAutoSave = false;
                    break;
            }
        }

        private void RTGBtn_AutoLogout_ToggleStateChanged(object sender, StateChangedEventArgs args)
        {
            RadToggleButton autoLogoutButton = (RadToggleButton)sender;

            switch (args.ToggleState)
            {
                case Telerik.WinControls.Enumerations.ToggleState.On:
                    autoLogoutButton.BackColor = Color.FromArgb(204, 153, 255);
                    autoLogoutButton.ButtonElement.TextElement.ForeColor = Color.Black;
                    UserAutoSaveInfo.GetInstance().userID = SharedData.GetInstance().CurrentUser.UserID;
                    UserAutoSaveInfo.GetInstance().IsAutoLogout = true;
                    break;
                case Telerik.WinControls.Enumerations.ToggleState.Off:
                    autoLogoutButton.BackColor = Color.FromArgb(247, 247, 247);
                    UserAutoSaveInfo.GetInstance().userID = SharedData.GetInstance().CurrentUser.UserID;
                    UserAutoSaveInfo.GetInstance().IsAutoLogout = false;
                    break;
            }
            CheckAutoLogout();
            SaveAutoInfo();
        }

        private void RadDropDownList1_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.KeyChar = (char)Keys.None;
        }

        private void SaveAutoInfo()
        {
            try
            {
                var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).ToString(CultureInfo.CreateSpecificCulture("en-US")) + @"\nDA";
                var file = Path.Combine(basePath, ConstantVal.UserAuto);
                JObject obj;
                if (!Directory.Exists(basePath))
                {
                    Directory.CreateDirectory(basePath);
                }
                if (File.Exists(file))
                {
                    List<UserAutoSaveInfo> infos = new List<UserAutoSaveInfo>();
                    bool isUpdate = false;
                    using (var outputFile = new StreamReader(file))
                    {
                        string msg = outputFile.ReadToEnd();
                        outputFile.Close();

                        obj = JObject.Parse(msg);

                        var list = obj["Info"];
                        foreach (var item in list.Children())
                        {
                            var info = JsonConvert.DeserializeObject<UserAutoSaveInfo>(item.ToString());
                            if (info.userID == SharedData.GetInstance().CurrentUser.UserID)
                            {
                                infos.Add(UserAutoSaveInfo.GetInstance());
                                isUpdate = true;
                            }
                            else
                            {
                                infos.Add(info);
                            }
                        }
                    }
                    if (!isUpdate)
                    {
                        infos.Add(UserAutoSaveInfo.GetInstance());
                    }
                    JArray ja = new JArray();
                    foreach (var u in infos)
                    {
                        JToken token = JToken.FromObject(u);
                        ja.Add(token);
                    }
                    JObject jObj = new JObject
                    {
                        { "Info", ja }
                    };

                    using (StreamWriter outputFile = new StreamWriter(file))
                    {
                        outputFile.Write(jObj.ToString());
                    }
                }
                else
                {
                    JToken token = JToken.FromObject(UserAutoSaveInfo.GetInstance());
                    JArray ja = new JArray
                    {
                        token
                    };
                    JObject jObj = new JObject
                    {
                        { "Info", ja }
                    };
                    File.WriteAllText(file, jObj.ToString());
                }
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        private void LoadAutoInfo()
        {
            try
            {
                var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).ToString(CultureInfo.CreateSpecificCulture("en-US")) + @"\nDA";
                var file = Path.Combine(basePath, ConstantVal.UserAuto);
                if (!Directory.Exists(basePath))
                {
                    Directory.CreateDirectory(basePath);
                }
                JObject obj;
                if (File.Exists(file))
                {

                    using (var outputFile = new StreamReader(file))
                    {
                        obj = JObject.Parse(outputFile.ReadToEnd());
                        outputFile.Close();
                        var list = obj["Info"];
                        foreach (var item in list.Children())
                        {
                            var info = JsonConvert.DeserializeObject<UserAutoSaveInfo>(item.ToString());
                            if (info.userID == SharedData.GetInstance().CurrentUser.UserID)
                            {
                                SharedData.GetInstance().OffSimInfo.AutoSaveInfo = new UserAutoSaveInfo(info);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    JToken token = JToken.FromObject(UserAutoSaveInfo.GetInstance());
                    JArray ja = new JArray
                    {
                        token
                    };
                    JObject jObj = new JObject
                    {
                        { "Info", ja }
                    };
                    File.WriteAllText(file, jObj.ToString());

                }

                if (SharedData.GetInstance().OffSimInfo.AutoSaveInfo == null)
                {
                    SharedData.GetInstance().OffSimInfo.AutoSaveInfo = new UserAutoSaveInfo
                    {
                        userID = SharedData.GetInstance().CurrentUser.UserID
                    };
                }

                if (SharedData.GetInstance().OffSimInfo.AutoSaveInfo.IsAutoLogout)
                {
                    if (timeoutTimer == null)
                    {
                        timeoutTimer = new System.Timers.Timer();
                        timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
                        timeoutTimer.Interval = 1000;
                    }
                    else
                    {
                        timeoutTimer.Stop();
                        timeoutTimer.Enabled = false;
                    }
                    timeoutTimeValue = SharedData.GetInstance().OffSimInfo.AutoSaveInfo.LogoutTime * minToSec;
                    timeoutTimer.Enabled = true;
                    GlobalMouseEventListener.Start();
                }
                else
                {
                    if (timeoutTimer != null)
                    {
                        timeoutTimer.Stop();
                        timeoutTimer.Enabled = false;
                        GlobalMouseEventListener.Stop();
                    }
                }

                if (File.Exists(file))
                {
                    List<UserAutoSaveInfo> infos = new List<UserAutoSaveInfo>();
                    bool isUpdate = false;
                    using (var outputFile = new StreamReader(file))
                    {
                        string msg = outputFile.ReadToEnd();
                        outputFile.Close();
                        obj = JObject.Parse(msg);

                        var list = obj["Info"];
                        foreach (var item in list.Children())
                        {
                            var info = JsonConvert.DeserializeObject<UserAutoSaveInfo>(item.ToString());
                            if (info.userID == SharedData.GetInstance().CurrentUser.UserID)
                            {
                                infos.Add(SharedData.GetInstance().OffSimInfo.AutoSaveInfo);
                                isUpdate = true;
                            }
                            else
                            {
                                infos.Add(info);
                            }
                        }
                    }
                    if (!isUpdate)
                    {
                        infos.Add(SharedData.GetInstance().OffSimInfo.AutoSaveInfo);
                    }
                    JArray ja = new JArray();
                    foreach (var u in infos)
                    {
                        JToken token = JToken.FromObject(u);
                        ja.Add(token);
                    }
                    JObject jObj = new JObject { { "Info", ja } };
                    using (StreamWriter outputFile = new StreamWriter(file))
                    {
                        outputFile.Write(jObj.ToString());
                    }
                }
                else
                {
                    JToken token = JToken.FromObject(SharedData.GetInstance().OffSimInfo.AutoSaveInfo);
                    JArray ja = new JArray { token };
                    JObject jObj = new JObject { { "Info", ja } };
                    File.WriteAllText(file, jObj.ToString());
                }
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        private double timeoutTimeValue;
        /// <summary>
        /// 자동로그아웃을 수행할 함수
        /// </summary>
        private void CheckAutoLogout()
        {
            var userSaveInfo = UserAutoSaveInfo.GetInstance();
            if (userSaveInfo.IsAutoLogout)
            {
                if (timeoutTimer == null)
                {
                    timeoutTimer = new System.Timers.Timer();
                    timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
                    timeoutTimer.Interval = 1000;
                }
                else
                {
                    timeoutTimer.Stop();
                    timeoutTimer.Enabled = false;
                }
                timeoutTimeValue = SharedData.GetInstance().OffSimInfo.AutoSaveInfo.LogoutTime * minToSec;
                timeoutTimer.Enabled = true;
                GlobalMouseEventListener.Start();
            }
            else
            {
                if (timeoutTimer != null)
                {
                    timeoutTimer.Stop();
                    timeoutTimer.Enabled = false;
                    GlobalMouseEventListener.Stop();
                }
            }
        }

        private void KeyDownEventListener(object sender, RawKeyEventArgs args)
        {
            TimeOutTimerReset();
        }

        private void TimeoutTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            //데이터 소스 창 활성화시에는 동작하지 않도록 함.(20200810 @Donghee.choi
            if (IsDataSourceViewShow)
            {
                return;
            }
            timeoutTimeValue -= 1;
            if (timeoutTimeValue <= 0)
            {
                timeoutTimer.Enabled = false;
                SharedData.GetInstance().AutoClose = true;
                LogOut(true);
            }
        }

        private void MouseEventListener(object sender, EventArgs e)
        {
            TimeOutTimerReset();
        }

        private void TimeOutTimerReset()
        {
            if (SharedData.GetInstance().OffSimInfo.AutoSaveInfo != null)
            {
                timeoutTimeValue = SharedData.GetInstance().OffSimInfo.AutoSaveInfo.LogoutTime * minToSec;
            }
        }
        #endregion

        #region [ UI 제어 로직 ]

        #region [메인 버튼 이벤트 로직]

        #region [분석창 관련 화면 제어 기능]
        private void CommandBtnNew_Click(object sender, EventArgs e)
        {
            if (SharedData.GetInstance().AnalysisWindowSharedData.Count >= 2)
            {
                if (!MessageDlg.GetInstance().Visible)
                {
                    MessageDlg.GetInstance().SetWarning(global::nDA.MetaData.Properties.Resources.TitleWarning, global::nDA.MetaData.Properties.Resources.InfoAnalyzerLimit, BoxType.CLOSE);
                    MessageDlg.GetInstance().ShowDialog();
                }
                else
                {
                    strFormatter.Clear();
                    strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), global::nDA.MetaData.Properties.Resources.InfoMessageBoxLimit);
#if (DEBUG)
                    CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name}");
#endif
                    FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 1);
                }

                return;
            }

            AnalysisWindowCreateView form = new AnalysisWindowCreateView();
            int offsetX = 0;
            int offsetY = 0;

            if ((DataToolView != null) && (ObjectBrowserForm != null))
            {
                offsetX = (DataToolView.Location.Y < ObjectBrowserForm.Location.Y) ? DataToolView.Location.X + DataToolView.Width + MetaData.ConstantValues.ALPHA_MARGIN : ObjectBrowserForm.Location.X + ObjectBrowserForm.Width + MetaData.ConstantValues.ALPHA_MARGIN;
                offsetY = (DataToolView.Location.Y < ObjectBrowserForm.Location.Y) ? DataToolView.Location.Y : ObjectBrowserForm.Location.Y;
            }
            else if (DataToolView != null)
            {
                offsetX = DataToolView.Location.X + DataToolView.Width + MetaData.ConstantValues.ALPHA_MARGIN;
                offsetY = DataToolView.Location.Y;

            }
            else if (ObjectBrowserForm != null)
            {
                offsetX = ObjectBrowserForm.Location.X + ObjectBrowserForm.Width + MetaData.ConstantValues.ALPHA_MARGIN;
                offsetY = ObjectBrowserForm.Location.Y;
            }
            else
            {
                offsetX = Location.X;
                offsetY = Location.Y + (Height + MetaData.ConstantValues.ALPHA_MARGIN);
            }

            form.Location = new Point(offsetX, offsetY);
            form.ShowDialog();

            if (form.DialogResult == DialogResult.OK)
            {
                AnalysisWindowInfoExtends info = new AnalysisWindowInfoExtends(form.AwTitle, form.AwComment, form.BgColor, form.AwIndex);

                OpenAnalysisWindow(info, Screen.FromControl(this));
            }
            form.Dispose();
        }

        private Thread thread;

        private void OpenAnalysisWindow(AnalysisWindowInfoExtends info, Screen szScreen)
        {
            try
            {
                if (info != null)
                {
                    if (!SharedData.GetInstance().AnalysisWindowSharedData.ContainsKey(info.BaseInfo.UID))
                    {
#if (_MULTI_APP_)
                        thread = new Thread(() =>
                        {
#endif
                            try
                            {
                                AnalysisWindowView awForm = new AnalysisWindowView(info);

                                lock (MainController)
                                {
                                    //프랙바 슬라이더 흐름이 가능 하도록 플래그 변경
                                    tbUpdateFlag = true;

                                    MainController.AnalysisWindowResetEvent += awForm.OnReset;
                                    MainController.StatusUpdateEventToAnalysisWindow.MUpdate += awForm.StatusUpdateFromMain;
                                    awForm.MainController.ParameterUpdateEvent.Update += MainController.OnAnalysisWindowParameterUpdate;
                                    awForm.FormClosed += AnalysisWindow_Closed;
                                    awForm.Activated += AnalysisWindow_Activated;
                                    awForm.LocationChanged += AnalysisWindow_LocationChanged;
                                    awForm.tStripBtn_MarkViewer.EnabledChanged += TStripBtn_MarkViewer_EnabledChanged;
                                    //메인의 "MainViewBtnUpdated" 발생시 분석창의 "OnUpdatePlayStopFromMain" 호출
                                    OnChangedMainPlayStatus += awForm.OnUpdatePlayStopFromMain;

                                    //분석창이 모니터 범위 밖에 있는 경우, 원점으로 가져옴.
                                    if (Utils.GetScreen(awForm) == null)
                                    {
                                        var workingArea = Utils.GetScreenFromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
                                        awForm.Location = new Point(workingArea.X, workingArea.Y);
                                    }

                                    if (szScreen != null)
                                    {
                                        //분석기의 가로크기: 스크린 크기
                                        //분석기의 세로크기: (스크린 크기 - (메인폼의 Height + 모니터의 Top으로부터의 간격)
                                        Size awSize = new Size(szScreen.WorkingArea.Width - (MetaData.ConstantValues.FORM_MARGIN * 2), szScreen.WorkingArea.Height - (Height + MetaData.ConstantValues.FORM_MARGIN + MetaData.ConstantValues.ALPHA_MARGIN * 2));
                                        Point awLocation = new Point(MetaData.ConstantValues.FORM_MARGIN, Height + MetaData.ConstantValues.FORM_MARGIN + MetaData.ConstantValues.ALPHA_MARGIN);

                                        //데이터툴과 오브젝트툴이 모두 열려있을 경우 그 옆으로 팝업하는 로직
                                        if ((DataToolView != null) && (ObjectBrowserForm != null))
                                        {
                                            awLocation.X = (DataToolView.Location.Y < ObjectBrowserForm.Location.Y) ? awLocation.X + MetaData.ConstantValues.ALPHA_MARGIN + DataToolView.Size.Width : awLocation.X + MetaData.ConstantValues.ALPHA_MARGIN + ObjectBrowserForm.Width;
                                            awSize.Width = (DataToolView.Location.Y < ObjectBrowserForm.Location.Y) ? awSize.Width - (MetaData.ConstantValues.ALPHA_MARGIN + DataToolView.Size.Width) : awSize.Width - (MetaData.ConstantValues.ALPHA_MARGIN + ObjectBrowserForm.Width);
                                        }
                                        //데이터툴이 열려있을 경우 그 옆으로 팝업하는 로직
                                        else if (DataToolView != null)
                                        {
                                            awLocation.X += MetaData.ConstantValues.ALPHA_MARGIN + DataToolView.Size.Width;
                                            awSize.Width -= MetaData.ConstantValues.ALPHA_MARGIN + DataToolView.Size.Width;
                                        }
                                        //오브젝트툴이 열려있을 경우 그 옆으로 팝업하는 로직
                                        else if (ObjectBrowserForm != null)
                                        {
                                            awLocation.X += MetaData.ConstantValues.ALPHA_MARGIN + ObjectBrowserForm.Width;
                                            awSize.Width -= MetaData.ConstantValues.ALPHA_MARGIN + ObjectBrowserForm.Width;
                                        }

                                        info.WindowLocation = awLocation;
                                        info.WindowSize = awSize;
                                        awForm.Location = info.WindowLocation;
                                        awForm.Size = info.WindowSize;
                                    }
                                    else
                                    {
                                        awForm.Location = info.WindowLocation;
                                        awForm.Size = new Size((info.WindowSize.Width > Screen.PrimaryScreen.Bounds.Width) ? Screen.PrimaryScreen.Bounds.Width : info.WindowSize.Width,
                                            (info.WindowSize.Height > Screen.PrimaryScreen.Bounds.Height) ? Screen.PrimaryScreen.Bounds.Height : info.WindowSize.Height);
                                    }

                                    awForm.BackColor = info.BaseInfo.BgColor;

                                    //=============================================================================================================
                                    //분석창 정보 리스트에 추가
                                    //=============================================================================================================
                                    var awSharedData = new AnalysisWindowStatusInfo();
                                    awSharedData.SaveStatusUpdateEventToAnalysisWindow.Update += awForm.OnUpdateSaveBtnStatus;
                                    var sharedInstance = SharedData.GetInstance();
                                    awSharedData.AwInfoExt = info;

                                    if (SharedData.GetInstance().DataSourceType != PlayDataSource.OFFLINE)
                                    {
                                        if (sharedInstance.CurrentUser.RoleName == "Admin" || sharedInstance.CurrentUser.UserID == info.BaseInfo.UserID)
                                        {
                                            awSharedData.EnableSaveMode = true;
                                        }
                                        else
                                        {
                                            awSharedData.EnableSaveMode = false;
                                        }
                                    }
                                    sharedInstance.AnalysisWindowSharedData.Add(info.BaseInfo.UID, awSharedData);
                                    sharedInstance.ActiveAnalysisWindowDic.Add(info.BaseInfo.UID, awForm);
                                    var awWorkSpace = awForm.mainWorkSpace;

                                    //sharedInstance.AnalysisWindowSharedData[info.BaseInfo.UID].SetDisplayComponentViewList(ref awForm.MainController.DisplayComponentViewList, ref awWorkSpace);
                                    sharedInstance.AnalysisWindowSharedData[info.BaseInfo.UID].SetDisplayComponentViewList(awForm.MainController.AwCharts.GetAllList(), ref awWorkSpace);
                                    awForm.Show();
                                    
                                    //분석창 스위처 UI 변경하기 위 호출
                                    AssignSwitcherButton(awForm);
                                    awForm.Activate();
                                    awForm.BringToFront();
                                }

#if (_MULTI_APP_)
                                //다이얼로그를 프로세스로 실행 시킨다. 메인과 다른 새로운 메세지 루프가 생성된다. 
                                //새로운 메인쓰레드가 생성된다. 즉, Main - Main Thread#1, Aw1 - Main Thread#2, Aw2 - Main Thread#3
                                //.NET에서의 Application의 구조는 Process->Application Domain->Thread->Context
                                //message loop for the windows forms application. At its most basic level it keeps the process alive until the last form is closed
                                Application.Run(awForm);
                            }
                            catch (Exception ex)
                            {
                                strFormatter.Clear();
                                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
                            }
                        });

                        thread.SetApartmentState(ApartmentState.STA);
                        thread.Start();
#endif
                    }
                }
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        /// <summary>
        /// 분석창이 닫힐때 발생 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AnalysisWindow_Closed(object sender, FormClosedEventArgs e)
        {
            var uid = (sender as AnalysisWindowView).MainController.awUID;

            (sender as AnalysisWindowView).Activated -= AnalysisWindow_Activated;
            (sender as AnalysisWindowView).LocationChanged -= AnalysisWindow_LocationChanged;
            MainController.AnalysisWindowResetEvent -= (sender as AnalysisWindowView).OnReset;
            MainController.StatusUpdateEventToAnalysisWindow.MUpdate -= (sender as AnalysisWindowView).StatusUpdateFromMain;
            OnChangedMainPlayStatus -= (sender as AnalysisWindowView).OnUpdatePlayStopFromMain;
            (sender as AnalysisWindowView).FormClosed -= AnalysisWindow_Closed;

            if (SharedData.GetInstance().AnalysisWindowSharedData.ContainsKey(uid))
            {
                SharedData.GetInstance().AnalysisWindowSharedData.Remove(uid);
            }

            if (!SharedData.GetInstance().onTimeClosingFlag)
            {
                // 열려있는 분석창이 없는경우, TagView창을 닫는다.
                if (SharedData.GetInstance().AnalysisWindowSharedData.Count == 0)
                {
                    if (tagView.InvokeRequired)
                    {
                        tagView.Invoke(new MethodInvoker(() =>
                        {
                            tagView.Visible = false;
                        }));
                    }
                    else
                    {
                        tagView.Visible = false;
                    }
                }
                else
                {
                    foreach (var aw in SharedData.GetInstance().AnalysisWindowSharedData)
                    {
                        if ((aw.Value.DisplayComponentViewList.FindAll(x => (x as CommonDisplayComponent).GetInfo().DisplayType == DisplayModuleType.StripChartH |
                                                                            (x as CommonDisplayComponent).GetInfo().DisplayType == DisplayModuleType.StripChartV).Count == 0))
                        {
                            if (tagView.InvokeRequired)
                            {
                                tagView.Invoke(new MethodInvoker(() =>
                                {
                                    tagView.Visible = false;
                                }));
                            }
                            else
                            {
                                tagView.Visible = false;
                            }
                        }
                    }
                }
            }

            CheckAwStatus(uid);

            //로그아웃으로 인해 분석기가 닫히는것이 아닌 경우에만 진입하여 처리
            if (!SharedData.GetInstance().onTimeClosingFlag)
            {
                RemoveSwitcherButton(sender as AnalysisWindowView);
            }
            TStripBtn_MarkViewer_EnabledChanged(null, null);
        }

        private void BtnOpenAnalysisWindow_Click(object sender, EventArgs e)
        {
           if (analysisWindowManager == null)
            {
                analysisWindowManager = new AnalysisWindowManagerView();
                analysisWindowManager.AnalysisOpenRequestEvent += AnalysisWindowManager_AnalysisOpenRequestEvent;
                analysisWindowManager.FormClosed += AnalysisWindowManager_FormClosed;
                LocationChanged += MainWindow_LocationChanged;
                Point p = DesktopLocation;
                p.X += Width;
                Size size = analysisWindowManager.Size;

                //analysisWindowManager.Location = p;
                analysisWindowManager.Show();
            }
            else
            {
                analysisWindowManager.WindowState = FormWindowState.Normal;
                analysisWindowManager.BringToFront();
            }
        }

        private void AnalysisWindowManager_AnalysisOpenRequestEvent(AnalysisWindowInfoExtends obj)
        {
            OpenAnalysisWindow(obj, null);
        }

        private void AnalysisWindowManager_FormClosed(object sender, FormClosedEventArgs e)
        {
            analysisWindowManager.AnalysisOpenRequestEvent -= AnalysisWindowManager_AnalysisOpenRequestEvent;
            analysisWindowManager.Dispose();
            analysisWindowManager = null;
        }
        #endregion

        #region [디스플레이 툴 화면 제어 기능]
        private void BtnDisplayTool_Click(object sender, EventArgs e)
        {
            if (ObjectBrowserForm == null)
            {
                ObjectBrowserForm = new ObjectBrowserView();
                ObjectBrowserForm.FormClosed += DisplayToolForm_FormClosed;
                Point LocOriginDispTool = ObjectBrowserForm.Location;

                LocOriginDispTool.X = MetaData.ConstantValues.FORM_MARGIN;
                LocOriginDispTool.Y = MetaData.ConstantValues.FORM_HEIGHT_DEFAULT + MetaData.ConstantValues.FORM_MARGIN + MetaData.ConstantValues.ALPHA_MARGIN;

                //디스플레이툴이 열려있을 경우 그 옆으로 팝업하는 로직
                if (Application.OpenForms[global::nDA.MetaData.Properties.Resources.ClassDataTool] is DataManagerView DtView)
                {
                    Point dataToolLoc = DtView.Location;
                    Size dataToolSz = DtView.Size;

                    LocOriginDispTool.X = dataToolLoc.X;

                    if (LocOriginDispTool.Y == dataToolLoc.Y)
                    {
                        LocOriginDispTool.Y = dataToolLoc.Y + dataToolSz.Height + MetaData.ConstantValues.ALPHA_MARGIN;
                    }                        
                }

                ObjectBrowserForm.Location = LocOriginDispTool;
                ObjectBrowserForm.Show();
            }
            else
            {
                ObjectBrowserForm.WindowState = FormWindowState.Normal;
                ObjectBrowserForm.BringToFront();
            }

            //if (ObjectBrowserForm == null)
            //{
            //    ObjectBrowserForm = new ObjectBrowserView();
            //    ObjectBrowserForm.FormClosed += DisplayToolForm_FormClosed;
            //    ObjectBrowserForm.Location = new Point(Location.X + Size.Width, Location.Y);
            //    ObjectBrowserForm.Show();
            //    if (DataToolView != null)
            //    {
            //        Point p = Location;
            //        p.X += Size.Width + (ObjectBrowserForm == null ? 0 : ObjectBrowserForm.Width);
            //        DataToolView.Location = p;
            //    }
            //}
            //else
            //{
            //    ObjectBrowserForm.WindowState = FormWindowState.Normal;
            //    ObjectBrowserForm.BringToFront();
            //}
        }
        private void DisplayToolForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            ObjectBrowserForm = null;
        }
        #endregion

        #region [파라미터 툴 화면 제어 기능]
        private void BtnDataTool_Click(object sender, EventArgs e)
        {
            if (DataToolView == null)
            {
                //메인 클래스 인스턴스를 넘기도록 생성자 수정
                DataToolView = new DataManagerView();
                DataToolView.FormClosed += DataToolForm_FormClosed;
                Point LocOriginDataTool = DataToolView.Location;

                LocOriginDataTool.X = MetaData.ConstantValues.FORM_MARGIN;
                LocOriginDataTool.Y = MetaData.ConstantValues.FORM_HEIGHT_DEFAULT + MetaData.ConstantValues.FORM_MARGIN + MetaData.ConstantValues.ALPHA_MARGIN;

                //디스플레이툴이 열려있을 경우 그 옆으로 팝업하는 로직
                if (Application.OpenForms[global::nDA.MetaData.Properties.Resources.ClassObjectBrowser] is ObjectBrowserView ObView)
                {
                    Point displayToolLoc = ObView.Location;
                    Size displayToolSz = ObView.Size;

                    LocOriginDataTool.X = displayToolLoc.X;

                    if (LocOriginDataTool.Y == displayToolLoc.Y)
                    {
                        LocOriginDataTool.Y = displayToolLoc.Y + displayToolSz.Height + MetaData.ConstantValues.ALPHA_MARGIN;
                    }                    
                }

                DataToolView.Location = LocOriginDataTool;

                DataToolView.Show();
            }
            else
            {
                DataToolView.WindowState = FormWindowState.Normal;
                DataToolView.BringToFront();
            }
        }

        private void DataToolForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            DataToolView.Dispose();
            DataToolView = null;
        }

        /// <summary>
        /// 데이터 브라우저의 UI를 변경하는 로직
        /// </summary>
        /// <param name="isEnable"></param>
        public void DataToolButtonUIChange(bool isEnable)
        {
            if (cmdBarMain.InvokeRequired)
            {
                cmdBarMain.Invoke(new MethodInvoker(() =>
                {
                    //데이터툴이 열려있을 경우 그 옆으로 팝업하는 로직
                    if ((Application.OpenForms[global::nDA.MetaData.Properties.Resources.ClassDataTool] is DataManagerView DtView) && !isEnable)
                    {
                        DtView.Close();
                    }
                    cmdButtonData.Enabled = isEnable;
                }));
            }
            else
            {
                //데이터툴이 열려있을 경우 그 옆으로 팝업하는 로직
                if ((Application.OpenForms[global::nDA.MetaData.Properties.Resources.ClassDataTool] is DataManagerView DtView) && !isEnable)
                {
                    DtView.Close();
                }
                cmdButtonData.Enabled = isEnable;
            }
        }

        #endregion

        #region [데이터 소스선택 화면 제어 기능]

        private void labSysStatus_Click(object sender, EventArgs e)
        {

        }

        private bool IsDataSourceViewShow;
        private void BtnDataSource_Click(object sender, EventArgs e)
        {
            //재생중에는 데이터 소스 변경안됨.
            if (SharedData.GetInstance().PlayMode == PlayMode.PLAY)
            {
                if (!MessageDlg.GetInstance().Visible)
                {
                    MessageDlg.GetInstance().SetCaution(global::nDA.MetaData.Properties.Resources.TitleCaution, global::nDA.MetaData.Properties.Resources.InfoSourceChange, BoxType.CLOSE);
                    MessageDlg.GetInstance().ShowDialog();
                }
                else
                {
                    strFormatter.Clear();
                    strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), global::nDA.MetaData.Properties.Resources.InfoSourceChange);
#if (DEBUG)
                    CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name}");
#endif
                    FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 1);
                }

                return;
            }

            //Playback인 경우 Realtime 상태로 변경한다. 
            if (SharedData.GetInstance().DataSourceType == PlayDataSource.REPLAY)
            {
                SharedData.GetInstance().DataSourceType = PlayDataSource.REALTIME;

                tlTimebar.Visible = false;
                DataToolButtonUIChange(false);
                RecViewButtonUIChange(false);
                MainController.ResponseParameterResult = false;

                //실시간 모드에서는 리플레이에서 저장된 태그이벤트 정보를 초기화해줌.
                SharedData.GetInstance().OffSimInfo.PreLoadedTagEventMsg = null;
                if (SharedData.GetInstance().OffSimInfo.TagEventList != null)
                {
                    SharedData.GetInstance().OffSimInfo.TagEventList.Clear();
                }
                StripChartTagView.GetInstance().Clear();

                //수신 파라미터 버퍼틑 비운다. 
                MasterParamList.GetInstance().ClearMasterParams();

                SetUIChangeByPlayMode(SharedData.GetInstance().DataSourceType);

                UpdateDataSourceLabel();
            } 
            else
            {
                if (dataSourceSelectView == null)
                {
                    dataSourceSelectView = new DataSourceSelectView();
                    dataSourceSelectView.StartPosition = FormStartPosition.CenterScreen;
                    dataSourceSelectView.FormClosed += DataSourceManagerForm_Closed;

                    dataSourceSelectView.Show();

                    //데이터 소스선택 다이얼로그가 Modaless로 팝업되기 때뭉에 그동안 Play/Stop 버튼을 비활성화한다. 
                    OnPlayStopEnable(false);
                }
                else
                {
                    dataSourceSelectView.WindowState = FormWindowState.Normal;
                    dataSourceSelectView.BringToFront();
                }

                IsDataSourceViewShow = true;
            }
        }

        /// <summary>
        /// 데이터소스 매니저 다이얼로그 Close 이벤트 처리
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DataSourceManagerForm_Closed(object sender, FormClosedEventArgs e)
        {
            var simulInfo = dataSourceSelectView.selectedSimulationInfo;
            var sharedData = SharedData.GetInstance();

            if (dataSourceSelectView.DialogResult == DialogResult.OK)
            {
                MainController.isDisplayParamRequested = false;

                //초기화
                tagView.Clear();
                sharedData.ClearSharedData();
                SimulationBufferManager.GetInstance().Clear();
                sharedData.PlayMode = PlayMode.NONE;
                MainController.StatusUpdateEventToAnalysisWindow.ActiveEvent(AwDisplayEventType.Reset, this, null);

                //UDP가 문제로 인해 끊어진 경우에는 기존 객체를 dispose 하고, UDP 객체를 다시만든다.
                ResetUdpConnection();

                sharedData.OffSimInfo.SimulInfo = simulInfo;

                var tagList = SharedData.GetInstance().OffSimInfo.TagEventList;
                if (tagList != null && tagList.Count > 0)
                {
                    foreach (var item in tagList)
                    {
                        if (item.Type == TagType.TIMETAG)
                        {
                            tagView.AddTag(item.Type, item.StartTime, true, item.Comment);
                            tagView.AddTag(item.Type, item.EndTime, false, item.Comment);
                        }
                        else
                        {
                            tagView.AddTag(item.Type, item.StartTime, false, item.Comment);
                        }
                    }
                }

                //시뮬레이션 데이터의 값으로 타임바를 초기화한다. 
                var startTime = sharedData.OffSimInfo.SimulInfo.GetStartTime();
                SetPlayTime(startTime, sharedData.OffSimInfo.SimulInfo.mPlayTime, 0);
                tlTimebar.Visible = true;

                //파라미터 요청을 한다. 
                Task.Run(() => {
                    (bool isResult, ResultCode resultCode) = MainController.OnPamraterListReq();

                    //파라미터 요청 결과로 메인창의 Play/Stop 버튼의 활성화를 결정한다. 
                    if (isResult && resultCode == ResultCode.OK)
                    {
                        //파라미터 툴 활성화.
                        DataToolButtonUIChange(true);
                        RecViewButtonUIChange(true);
                        //Play/Stop 버튼 활성화 호출
                        OnPlayStopButtonChange(MethodBase.GetCurrentMethod().Name, SharedData.GetInstance().PlayMode);
                        OnPlayStopEnable(MasterParamList.GetInstance().IsValidBothBuffer());
                    }
                    else
                    {
                        DataToolButtonUIChange(false);
                        RecViewButtonUIChange(false);
                        OnPlayStopEnable(false);
                    }

                });

                SetUIChangeByPlayMode(sharedData.DataSourceType);
                UpdateDataSourceLabel();    
            }
            else
            {
                //메인 UI의 데이터소스 버튼 Enable 시킨다.
                DataSourceButtonUIChange(true);

                OnPlayStopButtonChange(MethodBase.GetCurrentMethod().Name, SharedData.GetInstance().PlayMode);
                OnPlayStopEnable(true);
            }

            dataSourceSelectView = null;
            IsDataSourceViewShow = false;
        }        

        /// <summary>
        /// 기존 UDP Server 객체를 삭제하고 다시 생성한다. 포트가 사용중인 경우 기다린다. 
        /// </summary>
        private void ResetUdpConnection()
        {
            //[mackenzie 2020/9/25, 데이터 소스 변경시 Udp 연결을 재설정한다.]
            //UDP 객체에서 수신 문제시 "SocketUdpServer_UDPDataLostEvent"로 전달되며, 이 메서드에서 플래그를 설정한다.  
            if (UdpServerLostFlag)
            {
                //UDP 연결 해제 및 Dispose
                MainController.UdpServerDispose();

                while (true)
                {
                    if (Utils.PortInUse(ConstantVal.UpdPortNum))
                    {
                        strFormatter.Clear();
                        strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), global::nDA.MetaData.Properties.Resources.InfoPortBusy);

#if (DEBUG)
                        CDebug.WriteTrace($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {strFormatter}");
#endif
                        FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 1);
                    }
                    else
                    {
                        strFormatter.Clear();
                        strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), global::nDA.MetaData.Properties.Resources.InfoSuccessConPort);

#if (DEBUG)
                        CDebug.WriteTrace($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {strFormatter}");
#endif
                        FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 1);

                        MainController.UdpServerInit();

                        break;
                    }

                    Utils.DelayLogic(500);
                }
            }
        }

        #endregion


        #region [ 메인제어창 UI 관련 기능]
        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            if (Settings.Default.WindowMagneticEnable)
            {
                var mainView = (sender as nDAMainView);
                Point p = mainView.Location;
                p.X += mainView.Size.Width;
                if (analysisWindowManager != null)
                {
                    analysisWindowManager.Location = p;
                    analysisWindowManager.BringToFront();
                }
            }
        }

        /// <summary>
        /// 마우스 왼쪽 클릭: 메인 로고 이미지 클릭시 UI의 TopMost 설정 토글 
        /// 마우스 오른쪽 클릭: 메인 로고 이미지 클릭시 UI를 축소 또는 확대 토글
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PanLogo_Click(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (this.TopMost)
                {
                    this.TopMost = false;
                    ShowTootTip("Release Main Menu always top");
                }
                else
                {
                    this.TopMost = true;
                    ShowTootTip("Set Main Menu always top");
                }
            }

            if (e.Button == MouseButtons.Right)
            {
                //메인 확대
                if (MetaData.ConstantValues.IsMainMinimize)
                {
                    MetaData.ConstantValues.IsMainMinimize = false;
                    TableLayoutPanelExtensions.ShowCols(tlMain, new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
                    this.Width = originalWith;

                    ShowTootTip("Maximized Main Menu");
                }
                else //메인 축소
                {
                    MetaData.ConstantValues.IsMainMinimize = true;
                    originalWith = Width;
                    TableLayoutPanelExtensions.HideCols(tlMain, new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

                    //UI를 축소하는 크기
                    this.Width = (int)Math.Ceiling(tlMain.ColumnStyles[0].Width + 3);

                    ShowTootTip("Minimized Main Menu");
                }
            }

            //Thread thread = new Thread(() =>
            //{
            //    AwTestShaped10 awForm = new AwTestShaped10();
            //    awForm.Show();
            //    Application.Run(awForm);
            //});

            //thread.SetApartmentState(ApartmentState.STA);
            //thread.Start();
        }

        /// <summary>
        /// 현재 메인 UI 버튼 상태 및 시뮬레이션의 Start/Stop을 컨트롤한다. 
        /// 가능한 상태는 다음과 같다. 
        /// Case #1(재생중):    Enable, Start
        /// Case #2(정지중):    Enable, Stop
        /// Case #3(사용불가):  Disable, Stop
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnPlayStop_Click(object sender, EventArgs e)
        {
            MainController.OnPlayStopControl();
        }

        #region [ 플레이 버튼 활성화 ] 

        /// <summary>
        /// 분석창에 메인창의 플레이 상태 변경 이벤트 전달
        /// </summary>
        public void InvokeAwPlayStateChange()
        {
            if (SharedData.GetInstance().AnalysisWindowSharedData.Count != 0)
            {
                OnChangedMainPlayStatus.Invoke();
            }
        }

        /// <summary>
        /// 메인 Play/Stop UI 변경하는 토글 메서드
        /// Play -> Stop, Stop -> Play
        /// </summary>
        /// <param name="pMode"></param>
        /// <param name="isEnable"></param>
        public void OnPlayStopButtonChange(string sender, PlayMode pMode)
        {
            try
            {
                var sharedData = SharedData.GetInstance();
                var targetImg = (pMode == PlayMode.PLAY) ? Resources.mainStop_N : Resources.mainPlay_N;
                var targetTxt = (pMode == PlayMode.PLAY) ? "STOP" : "PLAY";

                if (cmdButtonPlayStop.InvokeRequired)
                {
                    cmdButtonPlayStop.Invoke(new MethodInvoker(() =>
                    {
                        cmdButtonPlayStop.Image = targetImg;
                        cmdButtonPlayStop.Text = targetTxt;
                    }));
                }
                else
                {
                    cmdButtonPlayStop.Image = targetImg;
                    cmdButtonPlayStop.Text = targetTxt;
                }
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        /// <summary>
        /// 버튼의 활성화 비활성화 변경
        /// </summary>
        /// <param name="isEnable"></param>
        public void OnPlayStopEnable(bool isEnable)
        {
            try
            {
                //데이터 소스 셀렉터가 실행중인 경우, UI Disable
                if (Application.OpenForms[global::nDA.MetaData.Properties.Resources.ClassDataSource] is DataSourceSelectView srcSelector)
                {
                    isEnable = false;
                }

                //파라미터를 관리하는 두 버퍼 중 하나라도 갯수가 0 이면 UI Disable
                if (MasterParamList.GetInstance().IsEmptyBothParamBuffer())
                {
                    isEnable = false;
                }

                //리플레이인 상태에서 플레이 상태가 Stop인 경우 UI Disable
                if (SharedData.GetInstance().DataSourceType == PlayDataSource.REPLAY && SharedData.GetInstance().PlayMode == PlayMode.STOP)
                {
                    isEnable = false;
                }

                var sharedData = SharedData.GetInstance();

                var txtColor = isEnable ? SystemColors.ControlText : Color.FromArgb(58, 58, 59);
                var borderColor = isEnable ? MetaData.ConstantValues.CMAINPLAY_TXTENTER : MetaData.ConstantValues.CMAINPLAY_TXTLEAVE;

                if (cmdButtonPlayStop.InvokeRequired)
                {
                    cmdButtonPlayStop.Invoke(new MethodInvoker(() =>
                    {
                        cmdButtonPlayStop.Enabled = isEnable;

                        if (isEnable)
                        {
                            cmdButtonPlayStop.BackColor = Color.Transparent;
                            //cmdButtonPlayStop.ButtonElement.TextElement.ForeColor = MetaData.CommonColorValues.CMAINPLAY_TXTNORMAL;
                            cmdButtonPlayStop.ButtonElement.Opacity = 1.0;
                        }
                        else
                        {
                            cmdButtonPlayStop.BackColor = Color.Transparent;
                            cmdButtonPlayStop.ButtonElement.ShowBorder = isEnable;
                            cmdButtonPlayStop.ButtonElement.Opacity = 0.2;
                        }

                    }));
                }
                else
                {
                    cmdButtonPlayStop.Enabled = isEnable;

                    if (isEnable)
                    {
                        cmdButtonPlayStop.BackColor = Color.Transparent;
                        cmdButtonPlayStop.ButtonElement.TextElement.ForeColor = MetaData.ConstantValues.CMAINPLAY_TXTNORMAL;
                        cmdButtonPlayStop.ButtonElement.Opacity = 1.0;
                    }
                    else
                    {
                        cmdButtonPlayStop.BackColor = Color.Transparent;
                        cmdButtonPlayStop.ButtonElement.ShowBorder = isEnable;
                        //cmdButtonPlayStop.ButtonElement.TextElement.ForeColor = Color.Gray;
                        cmdButtonPlayStop.ButtonElement.Opacity = 0.2;
                    }
                }
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        /// <summary>
        /// 분석창이 닫힐때, 공유 데이터 및 UI 처리 변경 처리
        /// </summary>
        /// <param name="uid"></param>
        internal void CheckAwStatus(string uid)
        {
            //분석창이 닫히면, SharedData내에 관리 버퍼내에 분석창 정보를 삭제해 준다. 
            SharedData.GetInstance().RemoveActiveAnalysisWindowDic(uid);

            //닫히는 분석창 정보로, 남아있는 분석창의 갯수가 0인 경우 UI를 변경한다. 
            //시뮬레이션이 재생중이라면, 시뮬레이션 종료 요청함.
            if (SharedData.GetInstance().ActiveAnalysisWindowDic.ContainsKey(uid))
            {
                if (SharedData.GetInstance().ActiveAnalysisWindowDic.Count == 0)
                {
                    //종료 상태가 아닌경우에만 처리한다. 
                    if (!SharedData.GetInstance().onTimeClosingFlag)
                    {
                        //플레이 상태
                        if (SharedData.GetInstance().PlayMode == PlayMode.PLAY)
                        {
                            if (!SharedData.GetInstance().onTimeClosingFlag)
                            {
                                MainController.OnPlayStopControl();
                                OnPlayStopButtonChange(MethodBase.GetCurrentMethod().Name, SharedData.GetInstance().PlayMode);
                                OnPlayStopEnable(false);
                            }
                        }
                        else //스탑 상태
                        {
                            OnPlayStopButtonChange(MethodBase.GetCurrentMethod().Name, SharedData.GetInstance().PlayMode);
                            OnPlayStopEnable(false);
                        }
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// 데이터소스 버튼의 UI의 Enable/Disable을 변경한다. 
        /// </summary>
        /// <param name="isEnable"></param>
        public void DataSourceButtonUIChange(bool isEnable)
        {
            if (labSourceTime.InvokeRequired)
            {
                labSourceTime.Invoke(new MethodInvoker(() =>
                {
                    labSourceTime.Enabled = isEnable;

                    if (isEnable)
                    {
                        labSourceTime.ForeColor = MetaData.ConstantValues.CMAINSRC_TXTNORMAL;
                    }
                    else
                    {
                        labSourceTime.ForeColor = MetaData.ConstantValues.CMAINSRC_TXTDISABLE;
                    }
                }));
            }
            else
            {
                labSourceTime.Enabled = isEnable;
                if (isEnable)
                {
                    labSourceTime.ForeColor = MetaData.ConstantValues.CMAINSRC_TXTNORMAL;
                }
                else
                {
                    labSourceTime.ForeColor = MetaData.ConstantValues.CMAINSRC_TXTDISABLE;
                }
            }
        }

        #region [ 로그아웃 및 종료]
        /// <summary>
        /// 이벤트 태그가 있을 경우 저장하는 로직
        /// </summary>
        private void CheckDataSave(bool isAuto = false)
        {
            var sharedData = SharedData.GetInstance();
            //=======================
            //이벤트 태그를 저장함.
            bool isSave = false;

            //태그가 있고, 사용자가 수동 저장하지 않은경우에만 저장여부를 물어보고 진행함.
            if (tagView.TagCommentInfos.Count > 0 && !tagView.IsSaved)
            {
                //오프라인 모드가 아닌상태에서 자동저장이 비활성화 되어 있는 경우, 저장 여부를 물어봄.
                if (sharedData.DataSourceType != PlayDataSource.OFFLINE && !UserAutoSaveInfo.GetInstance().IsAutoSave)
                {
                    if (isAuto && UserAutoSaveInfo.GetInstance().IsAutoSave)
                    {
                        isSave = true;
                    }
                    else
                    {
                        if (!MessageDlg.GetInstance().Visible)
                        {
                            MessageDlg.GetInstance().SetInformation(global::nDA.MetaData.Properties.Resources.TitleInfo, global::nDA.MetaData.Properties.Resources.InfoSaveMarker, BoxType.YN);
                            if (MessageDlg.GetInstance().ShowDialog() == DialogResult.OK)
                            {
                                isSave = true;
                            }
                        }
                    }
                }
                else
                {
                    isSave = true;
                }
            }

            if (isSave && sharedData.DataSourceType != PlayDataSource.OFFLINE)
            {
                tagView.SaveTagEventListToDB();
            }
        }

        /// <summary>
        /// 분석창 종료 및 조건 체크
        /// 1개의 분석창: 닫거나/않닫거나가 수신
        /// 2개의 분석창: (모두 닫는 경우(true)-계속종료), (모두 않닫는 경우(false)-종료중지), (두개 중 1개만 닫는 경우-종료중지)
        /// </summary>
        /// <returns></returns>
        private bool DoCloseAnalysisWindow()
        {
            var sharedData = SharedData.GetInstance();

            try
            {
                //분석창 키 목록을 리스트에 따로 저장해 두어야 분석창에서 닫아도 목록을 유지할 수 있다. 
                List<string> before_awKeys = new List<string>(sharedData.ActiveAnalysisWindowDic.Keys);
                
                //저장해둔 분석창 모두를 종료
                foreach (var key in before_awKeys)
                {
                    //// 자동 설정에 의해 종료되는경우, 저장중인 파라미터 레코더를 강제 종료 한다. 
                    //if (sharedData.AutoClose)
                    //{
                    //    (sharedData.ActiveAnalysisWindowDic[key] as AnalysisWindowView).OnParameterRecordComponentPlayStop();
                    //}

                    (sharedData.ActiveAnalysisWindowDic[key] as AnalysisWindowView).Terminate();
                }

                //분석창의 저장 및 취소에 대한 사용자 결정 후 갯수 
                List<string> after_awKeys = new List<string>(sharedData.ActiveAnalysisWindowDic.Keys);
                if (after_awKeys == null) return true;
                if (after_awKeys.Count > 0) return false;
                else return true;
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }

            return true;
        }

        private bool SubFormClose()
        {
            var sharedData = SharedData.GetInstance();
            if (!sharedData.AutoClose)
            {
                //파라미터 레코드 중인 경우 종료하지 않는다.
                if(recView?.RecoderSaveStatus == SaveStatus.RECORDING)
                {
                    if (!MessageDlg.GetInstance().Visible)
                    {
                        MessageDlg.GetInstance().SetWarning(global::nDA.MetaData.Properties.Resources.TitleWarning, "Data is being recorded!\r\nAre you sure you want to quit?", BoxType.YN);
                        if (MessageDlg.GetInstance().ShowDialog() == DialogResult.OK)
                        {
                            if (recView.InvokeRequired)
                                recView.Invoke(new MethodInvoker(() => { recView.btnStartStop.PerformClick(); }));
                            else
                                recView.btnStartStop.PerformClick();                         
                        }
                        else
                            return false;
                    }
                    else
                        return false;                   
                }                    
            }
            //분석창의 모든 창이 종료한 경우에 진입 가능
            if (DoCloseAnalysisWindow())
            {
                sharedData.onTimeClosingFlag = true;

                try
                {
                    //차트 도구모음 종료
                    if (ObjectBrowserForm != null)
                    {
                        if (ObjectBrowserForm.InvokeRequired)
                            ObjectBrowserForm.Invoke(new MethodInvoker(() => { ObjectBrowserForm.Close(); }));
                        else
                            ObjectBrowserForm?.Close();
                    }
                    //파라미터 도구모음 종료
                    if (DataToolView != null)
                    {
                        if (DataToolView.InvokeRequired)
                            DataToolView.Invoke(new MethodInvoker(() => { DataToolView.Close(); }));
                        else
                            DataToolView.Close();
                    }
                    //레코더 종료
                    if (recView != null)
                    {
                        if (recView.InvokeRequired)
                            recView.Invoke(new MethodInvoker(() => { recView.Close(); }));
                        else
                            recView.Close();
                    }
                    //리소스 선택창 종료
                    if (dataSourceSelectView != null)
                    {
                        dataSourceSelectView.FormClosed -= DataSourceManagerForm_Closed;
                        dataSourceSelectView.Close();
                        dataSourceSelectView = null;
                    }
                    //분석창 메니저 종료
                    if (analysisWindowManager != null)
                    {
                        if (analysisWindowManager.InvokeRequired)
                            analysisWindowManager.Invoke(new MethodInvoker(() => { analysisWindowManager.Close(); }));
                        else
                            analysisWindowManager.Close();
                    }                
                    //시스템모니터 종료
                    if (SystemMonitoringForm != null)
                    {
                        if (SystemMonitoringForm.InvokeRequired)
                            SystemMonitoringForm.Invoke(new MethodInvoker(() => { SystemMonitoringForm.Close(); }));
                        else
                            SystemMonitoringForm.Close();
                    }
                    //셋팅창 종료
                    if (settingView != null)
                    {
                        if (settingView.InvokeRequired)
                            settingView.Invoke(new MethodInvoker(() => { settingView.Close(); }));
                        else
                            settingView.Close();
                    }     
                    // 유저 메니저 종료
                    if (UserManagerForm != null)
                    {
                        if (UserManagerForm.InvokeRequired)
                            UserManagerForm.Invoke(new MethodInvoker(() => { UserManagerForm.Close(); }));
                        else
                            UserManagerForm.Close();
                    }
                    //스트립차트 태그뷰어 종료
                    if (tagView != null)
                    {
                        if (tagView.InvokeRequired)
                            tagView.Invoke(new MethodInvoker(() => { tagView.Close(); }));
                        else
                            tagView.Close();
                    }
                }
                catch (Exception ex)
                {
                    strFormatter.Clear();
                    strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                    CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                    FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
                }

                return true;

            }
            else //종료되지않은 분석창이 하나라도 남아있는 경우
            {
                return false;
            }
        }


        private void BtnLogout_Click(object sender, EventArgs e)
        {

            List<string> before_awKeys = new List<string>(SharedData.GetInstance().ActiveAnalysisWindowDic.Keys);
            //저장해둔 분석창 모두를 종료
            foreach (var key in before_awKeys)
            {
                if (SharedData.GetInstance().GetAnalysisWindowPlayMode(key) == PlayMode.PLAY)
                {
                    (SharedData.GetInstance().ActiveAnalysisWindowDic[key] as AnalysisWindowView).OnStartRequestFromMain();
                }
            }

            //메세지박스가 팝업되어있는 상태인 경우 로그아웃을 취소한다. 
            if (MessageDlg.GetInstance().Visible)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), global::nDA.MetaData.Properties.Resources.InfoMessageBoxLimit);
#if DEBUG
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 1);
                return;
            }

            LogOut();
        }

        //[mackenzie 2020/04/01 UDP 연결 문제시 자동 로그아웃 기능 구현]
        public bool LogOut(bool isAuto = false)
        {
            string userID = SharedData.GetInstance().CurrentUser == null ? string.Empty : SharedData.GetInstance().CurrentUser.UserID;
            //사용자에 의해 종료가 선택한 것을 저장해 놓는 플래그
            SharedData.GetInstance().onTimeClosingFlag = true;

            //이벤트 태그 저장
            CheckDataSave(isAuto);

            if (!SubFormClose())
            {
                //종료 절차가 취소됐으므로 플래그를 디폴트로 복원
                SharedData.GetInstance().onTimeClosingFlag = false;
                return false;
            }

            try
            {
                //이곳에 로그아웃 요청 메시지 루틴을 삽입.
                if (SharedData.GetInstance().DataSourceType != PlayDataSource.OFFLINE)
                {
                    DBUpdateUserLog.GetInstance().LogOutUserLogQry(userID);
                    MainController.LogOut(userID);
                    MainController.SetLogoutFlag(true);
                }
                else
                {
                    LocalReplay.GetInst().Dispose();
                }

                SharedData.GetInstance().Reset();

                MainController.Dispose();

                ApplicationRestart(isAuto);
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if DEBUG
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }

            return true;
        }

        /// <summary>
        /// 어플리케이션 재시작 메서드
        /// </summary>
        public void ApplicationRestart(bool isAuto = false)
        {
            try
            {
                CDebug.WriteTrace($"Project Name: {Assembly.GetEntryAssembly().GetName().Name}");
                Process[] procHQS = Process.GetProcessesByName(Assembly.GetEntryAssembly().GetName().Name);

                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"Current Process PID : {procHQS[0].Id}");
                CDebug.WriteTrace($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {strFormatter}");
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 1);

                //Message Loop를 모두 처리하지 않고, 현재 thread를 종료후 모든 창을 닫음, FormClosing 을 호출하지 않는점
                Application.ExitThread();

                //프로그램을 재실행하여 로그인 화면으로 전환한다.
                Application.Restart();
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        private void nDAMainView_FormClosing(object sender, FormClosingEventArgs e)
        {

            try
            {
                //Utils.ReleaseMultiMediaTimer();

                //성능측정용 타이머 모두 종료
                ThroughputRateMonitor.GetInstance().StopAll();
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        private void nDAMainView_FormClosed(object sender, FormClosedEventArgs e)
        {
            try
            {

            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        #endregion

        #endregion

        #endregion

        #region [ 클래스 Dispose 패턴 구현 - inherited Form]

        private bool disposed;

        /// <summary>
        /// 모든 비관리/관리 리소스 정리
        /// GC.SuppressFinalize(this)를 호출해 finalizer 호출 회피(Finalizer 호출은 성능상 손해가 있을 수 있기 때문)
        /// 폼은 .Designer 파일에 Dispose가 구현되어있고 자동으로 불리지만, 아래와 같이 override하여 사용자 변수와
        /// base.Dispose()를 하여 .Designer의 종료 절차도 호출하도록한다. 
        /// </summary>
        /// <param name="disposing"></param>
        internal void OnDispose(bool disposing)
        {
            //이미 한번 Dispose인 경우 다시 로직을 수행하지 않도록하는 플래그 체크
            if (disposed) return;

            if (disposing)
            {
                //--------------------------------------------------------
                // 이 인스턴스가 소유한 Managed 자원을 해제하는 부분
                //--------------------------------------------------------

                try
                {
                    //analysisWindowManager?.Close();
                    //analysisWindowManager?.Dispose();
                    //analysisWindowManager = null;

                    UserManagerForm?.Close();
                    UserManagerForm?.Dispose();
                    UserManagerForm = null;

                    if (timeoutTimer != null)
                    {
                        timeoutTimer.Enabled = false;
                        timeoutTimer.Stop();
                        timeoutTimer.Dispose();
                        timeoutTimer = null;
                    }

                    keyboardEventListener?.Dispose();
                    keyboardEventListener = null;

                    MainController?.Dispose();
                    MainController = null;

                    ObjectBrowserForm?.Dispose();
                    ObjectBrowserForm = null;

                    DataToolView?.Dispose();
                    DataToolView = null;

                    settingView?.Dispose();
                    settingView = null;

                    dataSourceSelectView?.Dispose();
                    dataSourceSelectView = null;

                    SystemMonitoringForm?.Dispose();
                    SystemMonitoringForm = null;

                    analysisWindowManager?.Dispose();
                    analysisWindowManager = null;

                    tagView?.Dispose();
                    tagView = null;

                    _toolTip?.Dispose();
                    _toolTip = null;

                    UdpDataReceiveStateTimer?.Dispose();
                    UdpDataReceiveStateTimer = null;

                    //※ 공통 적용 사항 - 상속받은 클래스 "CommondisplayComponent" Dispose
                    if (!base.IsDisposed)
                    {
                        if (base.InvokeRequired)
                        {
                            base.Invoke(new MethodInvoker(() =>
                            {
                                base.Dispose(disposing);
                            }));
                        }
                        else
                        {
                            base.Dispose(disposing);
                        }
                    }

                    strFormatter.Clear();
                    strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), global::nDA.MetaData.Properties.Resources.InfoDisposeOk);
#if (DEBUG)
                    CDebug.WriteTrace($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {strFormatter}");
#endif
                    FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 1);
                }
                catch (Exception ex)
                {
                    strFormatter.Clear();
                    strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                    CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                    FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
                }
                finally
                {
                    disposed = true;
                }
            }

            //--------------------------------------------------------------
            // 이 인스턴스가 소유한 Unmanaged 자원을 해제하는 부분
            //--------------------------------------------------------------
        }

        #endregion

        #region [환경설정 이벤트 로직]

        #region [ 시스템 모니터링 화면 ]
        private void RBtnSystemMonitor_Click(object sender, EventArgs e)
        {
            if (SystemMonitoringForm == null)
            {
                SystemMonitoringForm = new SystemMonitoringView();
                SystemMonitoringForm.FormClosed += SystemMonitoringForm_FormClosed;
                SystemMonitoringForm.Show();
            }
            else
            {
                SystemMonitoringForm.WindowState = FormWindowState.Normal;
                SystemMonitoringForm.BringToFront();
            }
        }

        private void SystemMonitoringForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (SystemMonitoringForm != null)
            {
                SystemMonitoringForm = null;
            }
        }
        #endregion

        #region [ 사용자 정보 화면 설정 기능 ] 
        private void RBtnUserManager_Click(object sender, EventArgs e)
        {
            if (UserManagerForm == null)
            {
                UserManagerForm = new UserManagerView();
                UserManagerForm.FormClosed += UserManagerForm_FormClosed;
                UserManagerForm.Show();
            }
            else
            {
                UserManagerForm.WindowState = FormWindowState.Normal;
                UserManagerForm.BringToFront();
            }
        }

        private void UserManagerForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            UserManagerForm.FormClosed -= UserManagerForm_FormClosed;

            UserManagerForm?.Dispose();
            UserManagerForm = null;
        }
        #endregion

        #region [ 관리자 환경설정화면]
        private void RBtnLimitSetting_Click(object sender, EventArgs e)
        {
            if (settingView == null)
            {
                settingView = new SettingView();
                settingView.FormClosed += ControlLimitSettingForm_FormClosed;
                settingView.StartPosition = FormStartPosition.CenterScreen;
                settingView.ShowDialog();
            }
            else
            {
                settingView.WindowState = FormWindowState.Normal;
                settingView.BringToFront();
            }
            CheckAutoLogout();
        }
        private void ControlLimitSettingForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            settingView = null;
        }
        #endregion
        #endregion

        #endregion

        #region [ 기타 기능 로직 ]


        private string CurrentSrcName = string.Empty;

        /// <summary>
        /// 메인창 하단의 데이터 소스 이름을 변경하는 메서드
        /// </summary>
        private void UpdateDataSourceLabel()
        {
            switch (SharedData.GetInstance().DataSourceType)
            {
                case PlayDataSource.REALTIME:                    
                    CurrentSrcName = string.Format(CultureInfo.CreateSpecificCulture("en-US"), "Realtime");
                    break;
                case PlayDataSource.REPLAY:
                    CurrentSrcName = string.Format(CultureInfo.CreateSpecificCulture("en-US"), "Playback");
                    break;
                case PlayDataSource.OFFLINE:
                    CurrentSrcName = string.Format(CultureInfo.CreateSpecificCulture("en-US"), "Offline");
                    break;
            }
        }

        #region [ 사용자 키 이벤트 처리 기능]

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.K))
            {
                OnDisplaySystemMonitoring();
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.A))
            {
                OpenDeveloperOptions();
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.T))
            {
                //쓰루풋 뷰어 다이얼로그 표시
                ThroughputView thViewDlg = new ThroughputView();
                thViewDlg.ShowDialog();
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.D))
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                isDebug = !isDebug;
                if (isDebug)
                {
                    Text = string.Format(CultureInfo.CreateSpecificCulture("en-US"), global::nDA.MetaData.Properties.Resources.SwName + "SW v.{0}.{1}.{2}", version.Major, version.Minor, version.Build);
                }
                else
                {
                    Text = string.Format(CultureInfo.CreateSpecificCulture("en-US"), global::nDA.MetaData.Properties.Resources.SwName + "SW v.{0}.{1}", version.Major, version.Minor);
                }
            }
            if (keyData == (Keys.Control | Keys.D)) // 개발자 모드
            {
                //do nothing
            }
            if (keyData == (Keys.Control | Keys.P))
            {

            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static void OnDisplaySystemMonitoring()
        {
            SharedData.ToString();
        }

        private static void OpenDeveloperOptions()
        {
            DeveloperOptions form = new DeveloperOptions();
            form.Show();
        }
        #endregion
        #endregion

        #region [ 클래스 이벤트 처리 ]

        /// <summary>
        /// 메인 UI에 UDP 연결 상태를 2초에 한번씩 점검하여 표시를 제어하는 메서드
        /// </summary>
        private void UdpConnectionStateChecker()
        {
            try
            {
                //프로그램이 종료 시퀀스중인 경우, 체크하지 않는다. 
                if (!SharedData.GetInstance().onTimeClosingFlag)
                {
                    //UDP 수신이 문제가 없는 경우
                    if (ThroughputRateMonitor.GetInstance().IsUDPDataReceving)
                    {
                        //현재 데이터 상태 정보를 얻는다.
                        //stop 상태는 서버에서 보내지 않는다.
                        DataStatusInfo StateSim = (DataStatusInfo)SimulationBufferManager.GetInstance().GetLastDataStatus();

                        switch (SharedData.GetInstance().PlayMode)
                        {
                            case PlayMode.PLAY:
                                switch (StateSim)
                                {
                                    case DataStatusInfo.FREEZE:
                                    case DataStatusInfo.STALBLE:
                                    case DataStatusInfo.RUN:
                                        //UDP 수신률 상태바 레이블에 표시
                                        Utils.UpdateMsLabel(labUDPState, strFpsFormatter.ToString(), MetaData.ConstantValues.CMAINUDP_TXTNORMAL);
                                        break;
                                    case DataStatusInfo.KILL:   //오퍼레이터가 kill 을 누루면 전송한다. 데이터의 저장은  init부터 kill 이 정상 적으로 도착할때이다. 
                                    case DataStatusInfo.HALT:   //패킷 지연시 발생                                                       
                                    default:
                                        Utils.UpdateMsLabel(labUDPState, DataStatusInfo.STOP.ToString(), MetaData.ConstantValues.CMAINUDP_TXTFAULT);
                                        break;
                                }
                                break;
                            case PlayMode.STOP:                                
                                Utils.UpdateMsLabel(labUDPState, DataStatusInfo.STOP.ToString(), MetaData.ConstantValues.CMAINUDP_TXTINACTIVE);
                                break;
                            case PlayMode.NONE:
                            case PlayMode.PLAYBACK:
                            case PlayMode.PAUSE:
                            default:
                                Utils.UpdateMsLabel(labUDPState, "UDP [ 0 ] Hz", MetaData.ConstantValues.CMAINUDP_TXTNORMAL);
                                break;
                        }

                        if (StateSim == DataStatusInfo.KILL)
                        {
                            //온라인 리플레이에서 스테이터스를 움직이는 경우, 
                            MainController.SimulationOffsetTime = 0;
                        }
                    }
                    else
                    {
                        Utils.UpdateMsLabel(labUDPState, "UDP [ 0 ] Hz", MetaData.ConstantValues.CMAINUDP_TXTNORMAL);
                    }
                }
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        /// <summary>
        /// 외부 함수에서 labUDPState 컨트롤에 표시 기능을 제공하기 위한 메서드
        /// </summary>
        internal void SetUdpLabelUI(string msg, Color color)
        {
            Utils.UpdateMsLabel(labUDPState, msg, color);
        }


#if (DEBUG)
        /// <summary>
        /// TCP 네트워크 연결이 안되었을때 이를 표시하기 위한 기능 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="value"></param>
        private void OnTcpConnectionStatusUpdate(object sender, object value)
        {
            var t = new Task(() =>
            {
                Thread.Sleep(5000);
                Utils.UpdateMsLabel(labSourceTime, "IP", MetaData.ConstantValues.CMAINUDP_TXTFAULT);
            }
            );
            t.Start();
        }
#endif

        /// <summary>
        /// 파라미터로 전달 받은 레이블 컨트롤에 전달받은 문자열을 합하여 UI에 표시하는 메서드
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="elapseTime"></param>
        public void UpdateSrcTimeLabel(object sender, string elapseTime)
        {
            try
            {
                if (sender != null)
                {
                    var item = (RadLabel)sender;

                    if (item != null)
                    {                        
                        if (item.InvokeRequired)
                        {
                            item.Invoke(new MethodInvoker(() =>
                            {                                
                                item.Text = string.Format("{0}   {1}", CurrentSrcName, elapseTime);
                            }));
                        }
                        else
                        {                            
                            item.Text = string.Format("{0}   {1}", CurrentSrcName, elapseTime);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)           
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), "Utils", MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        #endregion

        #region [ 타이머 관련 로직 ] 

        /// <summary>
        /// UDP 속도를 주기적으로 표시하는데 사용하는 타이머 초기화 및 시작
        /// </summary>
        private void InitUdpCheckTimer()
        {
            UdpDataReceiveStateTimer = new System.Windows.Forms.Timer
            {
                Interval = TM_LABEL_MAX
            };

            UdpDataReceiveStateTimer.Tick += DoUpdateUdpState;
            UdpDataReceiveStateTimer.Enabled = true;

            UdpDataReceiveStateTimer.Start();
        }

        /// <summary>
        /// 플레이시간 업데이트용 타이머 초기화
        /// </summary>
        private void InitClockTimer()
        {
            TimerClockUpdater = new System.Windows.Forms.Timer
            {
                Interval = TM_CLOCK_MAX
            };

            TimerClockUpdater.Tick += DoUpdatePlayTime;
            TimerClockUpdater.Enabled = true;
        }


        /// <summary>
        /// UDP 수신 상태 업데이트용 타이머 아벤트 처리
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DoUpdateUdpState(object sender, EventArgs e)
        {
            strFpsFormatter.Clear();
            strFpsFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), "UDP [ {0:0.0#} ] Hz", ThroughputRateMonitor.GetInstance().UDPRate);
            UdpConnectionStateChecker();
        }

        /// <summary>
        /// 플레이시간 업데이트용 타이머 아벤트 처리
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DoUpdatePlayTime(object sender, EventArgs e)
        {
            var currTime = SimulationBufferManager.GetInstance().LastServerTime * 1000;

            if (SharedData.GetInstance().DataSourceType != PlayDataSource.REALTIME)
            {
                if (SharedData.GetInstance().PlayMode == PlayMode.PLAY)
                {
                    UpdatePlayTimeProgress();
                }
            }

            UpdateSrcTimeLabel(labSourceTime, TimeSpan.FromMilliseconds(currTime).ToString("hh' : 'mm' : 'ss", CultureInfo.CreateSpecificCulture("en-US")));

            //재생시간이 8시간 지나면..강제로 자동저장 버튼을 활성화 하고 로그아웃한다. 
            //자동 로그아웃은 비활성화함..(더이상 필요치 않으나 기능 제거에 대하여 확실하지 않으므로 코드는 유지함(20201103 @최동희)
            if (SimulationBufferManager.GetInstance().PlayTimeout)
            {
                if (!SimulationBufferManager.GetInstance().PlayTimeoutEnabled)
                {
                    string msg = $"8시간 경과로 인한 함수 호출";
                    CDebug.WriteTrace(msg, ConsoleColor.Yellow);
                    strFormatter.Clear();
                    strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), msg);
                    FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 1);
                }
                SimulationBufferManager.GetInstance().PlayTimeoutEnabled = true;

            }
        }

        #endregion

        #region [ 플레이백 타임바 로직 ] 

        private DateTime startTime;
        public double startTimeTotalMilliseconds;
        private double offsetTime;
        private bool tbUpdateFlag;

        private void PlaybackBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (SharedData.GetInstance().PlayMode == PlayMode.PLAY)
            {
                tbUpdateFlag = false;
            }
        }

        private void PlaybackBar_MouseUp(object sender, MouseEventArgs e)
        {
            //분석창의 Stop 버튼을 누른다. 
            var sharedData = SharedData.GetInstance().ActiveAnalysisWindowDic;
            if (sharedData != null && sharedData.Count > 0)
            {
                //foreach (var aw in sharedData)
                //{
                //    (aw.Value as AnalysisWindowView).OnParameterRecordComponentPlayStop();
                //}

                //$$ 플레이백바를 이동했을때 레코딩중인 경우는 어떻게 해야할지 결정해야한다. 
                if(recView != null)
                {
                    if (recView.RecoderSaveStatus == SaveStatus.RECORDING)
                    {
                        recView.RecordStopfromExternal();
                    }
                }
            }

            // PlayBack 끝 값을 요청하는 경우, 시작 후 보내줄 데이터가 없으므로 오류를 발생함.
            // 마지막에서 5초 앞의 데이터를 요청하여, 오류 없이 데이터를 요청 할 수 있도록 함.
            var targetSec = PlaybackBar.Value;
            if (PlaybackBar.Value == PlaybackBar.Maximum)
            {
                targetSec -= 5;
                PlaybackBar.Value = targetSec;
            }

            TimeSpan ts = TimeSpan.FromSeconds((double)targetSec);
            var currTime = (int)ts.TotalSeconds;
            tbUpdateFlag = false;


            Utils.UpdateRadLabel(labTbTime, ts.ToString(@"hh\:mm\:ss", CultureInfo.CreateSpecificCulture("en-US")));


            //메인창의 함수를 호출하여 메인 컨트롤러의 "OnRestart()"를 호출한다. 
            if (ResetStreamAndUI(currTime))
            {
                Task.Run(() =>
                {
                    tbUpdateFlag = true;

                    SimulationBufferManager.GetInstance().Reset();
                    RawDataParser.GetInstance().InnerBufferClear();

                    //모든 분석창의 "OnReset(true)" 을 호출
                    foreach (var kv in SharedData.GetInstance().ActiveAnalysisWindowDic)
                    {
                        (kv.Value as AnalysisWindowView).OnReset(true);                        
                    }
                });
            }
            else
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), global::nDA.MetaData.Properties.Resources.ErrResetFail);
#if (DEBUG)
                CDebug.WriteTrace($"{strFormatter} :: {MethodBase.GetCurrentMethod().Name}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 1);
            }
        }

        public void TrackbarUpdateEnable()
        {
            tbUpdateFlag = true;
        }

        public void SetPlayTime(DateTime time, TimeSpan playTime, double offset)
        {
            try
            {
                startTime                   = time;
                DateTime origin             = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                TimeSpan diff               = startTime.ToUniversalTime() - origin;
                startTimeTotalMilliseconds  = diff.TotalMilliseconds;//
                float total_sec             = (float)playTime.TotalSeconds;

                PlaybackBar.Maximum     = (int)total_sec;
                //PlaybackBar.TickFrequency = (int)total_sec / 4;
                radLabel_endTime.Text   = playTime.ToString(@"hh\:mm\:ss", CultureInfo.CreateSpecificCulture("en-US"));
                PlaybackBar.Value       = (int)offset;
                radLabel_midTime.Text   = TimeSpan.FromSeconds(playTime.TotalSeconds / 2).ToString(@"hh\:mm\:ss", CultureInfo.CreateSpecificCulture("en-US"));
                TimeSpan ts             = TimeSpan.FromSeconds(0);
                offsetTime              = (int)ts.TotalSeconds;
                labTbTime.Text          = ts.ToString(@"hh\:mm\:ss", CultureInfo.CreateSpecificCulture("en-US"));
                offsetTime              = offset;

                ResetStreamAndUI(offsetTime);
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)           
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        /// <summary>
        /// 외부에서 플레이 타임을 설정하도록 제공되는 함수
        /// </summary>
        /// <param name="pTime"></param>
        public void SetPlayTimeExt(TimeSpan pTime)
        {
            if (PlaybackBar.InvokeRequired)
            {
                PlaybackBar?.Invoke(new MethodInvoker(() =>
                {
                    SetPlayTime(startTime, pTime, 0);
                }));
            }
            else
            {
                SetPlayTime(startTime, pTime, 0);
            }
        }

        public void UpdatePlayTimeProgress()
        {
            try
            {
                double time = SimulationBufferManager.GetInstance().LastServerTime;
                if (tbUpdateFlag)
                {
                    TimeSpan ts = TimeSpan.FromMilliseconds(time * 1000);
                    if (PlaybackBar.Minimum <= (int)ts.TotalSeconds && PlaybackBar.Maximum >= (int)ts.TotalSeconds)
                    {
                        if (PlaybackBar.InvokeRequired)
                        {
                            PlaybackBar.Invoke(new MethodInvoker(() =>
                            {
                                PlaybackBar.Value = (int)ts.TotalSeconds;
                            }));
                        }
                        else
                        {
                            PlaybackBar.Value = (int)ts.TotalSeconds;
                        }
                    }

                    if (labTbTime != null)
                    {
                        Utils.UpdateRadLabel(labTbTime, ts.ToString(@"hh\:mm\:ss", CultureInfo.CreateSpecificCulture("en-US")));
                    }
                }
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)           
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        public void OnPlackbackReset()
        {
            try
            {
                if (PlaybackBar != null)
                {
                    if (PlaybackBar.InvokeRequired)
                    {
                        PlaybackBar.Invoke(new MethodInvoker(() =>
                        {
                            PlaybackBar.Value = 0;
                        }));
                    }
                    else
                    {
                        PlaybackBar.Value = 0;
                    }
                }

                if (labTbTime != null)
                {
                    offsetTime = 0;
                    TimeSpan ts = TimeSpan.FromMilliseconds(0 * 1000);
                    Utils.UpdateRadLabel(labTbTime, ts.ToString(@"hh\:mm\:ss", CultureInfo.CreateSpecificCulture("en-US")));
                }
            }
            catch (Exception ex)
            {
                strFormatter.Clear();
                strFormatter.AppendFormat(CultureInfo.CreateSpecificCulture("en-US"), $"{ex.Message} :: {ex.StackTrace}");
#if (DEBUG)           
                CDebug.WriteError($"{MethodBase.GetCurrentMethod().DeclaringType.Name} :: {MethodBase.GetCurrentMethod().Name} :: {ex.Message} :: {ex.StackTrace}");
#endif
                FileLogger.GetInst().WriteLogMessage(strFormatter.ToString(), GetType().Name, MethodBase.GetCurrentMethod().Name, 0);
            }
        }

        /// <summary>
        /// Reply에서 PlayBack툴바에서 시간을 이동시켰을때 발생하는 이벤트.
        /// </summary>
        /// <param name="obj"></param>
        public bool ResetStreamAndUI(double playTime)
        {
            bool opResult = false;

            #region [ Description ] 
            /*
             * 1. 재생 중일때
             *    - 분석창에 리셋 명령을 전송
             *    - 시작 명령 전송
             * 2. 재생이 아닐경우
             *    - 오프셋값만 수정
             */
            #endregion

            var sharedData = SharedData.GetInstance();

            if (sharedData.DataSourceType == PlayDataSource.OFFLINE) //오프라인 모드
            {
                if (SharedData.GetInstance().PlayMode == PlayMode.PLAY)
                {
                    cmdButtonPlayStop.PerformClick();

                    if (LocalReplay.GetInst().ChangeOffset(playTime))
                    {
                        cmdButtonPlayStop.PerformClick();
                        OnChangedMainPlayStatus.Invoke();
                        opResult = true;
                    }
                    else
                    {
                        MessageDlg.GetInstance().SetWarning(global::nDA.MetaData.Properties.Resources.TitleWarning, global::nDA.MetaData.Properties.Resources.ErrScrollIndex, BoxType.CLOSE);
                    }
                }
                else
                {
                    //MainController.localReplaySender.ChangeOffset(playTime);
                    LocalReplay.GetInst().ChangeOffset(playTime);
                    opResult = true;
                }


            }
            else //온라인 모드
            {
                MainController.SimulationOffsetTime = playTime;
                if (SharedData.GetInstance().PlayMode == PlayMode.PLAY)
                {
                    var (isSuccess, msg) = MainController.OnRestart();

                    if (isSuccess)
                    {
                        JObject resMsg = JObject.Parse(msg);
                        string rlt_value = resMsg["result_msg"].ToString();

                        if (rlt_value.Equals("OK", StringComparison.CurrentCultureIgnoreCase))
                        {
                            opResult = true;
                        }
                    }
                }
            }

            return opResult;
        }


        #endregion

    }
}
