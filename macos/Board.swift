import Cocoa
import CoreGraphics
import ServiceManagement

let accent = NSColor(calibratedRed:240/255,green:71/255,blue:96/255,alpha:1)
let ink = NSColor(calibratedWhite:0.96,alpha:1)
let muted = NSColor(calibratedRed:190/255,green:190/255,blue:200/255,alpha:1)
func text(_ value:String, _ rect:NSRect, _ size:CGFloat, _ color:NSColor = ink, weight:NSFont.Weight = .regular, shadow:Bool = false, wrap:Bool = false) {
    let paragraph = NSMutableParagraphStyle(); paragraph.lineBreakMode = wrap ? .byWordWrapping : .byTruncatingTail
    var attributes:[NSAttributedString.Key:Any] = [.font:NSFont.systemFont(ofSize:size,weight:weight),.foregroundColor:color,.paragraphStyle:paragraph]
    if shadow {let shade = NSShadow();shade.shadowColor = NSColor.black.withAlphaComponent(0.8);shade.shadowBlurRadius = 3;shade.shadowOffset = NSSize(width:0,height:-1);attributes[.shadow] = shade}
    NSAttributedString(string:value,attributes:attributes).draw(with:rect,options:[.usesLineFragmentOrigin,.truncatesLastVisibleLine])
}
final class ActionButton:NSButton {
    var actionBlock:(()->Void)?
    init(symbol:String? = nil,title:String = "",tip:String = "",action:@escaping ()->Void) {
        super.init(frame:.zero);self.title = title;self.toolTip = tip;self.actionBlock = action
        isBordered = false;font = .systemFont(ofSize:11);contentTintColor = muted;target = self;self.action = #selector(invoke)
        if let symbol = symbol {image = NSImage(systemSymbolName:symbol,accessibilityDescription:tip);imagePosition = .imageOnly}
    }
    required init?(coder:NSCoder){fatalError("init(coder:) not supported")}
    @objc func invoke(){actionBlock?()}
}
final class CardPanel:NSPanel {override var canBecomeKey:Bool {true};override var canBecomeMain:Bool {false}}
final class HeaderView:NSView {override var isFlipped:Bool {true};override func mouseDown(with event:NSEvent){window?.performDrag(with:event)}}
final class NoteDocument:NSView {override var isFlipped:Bool {true}}
final class NoteRow:NSView {
    let note:Note;var transparent:Bool
    override var isFlipped:Bool {true}
    init(note:Note,transparent:Bool){self.note = note;self.transparent = transparent;super.init(frame:.zero);toolTip = note.title}
    required init?(coder:NSCoder){fatalError("init(coder:) not supported")}
    override func draw(_ dirtyRect:NSRect) {
        let wide = bounds.width >= 562
        text(note.title,NSRect(x:0,y:6,width:wide ? bounds.width-296 : bounds.width,height:34),12,weight:.medium,shadow:transparent,wrap:true)
        let labels = [note.read_label,"评论","收藏"], numbers = [note.read_count,note.comment_count,note.collect_count]
        let total:CGFloat = wide ? 280 : bounds.width, origin:CGFloat = wide ? bounds.width-280 : 0, y:CGFloat = wide ? 13 : 43
        let widths:[CGFloat] = [total*0.38,total*0.31,total*0.31]
        var x = origin
        let formatter = NumberFormatter();formatter.numberStyle = .decimal
        for index in 0..<3 {
            text(labels[index],NSRect(x:x,y:y+7,width:40,height:14),9,transparent ? ink : muted,shadow:transparent)
            let number = numbers[index].flatMap{formatter.string(from:NSNumber(value:$0))} ?? "—"
            text(number,NSRect(x:x+42,y:y,width:widths[index]-45,height:23),16,shadow:transparent)
            x += widths[index]
        }
        NSColor.white.withAlphaComponent(0.08).setFill();NSRect(x:0,y:bounds.height-1,width:bounds.width,height:1).fill()
    }
}
final class CardView:NSView {
    unowned let board:Board
    let header = HeaderView(), scroll = NSScrollView(), document = NoteDocument()
    var buttons:[ActionButton] = [], login:ActionButton!
    override var isFlipped:Bool {true}
    init(board:Board) {
        self.board = board;super.init(frame:.zero);addSubview(header);addSubview(scroll)
        scroll.drawsBackground = false;scroll.hasVerticalScroller = true;scroll.hasHorizontalScroller = false
        scroll.autohidesScrollers = true;scroll.scrollerStyle = .overlay;scroll.scrollerKnobStyle = .light;scroll.documentView = document
        buttons = [ActionButton(symbol:"arrow.clockwise",tip:"立即刷新"){[weak board] in board?.sync()},
                   ActionButton(symbol:"pin",tip:"始终置顶"){[weak board] in board?.togglePin()},
                   ActionButton(symbol:"slider.horizontal.3",tip:"看板设置"){[weak board] in board?.openSettings()},
                   ActionButton(symbol:"xmark",tip:"隐藏到菜单栏"){[weak board] in board?.hide()}]
        buttons.forEach{header.addSubview($0)}
        login = ActionButton(title:"打开后台",tip:"重新登录"){[weak board] in board?.sync(login:true)};login.contentTintColor = accent;login.font = .systemFont(ofSize:10);addSubview(login)
    }
    required init?(coder:NSCoder){fatalError("init(coder:) not supported")}
    override func layout() {
        super.layout();let w = bounds.width,h = bounds.height
        header.frame = NSRect(x:19,y:17,width:w-38,height:28)
        for (index,button) in buttons.enumerated(){button.frame = NSRect(x:header.bounds.width-106+CGFloat(index)*28,y:1,width:25,height:26)}
        scroll.frame = NSRect(x:19,y:96,width:w-38,height:max(50,h-157))
        login.frame = NSRect(x:w-78,y:h-46,width:59,height:18)
        layoutNotes();needsDisplay = true
    }
    func reload() {
        document.subviews.forEach{$0.removeFromSuperview()}
        for note in board.snapshot?.notes.prefix(board.preferences.note_count) ?? [] {document.addSubview(NoteRow(note:note,transparent:board.preferences.theme == "transparent"))}
        buttons[1].contentTintColor = board.preferences.on_top ? accent : muted
        layoutNotes();needsDisplay = true
    }
    func layoutNotes() {
        let rowHeight:CGFloat = bounds.width >= 600 ? 56 : 71
        document.frame = NSRect(x:0,y:0,width:scroll.contentSize.width,height:max(scroll.contentSize.height,CGFloat(document.subviews.count)*rowHeight))
        for (index,row) in document.subviews.enumerated(){row.frame = NSRect(x:0,y:CGFloat(index)*rowHeight,width:document.bounds.width,height:rowHeight);row.needsDisplay = true}
    }
    override func draw(_ dirtyRect:NSRect) {
        let w = bounds.width,h = bounds.height,transparent = board.preferences.theme == "transparent"
        if !transparent {
            NSColor(calibratedRed:21/255,green:24/255,blue:33/255,alpha:0.84).setFill()
            let shape = NSBezierPath(roundedRect:bounds.insetBy(dx:0.5,dy:0.5),xRadius:16,yRadius:16);shape.fill()
            NSColor.white.withAlphaComponent(0.22).setStroke();shape.lineWidth = 1;shape.stroke()
        }
        accent.setFill();NSBezierPath(roundedRect:NSRect(x:19,y:20,width:22,height:22),xRadius:7,yRadius:7).fill()
        text("红",NSRect(x:24,y:23,width:17,height:20),12)
        text("创作看板",NSRect(x:48,y:25,width:90,height:18),11,weight:.semibold,shadow:transparent)
        text("最近文章",NSRect(x:19,y:63,width:170,height:29),18,weight:.semibold,shadow:transparent)
        let count = min(board.preferences.note_count,board.snapshot?.notes.count ?? 0)
        text("\(count) 篇 · 按发布时间",NSRect(x:w-145,y:72,width:126,height:17),10,muted,shadow:transparent)
        NSColor.white.withAlphaComponent(0.13).setFill();NSRect(x:19,y:h-59,width:w-38,height:1).fill()
        (board.state == "ready" ? NSColor.systemGreen : board.state == "syncing" ? NSColor.systemYellow : accent).setFill()
        NSBezierPath(ovalIn:NSRect(x:19,y:h-40,width:5,height:5)).fill()
        text(board.message,NSRect(x:30,y:h-44,width:w-118,height:16),10,shadow:transparent)
        let formatter = ISO8601DateFormatter()
        let date = board.snapshot.flatMap{formatter.date(from:$0.last_success)}
        let short = DateFormatter();short.dateFormat = "MM-dd HH:mm"
        text(date.map{"更新于 " + short.string(from:$0)} ?? "尚未同步",NSRect(x:19,y:h-25,width:190,height:15),9,muted,shadow:transparent)
        text(board.paused ? "自动同步已暂停" : "每 15 分钟更新",NSRect(x:w-110,y:h-25,width:91,height:15),9,muted,shadow:transparent)
    }
}
final class Board:NSObject,NSApplicationDelegate,NSWindowDelegate {
    let store:Store;var preferences:Preferences;var snapshot:Snapshot?
    var window:CardPanel!, view:CardView!, item:NSStatusItem!, settings:SettingsController?
    var worker:Process?, runID = "", poll:Timer?, schedule:Timer?, saveTask:DispatchWorkItem?
    var state = "initializing", message = "正在连接创作后台", paused = false,pending = false,pendingLogin = false
    override init(){let storage = Store();store = storage;preferences = validated(storage.read("settings.json",as:Preferences.self) ?? Preferences());snapshot = storage.read("snapshot.json",as:Snapshot.self);super.init()}
    func applicationDidFinishLaunching(_ notification:Notification) {
        let size = preferences.size
        window = CardPanel(contentRect:NSRect(x:0,y:0,width:size[0],height:size[1]),styleMask:[.borderless,.resizable],backing:.buffered,defer:false)
        window.minSize = NSSize(width:320,height:260);window.maxSize = NSSize(width:2400,height:2400)
        window.isOpaque = false;window.backgroundColor = .clear;window.hasShadow = false;window.hidesOnDeactivate = false
        window.isReleasedWhenClosed = false;window.delegate = self;window.collectionBehavior = [.canJoinAllSpaces,.fullScreenAuxiliary]
        window.title = "小红书 · 创作看板";view = CardView(board:self);window.contentView = view
        let area = (NSScreen.main ?? NSScreen.screens[0]).visibleFrame
        window.setFrameOrigin(preferences.position.map{NSPoint(x:$0[0],y:$0[1])} ?? NSPoint(x:area.maxX-window.frame.width-20,y:area.maxY-window.frame.height-30))
        clamp();apply();view.reload()
        item = NSStatusBar.system.statusItem(withLength:NSStatusItem.squareLength)
        item.button?.image = NSImage(systemSymbolName:"chart.bar",accessibilityDescription:"创作看板");makeMenu()
        if preferences.visible {window.orderFrontRegardless()}
        schedule = Timer.scheduledTimer(withTimeInterval:900,repeats:true){[weak self] _ in if self?.paused == false {self?.sync()}}
        sync()
    }
    func apply(){window.level = preferences.on_top ? .floating : .normal;window.ignoresMouseEvents = preferences.click_through;view?.reload();makeMenu()}
    func clamp() {
        let frame = window.frame
        let screen = NSScreen.screens.max {a,b in a.visibleFrame.intersection(frame).area < b.visibleFrame.intersection(frame).area} ?? NSScreen.main!
        let a = screen.visibleFrame
        window.setFrameOrigin(NSPoint(x:max(a.minX,min(frame.minX,a.maxX-min(frame.width,a.width))),y:max(a.minY,min(frame.minY,a.maxY-min(frame.height,a.height)))))
    }
    func save(){preferences.size = [window.frame.width,window.frame.height];preferences.position = [window.frame.minX,window.frame.minY];do {try store.write("settings.json",preferences)} catch {message = "设置保存失败";view.needsDisplay = true}}
    func queueSave(){saveTask?.cancel();let task = DispatchWorkItem{[weak self] in self?.save()};saveTask = task;DispatchQueue.main.asyncAfter(deadline:.now()+0.6,execute:task)}
    func windowDidMove(_ notification:Notification){queueSave()}
    func windowDidResize(_ notification:Notification){view.needsLayout = true;queueSave()}
    func show(){preferences.visible = true;clamp();window.orderFrontRegardless();if !preferences.click_through {window.makeKey();NSApp.activate(ignoringOtherApps:true)};save()}
    func hide(){preferences.visible = false;window.orderOut(nil);save()}
    func togglePin(){preferences.on_top.toggle();apply();save()}
    func openSettings(){if let settings = settings {settings.showWindow(nil);settings.window?.makeKeyAndOrderFront(nil);return};settings = SettingsController(board:self);settings?.showWindow(nil);settings?.window?.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true)}
    func sync(login:Bool = false) {
        if let worker = worker,worker.isRunning {pending = true;pendingLogin = pendingLogin || login;return}
        if paused && !login {return}
        paused = false;runID = UUID().uuidString;state = "syncing";message = "正在同步最近文章";view.needsDisplay = true
        let process = Process();process.executableURL = Bundle.main.executableURL
        process.arguments = ["--collector","--data-dir",store.directory.path,"--count",String(preferences.note_count),"--run-id",runID,"--parent-pid",String(ProcessInfo.processInfo.processIdentifier)] + (login ? ["--login"] : [])
        let expectedRun = runID;worker = process
        process.terminationHandler = {[weak self] _ in DispatchQueue.main.async {guard let self = self,self.runID == expectedRun else {return};self.readWorker();self.poll?.invalidate();self.worker = nil
            if self.state == "syncing" {self.state = "error";self.message = "同步进程未返回数据，已保留上次数据";self.view.needsDisplay = true}
            if self.pending {let login = self.pendingLogin;self.pending = false;self.pendingLogin = false;self.sync(login:login)}
        }}
        do {try process.run();poll = Timer.scheduledTimer(withTimeInterval:0.5,repeats:true){[weak self] _ in self?.readWorker()}}
        catch {worker = nil;state = "error";message = "无法启动同步进程";view.needsDisplay = true}
    }
    func readWorker() {
        guard let result = store.read("worker-status.json",as:WorkerStatus.self),result.run_id == runID else {return}
        if result.status == state && result.message == message {return}
        state = result.status;message = result.message;paused = ["login_required","verification_required"].contains(state)
        if state == "ready" {snapshot = store.read("snapshot.json",as:Snapshot.self);view.reload()}
        view.needsDisplay = true
    }
    func layoutPreset(size:Int,position:Int) {
        let area = (window.screen ?? NSScreen.main!).visibleFrame
        let sizes = [NSSize(width:320,height:360),NSSize(width:380,height:520),NSSize(width:520,height:700),NSSize(width:800,height:520)]
        if size > 0 {let s = sizes[size-1];window.setContentSize(NSSize(width:max(320,min(s.width,area.width-32)),height:max(260,min(s.height,area.height-32))))}
        let f = window.frame,left = area.minX+16,right = max(left,area.maxX-f.width-16),bottom = area.minY+16,top = max(bottom,area.maxY-f.height-16)
        let positions = [f.origin,NSPoint(x:left,y:top),NSPoint(x:right,y:top),NSPoint(x:left,y:bottom),NSPoint(x:right,y:bottom),NSPoint(x:area.midX-f.width/2,y:area.midY-f.height/2)]
        if position > 0 {window.setFrameOrigin(positions[position])};clamp();save()
    }
    func makeMenu() {
        guard item != nil else {return};let menu = NSMenu()
        func entry(_ title:String,_ action:String,_ tag:Int = 0,_ checked:Bool = false)->NSMenuItem {let e = NSMenuItem(title:title,action:NSSelectorFromString(action),keyEquivalent:"");e.target = self;e.tag = tag;e.state = checked ? .on : .off;return e}
        menu.addItem(entry("显示看板","menuShow:"));menu.addItem(entry("隐藏看板","menuHide:"));menu.addItem(entry("立即刷新","menuRefresh:"));menu.addItem(entry("打开后台 / 重新登录","menuLogin:"));menu.addItem(.separator())
        let themes = NSMenuItem(title:"主题",action:nil,keyEquivalent:"");let choices = NSMenu();choices.addItem(entry("玻璃","menuTheme:",0,preferences.theme == "glass"));choices.addItem(entry("透明","menuTheme:",1,preferences.theme == "transparent"));themes.submenu = choices;menu.addItem(themes)
        menu.addItem(entry("始终置顶","menuPin:",0,preferences.on_top));menu.addItem(entry("鼠标穿透","menuPass:",0,preferences.click_through));menu.addItem(entry("看板设置","menuSettings:"));menu.addItem(.separator());menu.addItem(entry("退出","menuQuit:"));item.menu = menu
    }
    @objc func menuShow(_ sender:Any?){show()};@objc func menuHide(_ sender:Any?){hide()};@objc func menuRefresh(_ sender:Any?){sync()};@objc func menuLogin(_ sender:Any?){sync(login:true)}
    @objc func menuTheme(_ sender:NSMenuItem){preferences.theme = sender.tag == 1 ? "transparent" : "glass";apply();save()}
    @objc func menuPin(_ sender:Any?){togglePin()};@objc func menuPass(_ sender:Any?){preferences.click_through.toggle();apply();save()}
    @objc func menuSettings(_ sender:Any?){openSettings()};@objc func menuQuit(_ sender:Any?){NSApp.terminate(nil)}
    func applicationShouldHandleReopen(_ sender:NSApplication,hasVisibleWindows:Bool)->Bool {show();return false}
    func applicationShouldTerminateAfterLastWindowClosed(_ sender:NSApplication)->Bool {false}
    func applicationWillTerminate(_ notification:Notification){saveTask?.cancel();save();worker?.terminate();poll?.invalidate();schedule?.invalidate()}
}
extension CGRect {var area:CGFloat {isNull ? 0 : width*height}}

