using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Markup;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName=".NET Framework 4.8")]

namespace XhsNative {
    // Soft text shadows are recorded as static vector drawings, avoiding a
    // shader effect over the entire transparent window.
    class ShadowText : Grid {
        public bool ShadowEnabled, ThemeInk, ThemeMuted, SuppressShadow;
        readonly TextBlock label=new TextBlock();
        readonly Outline outline;
        public ShadowText(){outline=new Outline(this);Children.Add(outline);Children.Add(label);SizeChanged+=delegate{outline.InvalidateVisual();};}
        public string Text {get{return label.Text;}set{label.Text=value;outline.InvalidateVisual();}}
        public double FontSize {get{return label.FontSize;}set{label.FontSize=value;outline.InvalidateVisual();}}
        public FontFamily FontFamily {get{return label.FontFamily;}set{label.FontFamily=value;outline.InvalidateVisual();}}
        public FontWeight FontWeight {get{return label.FontWeight;}set{label.FontWeight=value;outline.InvalidateVisual();}}
        public Brush Foreground {get{return label.Foreground;}set{label.Foreground=value;}}
        public TextWrapping TextWrapping {get{return label.TextWrapping;}set{label.TextWrapping=value;outline.InvalidateVisual();}}
        public TextTrimming TextTrimming {get{return label.TextTrimming;}set{label.TextTrimming=value;outline.InvalidateVisual();}}
        public double LineHeight {get{return label.LineHeight;}set{label.LineHeight=value;outline.InvalidateVisual();}}
        public void RefreshShadow(){outline.InvalidateVisual();}
        class Outline : FrameworkElement {
            readonly ShadowText owner;
            public Outline(ShadowText value){owner=value;IsHitTestVisible=false;}
            protected override void OnRender(DrawingContext dc) {
                if(!owner.ShadowEnabled || ActualWidth<=0 || ActualHeight<=0 || String.IsNullOrEmpty(owner.Text))return;
                var label=owner.label;
                var text=new FormattedText(label.Text,CultureInfo.CurrentUICulture,label.FlowDirection,
                    new Typeface(label.FontFamily,label.FontStyle,label.FontWeight,label.FontStretch),label.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(label).PixelsPerDip);
                text.MaxTextWidth=Math.Max(1,ActualWidth);text.MaxTextHeight=Math.Max(1,ActualHeight);
                text.MaxLineCount=label.TextWrapping==TextWrapping.NoWrap?1:2;text.Trimming=label.TextTrimming;
                text.TextAlignment=label.TextAlignment;if(!Double.IsNaN(label.LineHeight))text.LineHeight=label.LineHeight;
                var geometry=text.BuildGeometry(new Point(0,0));geometry.Freeze();
                var soft=new SolidColorBrush(Color.FromArgb(34,0,0,0));soft.Freeze();
                foreach(var point in new[]{new Point(-1,0),new Point(1,0),new Point(-1,2),new Point(1,2),new Point(0,-1),new Point(0,3)}) {
                    dc.PushTransform(new TranslateTransform(point.X,point.Y));dc.DrawGeometry(soft,null,geometry);dc.Pop();
                }
                dc.PushTransform(new TranslateTransform(0,1));dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(165,0,0,0)),null,geometry);dc.Pop();
            }
        }
    }
    class LineIcon : FrameworkElement {
        public string Kind;
        public bool ShadowEnabled;
        public static readonly DependencyProperty StrokeProperty=DependencyProperty.Register("Stroke",typeof(Brush),typeof(LineIcon),new FrameworkPropertyMetadata(Brushes.White,FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Stroke {get{return (Brush)GetValue(StrokeProperty);}set{SetValue(StrokeProperty,value);}}
        protected override void OnRender(DrawingContext dc) {
            string path=Kind=="refresh"?"M19,8 A8,8 0 1 0 20,16 M19,3 L19,8 L14,8":Kind=="pin"?"M8,3 L16,3 M9,3 L9,9 L6,13 L6,15 L18,15 L18,13 L15,9 L15,3 M12,15 L12,21":Kind=="settings"?"M5,6 L19,6 M5,12 L19,12 M5,18 L19,18":"M7,7 L17,17 M17,7 L7,17";
            var geometry=Geometry.Parse(path);geometry.Freeze();
            dc.PushTransform(new ScaleTransform(ActualWidth/24,ActualHeight/24));
            if(ShadowEnabled){dc.PushTransform(new TranslateTransform(0,1.4));Draw(dc,geometry,new SolidColorBrush(Color.FromArgb(145,0,0,0)),2.4);dc.Pop();}
            Draw(dc,geometry,Stroke,1.55);dc.Pop();
        }
        void Draw(DrawingContext dc,Geometry geometry,Brush brush,double width) {
            var pen=new Pen(brush,width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};pen.Freeze();dc.DrawGeometry(null,pen,geometry);
            if(Kind=="settings")foreach(var p in new[]{new Point(9,6),new Point(15,12),new Point(10,18)})dc.DrawEllipse(null,pen,p,2,2);
        }
    }
    static class Json {
        public static Dictionary<string,object> Read(string file) {
            try { return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file, Encoding.UTF8)); }
            catch { return new Dictionary<string,object>(); }
        }
        public static void Write(string file, object value) {
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
        }
        public static string S(Dictionary<string,object> d, string k, string fallback="") { object v; return d.TryGetValue(k,out v) && v != null ? Convert.ToString(v,CultureInfo.InvariantCulture) : fallback; }
        public static bool B(Dictionary<string,object> d, string k, bool fallback=false) { object v; return d.TryGetValue(k,out v) && v is bool ? (bool)v : fallback; }
        public static double N(Dictionary<string,object> d, string k, double fallback=0) { double n; return Double.TryParse(S(d,k),NumberStyles.Float,CultureInfo.InvariantCulture,out n) && !Double.IsNaN(n) && !Double.IsInfinity(n) ? n : fallback; }
        public static double[] Pair(Dictionary<string,object> d, string k, double[] fallback) {
            object v; if (!d.TryGetValue(k,out v) || !(v is IEnumerable)) return fallback;
            var items = ((IEnumerable)v).Cast<object>().ToArray(); if (items.Length != 2) return fallback;
            double a,b; if (!Double.TryParse(Convert.ToString(items[0],CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out a) || !Double.TryParse(Convert.ToString(items[1],CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out b) || Double.IsNaN(a) || Double.IsInfinity(a) || Double.IsNaN(b) || Double.IsInfinity(b)) return fallback;
            return new double[]{a,b};
        }
    }
    static class Native {
        [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr h,int n);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtr(IntPtr h,int n,IntPtr v);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
        public static Rect WorkArea(Window window) {
            var area=Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea;
            var source=PresentationSource.FromVisual(window);
            var transform=source!=null && source.CompositionTarget!=null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
            return new Rect(transform.Transform(new Point(area.Left,area.Top)),transform.Transform(new Point(area.Right,area.Bottom)));
        }
    }
    class Card : Window {
        readonly string directory, identity, baseDir;
        readonly bool preview, noSync, selfTest;
        Dictionary<string,object> settings;
        List<Dictionary<string,object>> notes = new List<Dictionary<string,object>>();
        readonly Border surface = new Border();
        readonly Grid content = new Grid();
        readonly StackPanel rows = new StackPanel();
        readonly ScrollViewer scroll = new ScrollViewer();
        readonly Ellipse statusDot = new Ellipse{Width=5,Height=5,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,0)};
        ShadowText countLabel, statusLabel, timeLabel, hintLabel;
        Button refreshButton, pinButton, loginButton;
        readonly List<Button> toolbarButtons=new List<Button>();
        Forms.NotifyIcon tray;
        FileSystemWatcher files;
        DispatcherTimer saveTimer, fileTimer, schedule;
        Process worker;
        string currentRun="", state="initializing", message="正在连接创作后台", lastSuccess="", noteFingerprint="";
        bool quitting, paused, wide, pendingRefresh, pendingLogin, sourceReady;
        DateTime nextDue=DateTime.UtcNow;
        int runningCount;
        int layoutEvents, mouseEvents, locationEvents;
        Window settingsWindow;
        public Card(string dir, string id, bool testPreview, bool skipSync, bool qa) {
            directory=dir; identity=id; preview=testPreview; noSync=skipSync; selfTest=qa;
            baseDir=AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(directory);
            settings=Json.Read(Path.Combine(directory,"settings.json"));
            settings["theme"]=Json.S(settings,"theme","glass")=="transparent" ? "transparent" : "glass";
            settings["note_count"]=Math.Max(1,Math.Min(50,(int)Json.N(settings,"note_count",5)));
            Title=preview ? "小红书 · 原生看板预览" : "小红书 · 创作看板";
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
            ShowInTaskbar=preview; ResizeMode=ResizeMode.CanResize; MinWidth=320; MinHeight=260; MaxWidth=2400; MaxHeight=2400;
            double[] size=Json.Pair(settings,"size",new double[]{380,520}); Width=Math.Max(MinWidth,Math.Min(MaxWidth,size[0])); Height=Math.Max(MinHeight,Math.Min(MaxHeight,size[1]));
            double[] pos=Json.Pair(settings,"position",new double[]{SystemParameters.WorkArea.Right-Width-20,SystemParameters.WorkArea.Top+30});
            Left=pos[0]; Top=pos[1]; Topmost=Json.B(settings,"on_top");
            FontFamily=new FontFamily("Segoe UI Variable, Microsoft YaHei UI"); Foreground=Brush("#F5F4F4");
            Build(); ApplyTheme(); LoadSnapshot();
            saveTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(600)};
            saveTimer.Tick+=delegate { saveTimer.Stop(); SaveSettings(); };
            fileTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};
            fileTimer.Tick+=delegate { fileTimer.Stop(); LoadSnapshot(); LoadStatus(); };
            files=new FileSystemWatcher(directory,"*.json"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};
            files.Changed+=FileChanged; files.Created+=FileChanged; files.Renamed+=FileChanged; files.EnableRaisingEvents=true;
            schedule=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};
            schedule.Tick+=delegate { if (!noSync && !paused && !WorkerRunning && DateTime.UtcNow>=nextDue) StartWorker(false); WriteDiagnostics(); };
            schedule.Start();
            LocationChanged+=delegate { locationEvents++;QueueSave(); };
            LayoutUpdated+=delegate { layoutEvents++; };
            SizeChanged+=delegate { bool w=ActualWidth>=600; if(w!=wide){wide=w; RenderNotes(true);} QueueSave(); };
            PreviewMouseMove+=ResizeCursor;
            PreviewMouseLeftButtonDown+=BeginResize;
            MouseLeave+=delegate { Cursor=Cursors.Arrow; };
            KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Escape) HideCard(); if(e.Key==Key.F5) Refresh(); };
            Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(!quitting){e.Cancel=true;HideCard();} };
            SourceInitialized+=delegate { sourceReady=true; ApplyNativeStyle(); };
            Loaded+=delegate {
                ClampPosition(); SetupTray(); WriteDiagnostics();
                if(!preview && !Json.B(settings,"visible",true)) Hide();
                if(selfTest) BeginSelfTest(); else if(!noSync) StartWorker(!File.Exists(Path.Combine(directory,"snapshot.json")));
                if(!selfTest && Environment.GetCommandLineArgs().Contains("--settings"))OpenSettings();
            };
        }
        int Count { get { return (int)Json.N(settings,"note_count",5); } }
        bool Transparent { get { return Json.S(settings,"theme")=="transparent"; } }
        bool WorkerRunning { get { try { return worker!=null && !worker.HasExited; } catch { return false; } } }
        static SolidColorBrush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        Brush Ink {get{return Transparent?Brushes.White:Brush("#F5F4F4");}}
        Brush Muted {get{return Transparent?Brush("#E4E4E9"):Brush("#BEBEC8");}}
        ShadowText Text(string value,double size,Brush brush=null) {
            var solid=brush as SolidColorBrush;
            bool muted=solid!=null&&(solid.Color==Color.FromRgb(0xBE,0xBE,0xC8)||solid.Color==Color.FromRgb(0xE4,0xE4,0xE9));
            return new ShadowText{ShadowEnabled=Transparent,ThemeInk=brush==null,ThemeMuted=muted,Text=value,FontSize=size,Foreground=brush??Ink,VerticalAlignment=VerticalAlignment.Center};
        }
        Button Button(string text,string tip,Action action) {
            var button=new Button{Content=text,ToolTip=tip,FontSize=17,Width=25,Height=26,Margin=new Thickness(3,0,0,0),Background=Brushes.Transparent,Foreground=Muted,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,Padding=new Thickness(0)};
            var template=new ControlTemplate(typeof(Button));
            var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(6)); border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
            var presenter=new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center); border.AppendChild(presenter); template.VisualTree=border;
            var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true}; hover.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#1AFFFFFF")));hover.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));template.Triggers.Add(hover);
            var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false}; disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.4)); template.Triggers.Add(disabled);
            button.Template=template; button.Click+=delegate { action(); }; return button;
        }
        Button IconButton(string kind,string tip,Action action) {
            var button=Button("",tip,action);var icon=new LineIcon{Kind=kind,ShadowEnabled=Transparent,Width=17,Height=17};
            icon.SetBinding(LineIcon.StrokeProperty,new System.Windows.Data.Binding("Foreground"){Source=button});button.Content=icon;
            System.Windows.Automation.AutomationProperties.SetName(button,tip);toolbarButtons.Add(button);return button;
        }
        void StyleScrollViewer() {
            // Transparent track, no arrow buttons; the hit area remains eight pixels.
            var barStyle=(Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
              <Setter Property='Width' Value='8'/><Setter Property='Background' Value='Transparent'/><Setter Property='Focusable' Value='False'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
                <Grid Background='Transparent'><Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>
                  <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
                  <Track.Thumb><Thumb MinHeight='22'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='Handle' Width='4' CornerRadius='2' Background='{DynamicResource ScrollHandle}' Opacity='0.38'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Handle' Property='Opacity' Value='0.68'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='Handle' Property='Opacity' Value='0.85'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                  <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
                </Track></Grid>
              </ControlTemplate></Setter.Value></Setter></Style>");
            scroll.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=barStyle;
            scroll.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollViewer}'>
              <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
                <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' CanContentScroll='{TemplateBinding CanContentScroll}' Margin='0,0,3,0'/>
                <ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Minimum='0' Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}' Value='{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}' Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>
              </Grid></ControlTemplate>");
        }
        void Build() {
            surface.CornerRadius=new CornerRadius(16); surface.BorderThickness=new Thickness(1); surface.Padding=new Thickness(19,17,19,15); Content=surface;
            surface.Child=content; content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto}); content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto}); content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)}); content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new Grid{Height=28,Background=Brush("#01000000")}; header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)}); header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            header.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){ if(e.Handled || HasButtonParent(e.OriginalSource as DependencyObject))return; try{DragMove();}catch(InvalidOperationException){} };
            var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            var mark=new Border{Width=22,Height=22,Background=Brush("#F04760"),CornerRadius=new CornerRadius(7),Margin=new Thickness(0,0,7,0)};
            var red=Text("红",12,Brushes.White);red.ShadowEnabled=false;red.SuppressShadow=true;red.HorizontalAlignment=HorizontalAlignment.Center;mark.Child=red;brand.Children.Add(mark);var brandText=Text(preview?"创作看板 · 测试":"创作看板",11);brandText.FontWeight=FontWeights.SemiBold;brand.Children.Add(brandText);header.Children.Add(brand);
            var controls=new StackPanel{Orientation=Orientation.Horizontal}; Grid.SetColumn(controls,1);
            refreshButton=IconButton("refresh","立即刷新（F5）",Refresh);pinButton=IconButton("pin","始终置顶",delegate{SetOption("on_top",!Topmost);});
            controls.Children.Add(refreshButton);controls.Children.Add(pinButton);controls.Children.Add(IconButton("settings","看板设置",OpenSettings));controls.Children.Add(IconButton("hide","隐藏到托盘",HideCard));header.Children.Add(controls);content.Children.Add(header);
            var section=new Grid{Margin=new Thickness(0,17,0,5)}; Grid.SetRow(section,1); section.ColumnDefinitions.Add(new ColumnDefinition()); section.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var heading=Text("最近文章",18);heading.FontWeight=FontWeight.FromOpenTypeWeight(650);section.Children.Add(heading);countLabel=Text("",10,Muted);Grid.SetColumn(countLabel,1);section.Children.Add(countLabel);content.Children.Add(section);
            scroll.Content=rows;scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;scroll.PanningMode=PanningMode.VerticalOnly;StyleScrollViewer();Grid.SetRow(scroll,2);content.Children.Add(scroll);
            var footer=new Grid();var footerBorder=new Border{Margin=new Thickness(0,7,0,0),BorderThickness=new Thickness(0,1,0,0),BorderBrush=Brush("#21FFFFFF"),Padding=new Thickness(0,10,0,0),Child=footer};Grid.SetRow(footerBorder,3);footer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});footer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var line=new Grid(); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var statusGroup=new Grid();statusGroup.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});statusGroup.ColumnDefinitions.Add(new ColumnDefinition());statusGroup.Children.Add(statusDot);
            statusLabel=Text(message,10);statusLabel.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(statusLabel,1);statusGroup.Children.Add(statusLabel);line.Children.Add(statusGroup);
            loginButton=Button("打开后台","打开创作后台 / 重新登录",delegate{StartWorker(true);});loginButton.Width=48;loginButton.Height=16;loginButton.FontSize=10;loginButton.Foreground=Brush("#FF6476");Grid.SetColumn(loginButton,1);line.Children.Add(loginButton);footer.Children.Add(line);
            var bottom=new Grid{Margin=new Thickness(0,4,0,0)}; Grid.SetRow(bottom,1); bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            timeLabel=Text("尚未同步",9,Muted);bottom.Children.Add(timeLabel);hintLabel=Text("每 15 分钟更新",9,Muted);Grid.SetColumn(hintLabel,1);bottom.Children.Add(hintLabel);footer.Children.Add(bottom);content.Children.Add(footerBorder);
        }
        bool HasButtonParent(DependencyObject d) { while(d!=null){if(d is Button)return true;try{d=VisualTreeHelper.GetParent(d);}catch{return false;}}return false; }
        int Edge(Point p) {
            bool l=p.X<8,r=p.X>ActualWidth-8,t=p.Y<8,b=p.Y>ActualHeight-8;
            if(t&&l)return 4; if(t&&r)return 5; if(b&&l)return 7; if(b&&r)return 8; if(l)return 1; if(r)return 2; if(t)return 3; if(b)return 6; return 0;
        }
        void ResizeCursor(object sender,MouseEventArgs e) {
            mouseEvents++;
            int edge=Edge(e.GetPosition(this)); Cursor=edge==1||edge==2?Cursors.SizeWE:edge==3||edge==6?Cursors.SizeNS:edge==4||edge==8?Cursors.SizeNWSE:edge==5||edge==7?Cursors.SizeNESW:Cursors.Arrow;
        }
        void BeginResize(object sender,MouseButtonEventArgs e) {
            int edge=Edge(e.GetPosition(this)); if(edge==0)return; e.Handled=true; Native.ReleaseCapture(); Native.SendMessage(new WindowInteropHelper(this).Handle,0x112,new IntPtr(0xF000+edge),IntPtr.Zero);
        }
        void ApplyTheme() {
            surface.Background=Transparent?new SolidColorBrush(Color.FromArgb(1,0,0,0)):Brush("#C2151821");
            surface.BorderBrush=Transparent?Brushes.Transparent:Brush("#3DFFFFFF");
            Resources["ScrollHandle"]=Transparent?Brush("#E4E4E9"):Brush("#687080");foreach(var button in toolbarButtons)button.Foreground=Muted;
            content.Effect=null;content.CacheMode=null;ApplyTextShadows(content);
        }
        void ApplyTextShadows(DependencyObject parent) {
            var text=parent as ShadowText;if(text!=null){text.ShadowEnabled=Transparent&&!text.SuppressShadow;if(text.ThemeInk)text.Foreground=Ink;else if(text.ThemeMuted)text.Foreground=Muted;text.RefreshShadow();}
            var icon=parent as LineIcon;if(icon!=null){icon.ShadowEnabled=Transparent;icon.InvalidateVisual();}
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)ApplyTextShadows(VisualTreeHelper.GetChild(parent,i));
        }
        void LoadSnapshot() {
            var snapshot=Json.Read(Path.Combine(directory,"snapshot.json")); object raw;
            if(snapshot.TryGetValue("notes",out raw) && raw is IEnumerable) {
                notes=((IEnumerable)raw).Cast<object>().OfType<Dictionary<string,object>>().Where(n=>Json.S(n,"id")!="").OrderByDescending(n=>Json.N(n,"published_timestamp")).ThenByDescending(n=>Json.S(n,"id")).Take(50).ToList();
                lastSuccess=Json.S(snapshot,"last_success"); RenderNotes(false);
            } else RenderNotes(false);
            RenderStatus();
        }
        void RenderNotes(bool force) {
            string fingerprint=new JavaScriptSerializer().Serialize(notes.Take(Count).ToArray())+"/"+Count+"/"+wide;
            countLabel.Text=notes.Count==0?"最近 "+Count+" 篇":Math.Min(notes.Count,Count)+" 篇 · 按发布时间";countLabel.ToolTip="按发布时间排序，显示最近 "+Count+" 篇";
            if(!force && fingerprint==noteFingerprint)return; noteFingerprint=fingerprint;
            double offset=scroll.VerticalOffset; rows.Children.Clear();
            if(notes.Count==0) {
                var empty=new StackPanel{Margin=new Thickness(0,40,0,30)}; var title=Text("让创作进展留在桌面",15); title.HorizontalAlignment=HorizontalAlignment.Center; empty.Children.Add(title);
                var description=Text("登录后，这里会显示最近文章的数据。",11,Brush("#DDDEE7")); description.TextWrapping=TextWrapping.Wrap; description.HorizontalAlignment=HorizontalAlignment.Center; description.Margin=new Thickness(0,12,0,15); empty.Children.Add(description);
                var login=Button("登录创作后台","打开登录窗口",delegate{StartWorker(true);}); login.Width=130; login.FontSize=12; login.Foreground=Brush("#FF8090"); empty.Children.Add(login); rows.Children.Add(empty);
            }
            foreach(var note in notes.Take(Count)) {
                var article=new Border{Padding=new Thickness(0,5,0,6),BorderBrush=Brush("#21FFFFFF"),BorderThickness=new Thickness(0,0,0,note==notes.Take(Count).Last()?0:1)};
                var grid=new Grid();var title=Text(Json.S(note,"title","无标题笔记"),12);title.FontWeight=FontWeight.FromOpenTypeWeight(550);title.TextWrapping=TextWrapping.Wrap;title.TextTrimming=TextTrimming.CharacterEllipsis;title.Height=34;title.LineHeight=17;title.VerticalAlignment=VerticalAlignment.Top;title.ToolTip=title.Text;
                var metrics=new Grid(); for(int i=0;i<3;i++)metrics.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(i==0?1.25:1,GridUnitType.Star)});
                string[] labels={Json.S(note,"read_label","浏览量"),"评论","收藏"}; string[] keys={"read_count","comment_count","collect_count"};
                for(int i=0;i<3;i++) {
                    var metric=new Grid{Height=20,Margin=new Thickness(0,0,i<2?16:0,0)};Grid.SetColumn(metric,i);metric.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});metric.ColumnDefinitions.Add(new ColumnDefinition());
                    var label=Text(labels[i],9,Muted);label.VerticalAlignment=VerticalAlignment.Bottom;label.Margin=new Thickness(0,0,5,3);metric.Children.Add(label);
                    var number=Text(Number(note,keys[i]),16,i==0?Brush("#FFD6DC"):Ink);number.FontFamily=new FontFamily("Bahnschrift, Segoe UI");number.FontWeight=FontWeights.Medium;number.VerticalAlignment=VerticalAlignment.Bottom;number.TextTrimming=TextTrimming.CharacterEllipsis;number.ToolTip=number.Text;Grid.SetColumn(number,1);metric.Children.Add(number);metrics.Children.Add(metric);
                }
                if(wide) {
                    grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)}); grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(280)}); title.Margin=new Thickness(0,0,18,0); Grid.SetColumn(metrics,1);
                } else {
                    grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});title.Margin=new Thickness(0,0,0,5);Grid.SetRow(metrics,1);
                }
                grid.Children.Add(title); grid.Children.Add(metrics); article.Child=grid; rows.Children.Add(article);
            }
            Dispatcher.BeginInvoke(new Action(delegate{scroll.ScrollToVerticalOffset(offset);}),DispatcherPriority.Loaded);
        }
        string Number(Dictionary<string,object> note,string key) {
            object v; if(!note.TryGetValue(key,out v)||v==null)return "—"; long n; return Int64.TryParse(Convert.ToString(v,CultureInfo.InvariantCulture),out n)&&n>=0?n.ToString("N0",CultureInfo.GetCultureInfo("zh-CN")):"—";
        }
        void RenderStatus() {
            statusLabel.Text=message; statusLabel.ToolTip=message;
            statusLabel.Foreground=Ink;
            statusDot.Fill=state=="ready"?Brush("#77D0A0"):state=="syncing"?Brush("#F4C881"):state=="error"||paused?Brush("#FF6476"):Brush("#B6BAC7");
            loginButton.Content=paused?"继续登录":"打开后台";
            refreshButton.IsEnabled=state!="syncing"&&!paused;pinButton.Foreground=Topmost?Brush("#FF6476"):Muted;pinButton.Background=Topmost?Brush("#21FF6476"):Brushes.Transparent;
            DateTimeOffset date; timeLabel.Text=DateTimeOffset.TryParse(lastSuccess,out date)?"更新于 "+date.ToLocalTime().ToString("MM-dd HH:mm"):"尚未同步";
            hintLabel.Text=Json.B(settings,"click_through")?"穿透开启 · 托盘关闭":paused?"自动同步已暂停":"每 15 分钟更新";
        }
        void FileChanged(object sender,FileSystemEventArgs e) {
            if(e.Name!="snapshot.json"&&e.Name!="worker-status.json")return;
            try{Dispatcher.BeginInvoke(new Action(delegate{fileTimer.Stop();fileTimer.Start();}));}catch{}
        }
        void LoadStatus() {
            var status=Json.Read(Path.Combine(directory,"worker-status.json"));
            if(currentRun==""||Json.S(status,"run_id")!=currentRun)return;
            state=Json.S(status,"status",state); message=Json.S(status,"message",message); paused=state=="login_required"||state=="verification_required"; RenderStatus();
            if(pendingLogin && Signal("Show"))pendingLogin=false;
        }
        bool Signal(string action) { try{using(var evt=EventWaitHandle.OpenExisting("Local\\XhsCollector-"+action+"-"+identity))evt.Set();return true;}catch(WaitHandleCannotBeOpenedException){return false;} }
        void Refresh() { paused=false; StartWorker(false); }
        void StartWorker(bool login) {
            if(noSync)return;
            if(WorkerRunning) { if(login)pendingLogin=!Signal("Show"); else Signal("Refresh"); return; }
            string path=Path.Combine(baseDir,"XhsCollector.exe");
            if(!File.Exists(path)){state="error";message="缺少 XhsCollector.exe，请把同步程序放在看板旁边";RenderStatus();return;}
            currentRun=Guid.NewGuid().ToString("N"); runningCount=Count; state="syncing"; message="正在同步最近文章"; paused=false; RenderStatus();
            var launched=new Process{StartInfo=new ProcessStartInfo(path,"--data-dir \""+directory+"\" --count "+Count+" --run-id "+currentRun+" --parent-pid "+Process.GetCurrentProcess().Id+(login?" --login":"")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=baseDir},EnableRaisingEvents=true};
            worker=launched;
            launched.Exited+=delegate{try{Dispatcher.BeginInvoke(new Action(delegate{if(worker!=launched)return;LoadSnapshot();LoadStatus();worker=null;launched.Dispose();nextDue=DateTime.UtcNow.AddMinutes(15);if(state=="syncing"){state="error";message="同步进程已结束，请重新刷新或打开后台";}RenderStatus();WriteDiagnostics();if(pendingLogin||pendingRefresh||runningCount!=Count){bool loginNext=pendingLogin;pendingLogin=false;pendingRefresh=false;StartWorker(loginNext);}}));}catch{}};
            try{launched.Start();WriteDiagnostics();}catch(Exception e){worker=null;launched.Dispose();state="error";message="无法启动同步程序："+e.Message;RenderStatus();}
        }
        void SetOption(string key,object value) {
            settings[key]=value; if(key=="on_top")Topmost=(bool)value;
            if(key=="theme"){ApplyTheme();RenderNotes(true);} if(key=="click_through")ApplyNativeStyle();
            SaveSettings();RenderStatus(); UpdateTray();
        }
        void SetCount(int count) {
            if(count<1||count>50)return; settings["note_count"]=count; SaveSettings(); RenderNotes(true);UpdateTray();
            if(!noSync && notes.Count<count){if(WorkerRunning)pendingRefresh=true;else StartWorker(false);}
        }
        void ApplyNativeStyle() {
            if(!sourceReady)return; IntPtr h=new WindowInteropHelper(this).Handle; long bits=Native.GetWindowLongPtr(h,-20).ToInt64();
            if(!preview)bits=(bits|0x80)&~0x40000L;
            bits=Json.B(settings,"click_through")?(bits|0x20|0x08000000):(bits&~0x20L&~0x08000000L);
            Native.SetWindowLongPtr(h,-20,new IntPtr(bits)); Native.SetWindowPos(h,IntPtr.Zero,0,0,0,0,0x37);
        }
        void ClampPosition() {
            double left=SystemParameters.VirtualScreenLeft,top=SystemParameters.VirtualScreenTop,right=left+SystemParameters.VirtualScreenWidth,bottom=top+SystemParameters.VirtualScreenHeight;
            Left=Math.Max(left,Math.Min(Left,right-Math.Min(Width,right-left))); Top=Math.Max(top,Math.Min(Top,bottom-Math.Min(Height,bottom-top)));
        }
        void QueueSave(){if(saveTimer!=null){saveTimer.Stop();saveTimer.Start();}}
        void SaveSettings() {
            settings["size"]=new double[]{Width,Height};settings["position"]=new double[]{Left,Top};
            try{Json.Write(Path.Combine(directory,"settings.json"),settings);}catch(Exception e){message="设置保存失败："+e.Message;RenderStatus();}
        }
        public void ShowCard() { settings["visible"]=true;Show();ClampPosition();if(!Json.B(settings,"click_through"))Activate();ApplyNativeStyle();SaveSettings(); }
        void HideCard(){settings["visible"]=false;Hide();SaveSettings();}
        void SetupTray() {
            tray=new Forms.NotifyIcon{Text="小红书创作看板",Visible=true};
            string icon=Path.Combine(baseDir,"icon.ico");tray.Icon=File.Exists(icon)?new System.Drawing.Icon(icon):System.Drawing.SystemIcons.Application;
            tray.DoubleClick+=delegate{Dispatcher.BeginInvoke(new Action(ShowCard));}; UpdateTray();
        }
        Forms.ToolStripMenuItem Item(string text,Action action,bool check=false) { var item=new Forms.ToolStripMenuItem(text){Checked=check};item.Click+=delegate{Dispatcher.BeginInvoke(action);};return item; }
        void UpdateTray() {
            if(tray==null)return;var old=tray.ContextMenuStrip;var menu=new Forms.ContextMenuStrip();
            menu.Items.Add(Item("显示桌面看板",ShowCard));menu.Items.Add(Item("隐藏桌面看板",HideCard));menu.Items.Add(Item("立即刷新",Refresh));menu.Items.Add(Item("打开创作后台 / 重新登录",delegate{StartWorker(true);}));menu.Items.Add(new Forms.ToolStripSeparator());
            var themes=new Forms.ToolStripMenuItem("主题");themes.DropDownItems.Add(Item("玻璃",delegate{SetOption("theme","glass");},!Transparent));themes.DropDownItems.Add(Item("透明",delegate{SetOption("theme","transparent");},Transparent));menu.Items.Add(themes);
            var count=new Forms.ToolStripMenuItem("显示文章数量");foreach(int number in new[]{1,3,5,10,20,50}){int n=number;count.DropDownItems.Add(Item(n+" 篇",delegate{SetCount(n);},Count==n));}count.DropDownItems.Add(Item("自定义…",OpenSettings));menu.Items.Add(count);
            menu.Items.Add(Item("始终置顶",delegate{SetOption("on_top",!Topmost);},Topmost));menu.Items.Add(Item("鼠标穿透",delegate{SetOption("click_through",!Json.B(settings,"click_through"));},Json.B(settings,"click_through")));menu.Items.Add(Item("看板设置",OpenSettings));menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add(Item("退出",Exit));
            tray.ContextMenuStrip=menu;if(old!=null)old.Dispose();
        }
        bool AutoStart() { using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){return key!=null&&key.GetValue("XhsDesktopBoard")!=null;} }
        void SetAutoStart(bool enable) {
            using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if(enable)key.SetValue("XhsDesktopBoard","\""+Process.GetCurrentProcess().MainModule.FileName+"\" --background");else key.DeleteValue("XhsDesktopBoard",false);
            }
        }
        Rect CurrentWorkArea() {
            // Screen coordinates are physical pixels; the WPF window uses DIPs.
            return Native.WorkArea(this);
        }
        static readonly double[][] PresetSizes={new double[]{320,360},new double[]{380,520},new double[]{520,700},new double[]{800,520}};
        void ApplyCardLayout(int sizeIndex,int positionIndex) {
            var area=CurrentWorkArea();
            if(sizeIndex>0 && sizeIndex<=PresetSizes.Length) {
                var size=PresetSizes[sizeIndex-1];
                Width=Math.Max(MinWidth,Math.Min(size[0],area.Width-32));Height=Math.Max(MinHeight,Math.Min(size[1],area.Height-32));
            }
            double left=area.Left+16,right=Math.Max(left,area.Right-Width-16),top=area.Top+16,bottom=Math.Max(top,area.Bottom-Height-16);
            if(positionIndex==1){Left=left;Top=top;}else if(positionIndex==2){Left=right;Top=top;}
            else if(positionIndex==3){Left=left;Top=bottom;}else if(positionIndex==4){Left=right;Top=bottom;}
            else if(positionIndex==5){Left=area.Left+Math.Max(0,(area.Width-Width)/2);Top=area.Top+Math.Max(0,(area.Height-Height)/2);}
            else if(sizeIndex>0){Left=Math.Max(area.Left,Math.Min(Left,area.Right-Width));Top=Math.Max(area.Top,Math.Min(Top,area.Bottom-Height));}
            SaveSettings();WriteDiagnostics();
        }
        void OpenSettings() {
            if(settingsWindow!=null){settingsWindow.Activate();return;}
            var area=CurrentWorkArea();int positionIndex=0;
            bool left=Math.Abs(Left-area.Left-16)<2,right=Math.Abs(Left-(area.Right-Width-16))<2;
            bool top=Math.Abs(Top-area.Top-16)<2,bottom=Math.Abs(Top-(area.Bottom-Height-16))<2;
            if(left&&top)positionIndex=1;else if(right&&top)positionIndex=2;else if(left&&bottom)positionIndex=3;else if(right&&bottom)positionIndex=4;
            else if(Math.Abs(Left-(area.Left+(area.Width-Width)/2))<2 && Math.Abs(Top-(area.Top+(area.Height-Height)/2))<2)positionIndex=5;
            var dialog=new SettingsDialog(FontFamily,Transparent,Count,Width,Height,positionIndex,Topmost,Json.B(settings,"click_through"),AutoStart());
            settingsWindow=dialog;dialog.Owner=this;
            dialog.Apply+=delegate {
                try {
                    // Do not rewrite the startup entry when only appearance changes.
                    if(dialog.AutoStartEnabled!=AutoStart())SetAutoStart(dialog.AutoStartEnabled);
                    SetOption("theme",dialog.TransparentTheme?"transparent":"glass");SetOption("on_top",dialog.PinEnabled);
                    ApplyCardLayout(dialog.SizeIndex,dialog.PositionIndex);SetCount(dialog.NoteCount);
                    bool pass=dialog.PassEnabled;dialog.Close();SetOption("click_through",pass);WriteDiagnostics();
                }catch(Exception e){dialog.ShowError("保存失败："+e.Message);}
            };
            dialog.Closed+=delegate{settingsWindow=null;dialog.ReleaseContents();};
            dialog.Show();
        }
        void WriteDiagnostics() {
            try{Json.Write(Path.Combine(directory,"runtime-native.json"),new{pid=Process.GetCurrentProcess().Id,worker_running=WorkerRunning,visible=IsVisible,width=Width,height=Height,left=Left,top=Top,note_count=Count,rendered_count=Math.Min(notes.Count,Count),theme=Json.S(settings,"theme"),scroll_extent=scroll.ExtentHeight,scroll_viewport=scroll.ViewportHeight,status=state,layout_events=layoutEvents,mouse_events=mouseEvents,location_events=locationEvents});}catch{}
        }
        void BeginSelfTest() {
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)};int step=0;var result=new Dictionary<string,object>();
            timer.Tick+=delegate {try{step++;if(step==1){SetCount(20);Width=320;Height=280;}else if(step==2){UpdateLayout();if(scroll.ScrollableHeight<=0)throw new Exception("No overflow scrollbar");scroll.ScrollToEnd();}else if(step==3){if(scroll.VerticalOffset<=0)throw new Exception("Scroll did not move");result["scrollbar"]=true;Width=800;Height=600;}else if(step==4){if(!wide)throw new Exception("Wide layout did not adapt");result["responsive"]=true;SetOption("theme","glass");SetOption("theme","transparent");SetCount(3);}else if(step==5){if(rows.Children.Count!=3)throw new Exception("Display count failed");SaveSettings();var restored=Json.Read(Path.Combine(directory,"settings.json"));if(Json.N(restored,"note_count")!=3||Json.Pair(restored,"size",new double[]{0,0})[0]!=800)throw new Exception("Persistence failed");result["count"]=true;result["size_saved"]=true;timer.Stop();BeginSettingsLifecycleTest(result);}}catch(Exception e){result["ok"]=false;result["error"]=e.ToString();Json.Write(Path.Combine(directory,"native-qa.json"),result);timer.Stop();Exit();}};
            timer.Start();
        }
        void BeginSettingsLifecycleTest(Dictionary<string,object> result) {
            var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};int phase=0;double before=0,baseline=0;
            timer.Tick+=delegate {
                phase++;
                if(phase==1)before=Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
                else if(phase==5){baseline=Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds-before;OpenSettings();}
                else if(phase==6 && settingsWindow!=null)settingsWindow.Close();
                else if(phase==7)before=Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
                else if(phase==11){
                    double cpu=Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds-before;
                    // Compare identical card conditions; preview conditions may
                    // include rendering activity unrelated to this dialog.
                    result["baseline_cpu_seconds"]=baseline;result["after_settings_cpu_seconds"]=cpu;result["settings_closed"]=settingsWindow==null;
                    result["ok"]=cpu<=baseline+0.4 && settingsWindow==null;
                    Json.Write(Path.Combine(directory,"native-qa.json"),result);timer.Stop();Exit();
                }
            };
            timer.Start();
        }
        public void Exit() {
            if(quitting)return;quitting=true;SaveSettings();Signal("Stop");schedule.Stop();saveTimer.Stop();fileTimer.Stop();files.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}Close();Application.Current.Shutdown();
        }
    }
    // The settings panel is native WPF too: no browser or continuously animated
    // blur surface is needed for a rounded, translucent desktop card.
    class SettingsDialog : Window {
        readonly ComboBox theme,count,position,size;
        readonly CheckBox pin,pass,auto;
        readonly TextBlock error;
        readonly int originalPositionIndex,originalSizeIndex;
        public event Action Apply;
        public bool TransparentTheme {get{return theme.SelectedIndex==1;}}
        public int NoteCount {get{return count.SelectedIndex+1;}}
        public int PositionIndex {get{return position.SelectedIndex==originalPositionIndex && size.SelectedIndex==originalSizeIndex ? 0 : position.SelectedIndex;}}
        public int SizeIndex {get{return size.SelectedIndex==originalSizeIndex ? 0 : size.SelectedIndex;}}
        public bool PinEnabled {get{return pin.IsChecked==true;}}
        public bool PassEnabled {get{return pass.IsChecked==true;}}
        public bool AutoStartEnabled {get{return auto.IsChecked==true;}}
        public void ShowError(string text){error.Text=text;error.Visibility=Visibility.Visible;}
        public void ReleaseContents() {
            theme.IsDropDownOpen=false;count.IsDropDownOpen=false;position.IsDropDownOpen=false;size.IsDropDownOpen=false;
            // Detach the closed dialog's visual tree and popup resources promptly.
            Content=null;Resources.Clear();
        }
        public SettingsDialog(FontFamily font,bool transparent,int number,double width,double height,int positionIndex,bool pinned,bool through,bool startup) {
            Title="创作看板 · 设置";Width=380;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=true;Topmost=true;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=font;FontSize=12;Foreground=ColorBrush("#F5F4F4");
            UseLayoutRounding=true;TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
            Resources=(ResourceDictionary)XamlReader.Parse(Styles);
            var shell=new Border{CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1),BorderBrush=ColorBrush("#3DFFFFFF"),Background=ColorBrush("#F0151821")};
            Content=shell;var panel=new StackPanel();shell.Child=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            var header=new Grid{Height=28,Background=Brushes.Transparent,Margin=new Thickness(19,17,19,0)};
            header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            var mark=new Border{Width=22,Height=22,CornerRadius=new CornerRadius(7),Background=ColorBrush("#F04760"),Margin=new Thickness(0,0,7,0)};
            mark.Child=new TextBlock{Text="红",FontSize=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};brand.Children.Add(mark);
            brand.Children.Add(new TextBlock{Text="创作看板",FontSize=11,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});header.Children.Add(brand);
            var close=new Button{Width=25,Height=26,Style=(Style)Resources["QuietButton"],ToolTip="关闭设置",VerticalAlignment=VerticalAlignment.Center};
            var closeIcon=new LineIcon{Kind="hide",Width=17,Height=17};closeIcon.SetBinding(LineIcon.StrokeProperty,new System.Windows.Data.Binding("Foreground"){Source=close});close.Content=closeIcon;
            System.Windows.Automation.AutomationProperties.SetName(close,"关闭设置");Grid.SetColumn(close,1);header.Children.Add(close);close.Click+=delegate{Close();};
            header.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.OriginalSource is LineIcon || e.Handled)return;try{DragMove();}catch(InvalidOperationException){}};
            panel.Children.Add(header);
            panel.Children.Add(new TextBlock{Text="看板设置",FontSize=18,FontWeight=FontWeight.FromOpenTypeWeight(650),Margin=new Thickness(19,17,19,10)});
            var rows=new StackPanel{Margin=new Thickness(19,0,19,9)};panel.Children.Add(rows);
            theme=Dropdown("主题",new[]{"深色","透明"},transparent?1:0);theme.ToolTip="深色为半透明玻璃卡片；透明为无背景卡片";
            count=Dropdown("显示篇数",Enumerable.Range(1,50).Select(n=>n+" 篇").ToArray(),number-1);count.ToolTip="1–50 篇；内容放不下时可滚动";
            originalPositionIndex=positionIndex;position=Dropdown("卡片位置",new[]{"当前位置","左上角","右上角","左下角","右下角","居中"},positionIndex);position.ToolTip="在看板所在屏幕的可用区域中放置，避开任务栏";
            int selected=0;double[][] presets={new double[]{320,360},new double[]{380,520},new double[]{520,700},new double[]{800,520}};
            for(int i=0;i<presets.Length;i++)if(Math.Abs(width-presets[i][0])<1 && Math.Abs(height-presets[i][1])<1)selected=i+1;
            originalSizeIndex=selected;size=Dropdown("卡片大小",new[]{"当前尺寸","紧凑","中等","大号","宽幅"},selected);size.ToolTip="紧凑 320×360；中等 380×520；大号 520×700；宽幅 800×520（逻辑尺寸）";
            AddRow(rows,"主题",theme);AddRow(rows,"显示篇数",count);AddRow(rows,"卡片位置",position);AddRow(rows,"卡片大小",size);
            panel.Children.Add(new Border{Height=1,Background=ColorBrush("#21FFFFFF"),Margin=new Thickness(19,0,19,12)});
            var advanced=new Expander{Header="更多设置",Style=(Style)Resources["MoreSettings"],Margin=new Thickness(19,0,19,0)};
            var extras=new StackPanel{Margin=new Thickness(0,13,0,0)};pin=Toggle("始终置顶",pinned);pass=Toggle("鼠标穿透（在托盘中关闭）",through);auto=Toggle("开机启动",startup);
            extras.Children.Add(pin);extras.Children.Add(pass);extras.Children.Add(auto);advanced.Content=extras;panel.Children.Add(advanced);
            error=new TextBlock{Foreground=ColorBrush("#FF8090"),FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(19,10,19,0),Visibility=Visibility.Collapsed};panel.Children.Add(error);
            var footer=new Grid{Margin=new Thickness(19,16,19,15)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            footer.Children.Add(new TextBlock{Text="修改后保存生效",FontSize=9,Foreground=ColorBrush("#BEBEC8"),VerticalAlignment=VerticalAlignment.Center});
            var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);footer.Children.Add(actions);
            var cancel=new Button{Content="取消",Width=46,Height=28,FontSize=11,Margin=new Thickness(0,0,8,0),Style=(Style)Resources["QuietButton"]};cancel.Click+=delegate{Close();};actions.Children.Add(cancel);
            var save=new Button{Content="保存设置",IsDefault=true,Height=28,Padding=new Thickness(12,0,12,0),FontSize=11,Style=(Style)Resources["SaveButton"]};actions.Children.Add(save);panel.Children.Add(footer);
            save.Click+=delegate{if(Apply!=null)Apply();};KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}};
            Loaded+=delegate{ConstrainToScreen();};SizeChanged+=delegate{if(IsLoaded)ConstrainToScreen();};
        }
        void ConstrainToScreen() {
            var area=Native.WorkArea(this);MaxHeight=Math.Max(260,area.Height-32);
            Left=Math.Max(area.Left,Math.Min(Left,area.Right-ActualWidth));Top=Math.Max(area.Top,Math.Min(Top,area.Bottom-ActualHeight));
        }
        static SolidColorBrush ColorBrush(string value){return (SolidColorBrush)new BrushConverter().ConvertFromString(value);}
        ComboBox Dropdown(string name,string[] choices,int selected) {
            var combo=new ComboBox{Style=(Style)Resources["PanelCombo"],Height=32,MinWidth=156,ItemsSource=choices,SelectedIndex=selected,MaxDropDownHeight=242};
            System.Windows.Automation.AutomationProperties.SetName(combo,name);return combo;
        }
        void AddRow(Panel panel,string label,ComboBox combo) {
            var row=new Grid{Margin=new Thickness(0,5,0,5)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(156)});
            row.Children.Add(new TextBlock{Text=label,FontSize=12,FontWeight=FontWeights.Medium,VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(combo,1);row.Children.Add(combo);panel.Children.Add(row);
        }
        CheckBox Toggle(string title,bool value){return new CheckBox{Content=title,IsChecked=value,Foreground=ColorBrush("#BEBEC8"),FontSize=11,Margin=new Thickness(0,0,0,11),Style=(Style)Resources["PanelCheck"]};}
        const string Styles=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
          <Style x:Key='QuietButton' TargetType='Button'>
            <Setter Property='Foreground' Value='#BEBEC8'/><Setter Property='Background' Value='Transparent'/><Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='Bg' Background='{TemplateBinding Background}' CornerRadius='7'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bg' Property='Background' Value='#16FFFFFF'/><Setter Property='Foreground' Value='White'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Bg' Property='Background' Value='#22FFFFFF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key='SaveButton' TargetType='Button' BasedOn='{StaticResource QuietButton}'>
            <Setter Property='Background' Value='#20F04760'/><Setter Property='Foreground' Value='#FF7188'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='Bg' Background='{TemplateBinding Background}' BorderBrush='#55F04760' BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bg' Property='Background' Value='#36F04760'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Bg' Property='BorderBrush' Value='#FFC0CB'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType='ComboBoxItem'>
            <Setter Property='Foreground' Value='#F5F4F4'/><Setter Property='Padding' Value='10,7'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/>
            <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border x:Name='Bg' Background='Transparent' CornerRadius='5' Padding='{TemplateBinding Padding}' Margin='1,1'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bg' Property='Background' Value='#14FFFFFF'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Bg' Property='Background' Value='#22FFFFFF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType='ScrollBar'>
            <Setter Property='Width' Value='7'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>
              <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb MinHeight='22'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Width='3' Background='#687383' CornerRadius='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            </Track></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key='PanelCombo' TargetType='ComboBox'>
            <Setter Property='Foreground' Value='#F5F4F4'/><Setter Property='FontSize' Value='11.5'/><Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/><Setter Property='ScrollViewer.VerticalScrollBarVisibility' Value='Auto'/>
            <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
              <Grid>
                <ToggleButton x:Name='Toggle' Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                  <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border x:Name='Bg' BorderThickness='1' BorderBrush='#24FFFFFF' CornerRadius='6' Background='#0DFFFFFF'><Path Stroke='#BEBEC8' StrokeThickness='1.3' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Data='M0,0 L4,4 L8,0' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,12,0'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bg' Property='Background' Value='#16FFFFFF'/><Setter TargetName='Bg' Property='BorderBrush' Value='#44FFFFFF'/></Trigger><Trigger Property='IsChecked' Value='True'><Setter TargetName='Bg' Property='BorderBrush' Value='#68FFFFFF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter Margin='11,0,31,0' HorizontalAlignment='Left' VerticalAlignment='Center' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
                <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'>
                  <Border MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='{TemplateBinding MaxDropDownHeight}' Margin='0,4,0,0' Padding='3' Background='#FA1B1F29' BorderBrush='#35FFFFFF' BorderThickness='1' CornerRadius='8'>
                    <ScrollViewer CanContentScroll='True' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
                  </Border>
                </Popup>
              </Grid><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Toggle' Property='Opacity' Value='0.85'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key='PanelCheck' TargetType='CheckBox'>
            <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Grid Background='Transparent'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Border x:Name='Box' Width='16' Height='16' CornerRadius='4' BorderThickness='1' BorderBrush='#65758397' Background='#18FFFFFF' VerticalAlignment='Center'><Path x:Name='Check' Data='M3,8 L6.5,11.5 L12.5,4.5' Stroke='White' StrokeThickness='1.5' Visibility='Collapsed'/></Border><ContentPresenter Grid.Column='1' Margin='9,0,0,0' VerticalAlignment='Center'/></Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='#F04760'/><Setter TargetName='Box' Property='BorderBrush' Value='#F04760'/><Setter TargetName='Check' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#C8D0DF'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='White'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key='MoreSettings' TargetType='Expander'>
            <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Expander'><StackPanel>
              <ToggleButton Foreground='#B7BAC8' FontSize='11' HorizontalAlignment='Left' IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><StackPanel Orientation='Horizontal' Background='Transparent'><Path x:Name='Arrow' Data='M0,0 L4,4 L0,8' Stroke='#B7BAC8' StrokeThickness='1.3' VerticalAlignment='Center' Margin='0,0,8,0'/><ContentPresenter/></StackPanel><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Arrow' Property='Data' Value='M0,0 L4,4 L8,0'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='White'/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template><ContentPresenter Content='{TemplateBinding Header}'/></ToggleButton>
              <ContentPresenter x:Name='Extras' Content='{TemplateBinding Content}' Visibility='Collapsed'/>
            </StackPanel><ControlTemplate.Triggers><Trigger Property='IsExpanded' Value='True'><Setter TargetName='Extras' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
        </ResourceDictionary>";
    }
    static class Program {
        [STAThread] public static int Main(string[] args) {
            string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");bool preview=false,noSync=false,qa=false;
            for(int i=0;i<args.Length;i++){if(args[i]=="--data-dir"&&i+1<args.Length)dir=args[++i];else if(args[i]=="--preview")preview=true;else if(args[i]=="--no-sync")noSync=true;else if(args[i]=="--self-test"){qa=true;preview=true;noSync=true;}}
            dir=Path.GetFullPath(dir);string id;using(var hash=SHA256.Create())id=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(dir.ToLowerInvariant()))).Replace("-","").ToLowerInvariant().Substring(0,16);
            bool created;using(var mutex=new Mutex(false,"Local\\XhsBoard-"+id,out created))using(var activate=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\XhsBoard-Activate-"+id)) {
                if(!created){activate.Set();return 0;}
                try {
                    // This card is static between syncs; avoid keeping a D3D device
                    // and GPU buffers alive solely for text and a translucent panel.
                    RenderOptions.ProcessRenderMode=args.Contains("--hardware-render")?RenderMode.Default:RenderMode.SoftwareOnly;
                    var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var card=new Card(dir,id,preview,noSync,qa);app.MainWindow=card;
                    var listener=new Thread(delegate(){while(true){activate.WaitOne();try{card.Dispatcher.BeginInvoke(new Action(card.ShowCard));}catch{return;}}});listener.IsBackground=true;listener.Start();app.Run(card);return 0;
                }catch(Exception e){Directory.CreateDirectory(dir);File.AppendAllText(Path.Combine(dir,"native-error.log"),DateTime.Now+" "+e+Environment.NewLine);Forms.MessageBox.Show("看板启动失败，请查看 data/native-error.log。","小红书创作看板");return 1;}
            }
        }
    }
}
