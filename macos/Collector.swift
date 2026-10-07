import Cocoa
import WebKit
import Darwin

// Site messages carry only observed list responses. No file/settings/command
// bridge is registered on the login website.
final class BrowserCollector: NSObject, WKScriptMessageHandler, WKNavigationDelegate, NSWindowDelegate {
    let store: Store, runID: String, limit: Int
    let interactive: Bool
    var window: NSWindow!, web: WKWebView!, timer: Timer?
    var pages = Set<Int>(), notes: [Note] = [], expected = 0
    var label = "浏览量", started = Date(), lastScroll = Date.distantPast, finished = false
    let parentPID: Int32
    init(store: Store, runID: String, limit: Int, interactive: Bool, parentPID: Int32) {
        self.store = store; self.runID = runID; self.limit = limit; self.interactive = interactive; self.parentPID = parentPID
    }
    func status(_ state: String, _ message: String) {try? store.write("worker-status.json",WorkerStatus(run_id:runID,status:state,message:message))}
    func start() {
        let config = WKWebViewConfiguration(); config.websiteDataStore = .default()
        config.userContentController.add(self,name:"postedResponse")
        config.userContentController.addUserScript(WKUserScript(source: Self.observer,injectionTime:.atDocumentStart,forMainFrameOnly:true))
        web = WKWebView(frame:NSRect(x:0,y:0,width:1100,height:760),configuration:config); web.navigationDelegate = self
        window = NSWindow(contentRect:web.frame,styleMask:[.titled,.closable,.resizable,.miniaturizable],backing:.buffered,defer:false)
        window.title = "小红书创作后台 · 登录与同步"; window.contentView = web; window.delegate = self; window.center(); window.isReleasedWhenClosed = false
        if interactive {window.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)}
        status("syncing","正在同步最近文章");web.load(URLRequest(url:creatorURL))
        timer = Timer.scheduledTimer(withTimeInterval:2,repeats:true) {[weak self] _ in self?.tick()}
    }
    func tick() {
        guard !finished else {return}
        if parentPID > 0 && kill(parentPID,0) != 0 {finish();return}
        if Date().timeIntervalSince(started) > (interactive ? 1800 : 75) {fail(.invalid("后台连接超时，请检查网络或打开后台"));return}
        web.evaluateJavaScript(Self.probe) {[weak self] result,error in
            guard let self = self, !self.finished else {return}
            if let state = result as? [String:Any] {
                self.label = state["label"] as? String ?? "浏览量"
                switch state["state"] as? String {
                case "login": self.status("login_required","请登录小红书创作后台");if !self.interactive {self.finish()}
                case "verification": self.status("verification_required","后台需要验证，请打开后台");if !self.interactive {self.finish()}
                default: break
                }
            }
            if !self.pages.isEmpty && !self.finished {self.web.evaluateJavaScript(Self.scroll,completionHandler:nil)}
        }
    }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard !finished, message.frameInfo.isMainFrame, let origin = message.frameInfo.request.url, official(origin),
              let value = message.body as? [String:Any], let text = value["url"] as? String, let url = URL(string:text),
              let page = postedPage(url), page == expected, !pages.contains(page), let responseStatus = value["status"] as? Int else {return}
        do {
            label = value["label"] as? String ?? label
            let (received,next) = try parsePage(value["body"] ?? NSNull(), status:responseStatus,label:label)
            pages.insert(page); notes += received
            if next == -1 || Set(notes.filter{!$0.sticky}.map{$0.id}).count >= limit {
                let result = recent(notes,limit:limit).map { n -> Note in var n = n;n.read_label = label;return n }
                try store.write("snapshot.json",Snapshot(notes:result,last_success:ISO8601DateFormatter().string(from:Date())))
                status("ready","已同步 · 每 15 分钟更新");finish()
            } else {
                if pages.contains(next) {throw BoardError.invalid("后台分页未前进")}
                expected = next
            }
        } catch let error as BoardError {fail(error)} catch {fail(.invalid("同步失败，已保留上次数据"))}
    }
    func fail(_ error: BoardError) {status(error.state,error.message);finish()}
    func finish() {
        guard !finished else {return};finished = true;timer?.invalidate();web.stopLoading()
        web.configuration.userContentController.removeScriptMessageHandler(forName:"postedResponse");window.orderOut(nil)
        NSApp.terminate(nil)
    }
    func windowWillClose(_ notification: Notification) {status("error","后台已关闭；可从菜单重新登录");finish()}
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        if navigationAction.targetFrame?.isMainFrame != false, let url = navigationAction.request.url, !official(url) {decisionHandler(.cancel)} else {decisionHandler(.allow)}
    }
    static let observer = #"""
    (() => {
      const relevant = value => {try {const u=new URL(value,location.href);return u.protocol==='https:' && (u.hostname==='xiaohongshu.com'||u.hostname.endsWith('.xiaohongshu.com')) && u.pathname==='/api/galaxy/v2/creator/note/user/posted' && u.searchParams.get('tab')==='1';}catch{return false;}};
      const emit=(url,status,body)=>{const text=document.body?document.body.innerText:'';const label=/阅读量/.test(text)?'阅读量':/观看量/.test(text)?'观看量':'浏览量';if(relevant(url))window.webkit.messageHandlers.postedResponse.postMessage({url:String(url),status,body,label});};
      const original=window.fetch;
      window.fetch=async function(...args){const response=await original.apply(this,args);const url=response.url;if(relevant(url)){response.clone().text().then(text=>{if(text.length<=4000000){try{emit(url,response.status,JSON.parse(text));}catch{emit(url,response.status,null);}}}).catch(()=>{});}return response;};
      const open=XMLHttpRequest.prototype.open,send=XMLHttpRequest.prototype.send;
      XMLHttpRequest.prototype.open=function(method,url,...rest){this.__boardURL=new URL(url,location.href).href;return open.call(this,method,url,...rest);};
      XMLHttpRequest.prototype.send=function(...args){if(relevant(this.__boardURL)){this.addEventListener('load',()=>{try{const text=this.responseType==='json'?JSON.stringify(this.response):this.responseText;if(text.length>4000000)return;emit(this.responseURL||this.__boardURL,this.status,JSON.parse(text));}catch{emit(this.__boardURL,this.status,null);}});}return send.apply(this,args);};
    })()
    """#
    static let probe = #"""
    (()=>{const text=document.body?document.body.innerText:'';
      if(/安全验证|拖动滑块|完成验证|访问过于频繁/.test(text))return {state:'verification'};
      const login=/短信登录|验证码登录|扫码登录|手机号登录|发送验证码/.test(text);
      if(login&&!document.querySelector('.note-card'))return {state:'login'};
      const tab=[...document.querySelectorAll('span,div,button,a')].find(e=>e.textContent.trim()==='已发布'&&e.children.length===0&&e.getBoundingClientRect().height>0);
      if(tab&&!window.__boardPublished){window.__boardPublished=true;tab.click();}
      return {state:tab||document.querySelector('.note-card')?'manager':'loading',label:/阅读量/.test(text)?'阅读量':/观看量/.test(text)?'观看量':'浏览量'};
    })()
    """#
    static let scroll = #"""
    (()=>{const cards=[...document.querySelectorAll('.note-card')];if(!cards.length)return;cards.at(-1).scrollIntoView({block:'end'});let e=cards.at(-1).parentElement;while(e&&e!==document.body){if(e.scrollHeight>e.clientHeight+20&&/(auto|scroll)/.test(getComputedStyle(e).overflowY)){e.scrollTop=e.scrollHeight;return;}e=e.parentElement;}window.scrollTo(0,document.documentElement.scrollHeight);})()
    """#
}