final class SettingsController:NSWindowController,NSWindowDelegate {
    unowned let board:Board
    let theme = NSPopUpButton(frame:.zero,pullsDown:false), count = NSPopUpButton(frame:.zero,pullsDown:false),position = NSPopUpButton(frame:.zero,pullsDown:false),size = NSPopUpButton(frame:.zero,pullsDown:false)
    let pin = NSButton(checkboxWithTitle:"始终置顶",target:nil,action:nil),pass = NSButton(checkboxWithTitle:"鼠标穿透（在菜单栏中关闭）",target:nil,action:nil),auto = NSButton(checkboxWithTitle:"开机启动",target:nil,action:nil)
    var initialSize = 0
    init(board:Board) {
        self.board = board
        let panel = NSPanel(contentRect:NSRect(x:0,y:0,width:380,height:442),styleMask:[.titled,.closable],backing:.buffered,defer:false)
        panel.title = "看板设置";panel.appearance = NSAppearance(named:.darkAqua);panel.backgroundColor = NSColor(calibratedRed:21/255,green:24/255,blue:33/255,alpha:0.96);panel.isReleasedWhenClosed = false
        super.init(window:panel);panel.delegate = self;panel.center()
        guard let content = panel.contentView else {return}
        func label(_ title:String,_ frame:NSRect,_ font:CGFloat) {let label = NSTextField(labelWithString:title);label.font = .systemFont(ofSize:font);label.textColor = ink;label.frame = frame;content.addSubview(label)}
        label("创作看板",NSRect(x:19,y:404,width:200,height:20),11);label("看板设置",NSRect(x:19,y:365,width:200,height:27),18)
        theme.addItems(withTitles:["深色","透明"]);theme.selectItem(at:board.preferences.theme == "transparent" ? 1 : 0)
        count.addItems(withTitles:(1...50).map{"\($0) 篇"});count.selectItem(at:board.preferences.note_count-1)
        position.addItems(withTitles:["当前位置","左上角","右上角","左下角","右下角","居中"])
        size.addItems(withTitles:["当前尺寸","紧凑","中等","大号","宽幅"])
        let sizes:[[Double]] = [[320,360],[380,520],[520,700],[800,520]]
        initialSize = (sizes.firstIndex(of:board.preferences.size).map{$0+1}) ?? 0;size.selectItem(at:initialSize)
        for (index,pair) in zip(["主题","显示篇数","卡片位置","卡片大小"],[theme,count,position,size]).enumerated() {
            let y = CGFloat(316-index*42);label(pair.0,NSRect(x:19,y:y+6,width:150,height:18),12)
            pair.1.frame = NSRect(x:204,y:y,width:156,height:32);pair.1.font = .systemFont(ofSize:11.5);content.addSubview(pair.1)
        }
        pin.state = board.preferences.on_top ? .on : .off;pass.state = board.preferences.click_through ? .on : .off;auto.state = SMAppService.mainApp.status == .enabled ? .on : .off
        for (index,toggle) in [pin,pass,auto].enumerated(){toggle.frame = NSRect(x:19,y:146-CGFloat(index)*29,width:330,height:22);toggle.font = .systemFont(ofSize:11);content.addSubview(toggle)}
        let cancel = ActionButton(title:"取消"){[weak self] in self?.close()};cancel.frame = NSRect(x:222,y:23,width:50,height:28);content.addSubview(cancel)
        let save = ActionButton(title:"保存设置"){[weak self] in self?.save()};save.contentTintColor = accent;save.frame = NSRect(x:280,y:23,width:80,height:28);content.addSubview(save)
        label("修改后保存生效",NSRect(x:19,y:29,width:160,height:15),9)
    }
    required init?(coder:NSCoder){fatalError("init(coder:) not supported")}
    func save() {
        do {
            if auto.state == .on && SMAppService.mainApp.status != .enabled {try SMAppService.mainApp.register()}
            if auto.state == .off && SMAppService.mainApp.status == .enabled {try SMAppService.mainApp.unregister()}
            board.preferences.theme = theme.indexOfSelectedItem == 1 ? "transparent" : "glass";board.preferences.note_count = count.indexOfSelectedItem+1
            board.preferences.on_top = pin.state == .on
            board.layoutPreset(size:size.indexOfSelectedItem == initialSize ? 0 : size.indexOfSelectedItem,position:position.indexOfSelectedItem)
            let through = pass.state == .on;close();board.preferences.click_through = through;board.apply();board.save()
            if (board.snapshot?.notes.count ?? 0) < board.preferences.note_count {board.sync()}
        } catch {let alert = NSAlert();alert.messageText = "设置未保存";alert.informativeText = error.localizedDescription;alert.runModal()}
    }
    func windowWillClose(_ notification:Notification){board.settings = nil}
}
