// Real WPF windows and tray menu clicks; only the test process ends its session.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace XhsNative {
    class VisibilityTestApp :
#if BASELINE
        Application
#else
        BoardApplication
#endif
    {
        public void EndTestSession() {
            var ending=(SessionEndingCancelEventArgs)Activator.CreateInstance(typeof(SessionEndingCancelEventArgs),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{ReasonSessionEnding.Shutdown},null);
            OnSessionEnding(ending);
            Shutdown();
        }
    }
    static class VisibilityTests {
        [STAThread] public static int Main(string[] args) {
            string directory=Path.GetFullPath(args[0]);bool initial=args[1]=="show",final=args[2]=="show";
            Directory.CreateDirectory(directory);
            Json.Write(Path.Combine(directory,"settings.json"),new {visible=initial,size=new double[]{380,520},theme="glass"});
            var app=new VisibilityTestApp{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var card=new Card(directory,"visibility-test-"+Guid.NewGuid().ToString("N"),false,true,false);app.MainWindow=card;
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};int step=0;string failure=null;bool ended=false;
            Action<bool,string> require=delegate(bool ok,string message){if(!ok)throw new Exception(message);};
            Func<Forms.ToolStripMenuItem> visibilityItem=delegate {
                var tray=(Forms.NotifyIcon)typeof(Card).GetField("tray",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(card);
                var controls=tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>().Where(i=>i.Text=="显示桌面看板" || i.Text=="隐藏桌面看板").ToArray();
                require(controls.Length==1,"Visibility must have one tray item");return controls[0];
            };
            Action<bool> check=delegate(bool wanted) {
                require(card.IsVisible==wanted,"Window visibility did not restore or toggle");
                require(visibilityItem().Checked==wanted,"Tray checkmark is stale");
                require(Json.B(Json.Read(Path.Combine(directory,"settings.json")),"visible") == wanted,"Saved visibility disagrees with tray");
            };
            app.Exit+=delegate {
                try {
                    require(ended,"Session test did not run");
                    require(Json.B(Json.Read(Path.Combine(directory,"settings.json")),"visible")==final,"Shutdown changed the last visibility choice");
                }catch(Exception error){if(failure==null)failure=error.Message;}
                Json.Write(Path.Combine(directory,"visibility-qa.json"),new {ok=failure==null,error=failure,initial_visible=initial,shutdown_visible=final,session_ending_simulated=ended});
            };
            timer.Tick+=delegate {
                try {
                    step++;
#if BASELINE
                    if(step==1){
                        require(card.IsVisible==initial,"Initial window visibility disagrees");
                        if(final)card.ShowCard();else {
                            var tray=(Forms.NotifyIcon)typeof(Card).GetField("tray",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(card);
                            tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>().First(i=>i.Text=="隐藏桌面看板").PerformClick();
                        }
                    }else if(step==2){timer.Stop();ended=true;app.EndTestSession();}
#else
                    if(step==1){check(initial);visibilityItem().PerformClick();}
                    else if(step==2){check(!initial);visibilityItem().PerformClick();}
                    else if(step==3){check(initial);card.Close();}
                    else if(step==4){check(false);card.ShowCard();}
                    else if(step==5){check(true);if(!final)visibilityItem().PerformClick();}
                    else if(step==6){check(final);timer.Stop();ended=true;app.EndTestSession();}
#endif
                }catch(Exception error){failure=error.Message;timer.Stop();card.Exit();}
            };
            card.Loaded+=delegate {timer.Start();};
            app.Run(card);return failure==null?0:1;
        }
    }
}
