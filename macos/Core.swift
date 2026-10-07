import Foundation
import CoreFoundation

let creatorURL = URL(string: "https://creator.xiaohongshu.com/new/note-manager")!
let postedPath = "/api/galaxy/v2/creator/note/user/posted"

struct Note: Codable {
    let id: String
    let title: String
    let published_at: String
    let published_timestamp: Double
    var read_label: String
    let read_count: Int?
    let comment_count: Int?
    let collect_count: Int?
    let sticky: Bool
}
struct Snapshot: Codable { var notes: [Note]; var last_success: String }
struct Preferences: Codable {
    var visible = true, on_top = false, click_through = false
    var theme = "glass", note_count = 5
    var position: [Double]? = nil
    var size: [Double] = [380, 520]
}
struct WorkerStatus: Codable { let run_id: String; let status: String; let message: String }
enum BoardError: Error {
    case login, verification, invalid(String)
    var state: String { switch self {case .login: return "login_required";case .verification: return "verification_required";case .invalid: return "error"} }
    var message: String { switch self {case .login: return "请登录小红书创作后台";case .verification: return "后台需要验证，请打开后台";case .invalid(let message): return message} }
}
final class Store {
    let directory: URL
    init(_ directory: URL? = nil) {
        self.directory = directory ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("XhsDesktopBoard", isDirectory: true)
        try? FileManager.default.createDirectory(at: self.directory, withIntermediateDirectories: true)
    }
    func read<T: Decodable>(_ name: String, as: T.Type) -> T? {
        guard let data = try? Data(contentsOf: directory.appendingPathComponent(name)) else { return nil }
        return try? JSONDecoder().decode(T.self, from: data)
    }
    func write<T: Encodable>(_ name: String, _ value: T) throws {
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(value).write(to: directory.appendingPathComponent(name), options: .atomic)
    }
}
func official(_ url: URL) -> Bool {
    guard url.scheme == "https", let host = url.host?.lowercased() else { return false }
    return host == "xiaohongshu.com" || host.hasSuffix(".xiaohongshu.com")
}
func postedPage(_ url: URL) -> Int? {
    guard official(url), url.path == postedPath,
          let query = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems,
          query.filter({$0.name == "tab"}).map({$0.value}) == ["1"] else { return nil }
    let page = Int(query.first(where: {$0.name == "page"})?.value ?? "0")
    return page.flatMap { $0 >= 0 ? $0 : nil }
}
func exactCount(_ value: Any?) -> Int? {
    if let number = value as? NSNumber {
        guard CFGetTypeID(number) != CFBooleanGetTypeID() else { return nil }
        let n = number.doubleValue
        return n.isFinite && n >= 0 && n.rounded() == n && n < Double(Int.max) ? Int(n) : nil
    }
    if let text = value as? String {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed.allSatisfy({$0.isNumber && $0.isASCII}) else {return nil}
        return Int(trimmed).flatMap { $0 >= 0 ? $0 : nil }
    }
    return nil
}
func publishedTime(_ value: Any?) throws -> (String, Double) {
    var seconds: Double? = nil
    if let number = value as? NSNumber, CFGetTypeID(number) != CFBooleanGetTypeID() { seconds = number.doubleValue }
    else if let text = value as? String { seconds = Double(text) }
    var date: Date?
    if var s = seconds {
        if s > 100_000_000_000 { s /= 1000 }
        if s.isFinite && s > 0 && s < 253402300800 { date = Date(timeIntervalSince1970: s) }
    } else if let text = value as? String {
        let normalized = text.replacingOccurrences(of: "发布于", with: "").replacingOccurrences(of: "/", with: "-").trimmingCharacters(in: .whitespacesAndNewlines)
        let iso = ISO8601DateFormatter(); date = iso.date(from: normalized)
        if date == nil {
            let parser = DateFormatter(); parser.locale = Locale(identifier: "en_US_POSIX"); parser.timeZone = TimeZone(secondsFromGMT: 28800)
            for pattern in ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"] { parser.dateFormat = pattern; if let parsed = parser.date(from: normalized) {date = parsed; break} }
        }
    }
    guard let result = date else { throw BoardError.invalid("文章发布时间缺失或格式变化") }
    return (ISO8601DateFormatter().string(from: result), result.timeIntervalSince1970)
}
func parsePage(_ payload: Any, status: Int, label: String) throws -> ([Note], Int) {
    if status == 401 {throw BoardError.login}
    if status == 403 || status == 429 {throw BoardError.verification}
    guard status == 200, let body = payload as? [String: Any] else {throw BoardError.invalid("后台响应异常")}
    let code = (body["code"] as? NSNumber)?.intValue ?? 0
    let message = (body["message"] ?? body["msg"]) as? String ?? ""
    if [-100,-101,-1,10001].contains(code) && ["登录","登陆","login","session"].contains(where: {message.lowercased().contains($0)}) {throw BoardError.login}
    if ["验证码","安全验证","访问频繁"].contains(where: {message.contains($0)}) {throw BoardError.verification}
    guard code == 0, (body["success"] as? Bool) != false else {throw BoardError.invalid("后台未返回文章数据")}
    guard let data = (body["data"] ?? body) as? [String: Any], let raw = data["notes"] as? [[String: Any]],
          let pageValue = data["page"] as? NSNumber, CFGetTypeID(pageValue) != CFBooleanGetTypeID(),
          pageValue.doubleValue == Double(pageValue.intValue), pageValue.intValue >= -1 else {throw BoardError.invalid("文章列表或分页结构变化")}
    let notes = try raw.compactMap { row -> Note? in
        guard let id = row["id"] as? String, !id.isEmpty else {throw BoardError.invalid("文章 ID 缺失")}
        if (exactCount(row["schedule_post_time"]) ?? 0) != 0 || [2,3,4].contains((row["tab_status"] as? Int) ?? 0) {return nil}
        let (date,time) = try publishedTime(row["time"])
        return Note(id: id, title: row["display_title"] as? String ?? "无标题笔记", published_at: date, published_timestamp: time,
                    read_label: label, read_count: exactCount(row["view_count"]), comment_count: exactCount(row["comments_count"]),
                    collect_count: exactCount(row["collected_count"]), sticky: row["sticky"] as? Bool ?? false)
    }
    return (notes, pageValue.intValue)
}
func recent(_ notes: [Note], limit: Int) -> [Note] {
    var unique: [String: Note] = [:]; notes.forEach {unique[$0.id] = $0}
    return Array(unique.values.sorted { $0.published_timestamp == $1.published_timestamp ? $0.id > $1.id : $0.published_timestamp > $1.published_timestamp }.prefix(max(1, min(50,limit))))
}
func validated(_ preferences: Preferences) -> Preferences {
    var p = preferences
    p.note_count = max(1,min(50,p.note_count)); p.theme = p.theme == "transparent" ? "transparent" : "glass"
    if p.size.count != 2 || !p.size.allSatisfy({$0.isFinite}) {p.size = [380,520]}
    p.size = [max(320,min(2400,p.size[0])),max(260,min(2400,p.size[1]))]
    if let pos = p.position, pos.count != 2 || !pos.allSatisfy({$0.isFinite}) {p.position = nil}
    return p
}
func coreTests() throws {
    func require(_ condition: @autoclosure () -> Bool, _ label: String) throws {if !condition() {throw BoardError.invalid("FAIL: " + label)}}
    try require(exactCount(0) == 0, "zero"); try require(exactCount(true) == nil, "bool"); try require(exactCount("1.2万") == nil, "rounded")
    try require(exactCount(-1) == nil, "negative"); try require(exactCount("42") == 42, "count string")
    try require(postedPage(URL(string:"https://creator.xiaohongshu.com" + postedPath + "?tab=1&page=0")!) == 0,"posted tab")
    try require(postedPage(URL(string:"https://example.invalid" + postedPath + "?tab=1&page=0")!) == nil,"origin")
    try require(postedPage(URL(string:"https://creator.xiaohongshu.com" + postedPath + "?tab=0")!) == nil,"draft")
    let row: [String: Any] = ["id":"sample","display_title":"测试文章","time":1700000000000,"view_count":0,"comments_count":2,"sticky":true]
    let (notes,page) = try parsePage(["code":0,"data":["notes":[row],"page":-1]], status:200,label:"阅读量")
    try require(page == -1 && notes.count == 1,"page"); try require(notes[0].read_count == 0 && notes[0].collect_count == nil,"missing")
    try require(abs(notes[0].published_timestamp - 1700000000) < 1,"milliseconds")
    var newer = row; newer["id"] = "new"; newer["time"] = 1700000010; newer["sticky"] = false
    let (more,_) = try parsePage(["data":["notes":[newer],"page":-1]],status:200,label:"浏览量")
    try require(recent(notes + more + more,limit:1)[0].id == "new","pin and dedupe")
    do {_ = try parsePage([String:Any](),status:401,label:"浏览量"); throw BoardError.invalid("401 accepted")} catch BoardError.login {}
    do {_ = try parsePage([String:Any](),status:403,label:"浏览量"); throw BoardError.invalid("403 accepted")} catch BoardError.verification {}
    var p = Preferences(); p.size = [Double.nan, 10]; p.note_count = 100
    try require(validated(p).size == [380,520] && validated(p).note_count == 50,"preferences")
    print("PASS: 15 macOS core checks")
}
