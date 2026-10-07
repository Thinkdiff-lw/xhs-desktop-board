import Cocoa

let args = CommandLine.arguments
func argument(_ key:String)->String? {guard let index = args.firstIndex(of:key),index+1 < args.count else {return nil};return args[index+1]}
if args.contains("--self-test") {
    do {try coreTests();exit(0)} catch {fputs("Core checks failed: \(error)\n",stderr);exit(1)}
}
let app = NSApplication.shared
app.setActivationPolicy(.accessory)
if args.contains("--collector") {
    guard let path = argument("--data-dir"),let run = argument("--run-id"),let count = Int(argument("--count") ?? "5"),(1...50).contains(count) else {exit(2)}
    let collector = BrowserCollector(store:Store(URL(fileURLWithPath:path,isDirectory:true)),runID:run,limit:count,interactive:args.contains("--login"),parentPID:Int32(argument("--parent-pid") ?? "0") ?? 0)
    collector.start();withExtendedLifetime(collector){app.run()}
} else {
    let board = Board();app.delegate = board;withExtendedLifetime(board){app.run()}
}
