// Sends a process the quit Apple event macOS sends it, and prints the error number its reply
// carries, 0 for none. Used by the tray-install job on macOS, which has no logout to give the
// tray: `send-quit <pid>` is a quit from the Dock or AppleScript, and `send-quit <pid> logout` is
// the one a logout sends, the quit with kAEQuitReason set to kAELogOut.
//
// A quit the app refuses is answered userCanceledErr (-128), which is how a logout learns it was
// refused, and cancelled. A quit the app lets through ends it before it replies, so the send
// fails on a connection that has gone instead, which is printed as its error number too.

import Foundation

let arguments = CommandLine.arguments
guard arguments.count >= 2, let pid = Int32(arguments[1]) else {
    FileHandle.standardError.write("usage: send-quit <pid> [logout]\n".data(using: .utf8)!)
    exit(2)
}

let event = NSAppleEventDescriptor(
    eventClass: AEEventClass(kCoreEventClass),
    eventID: AEEventID(kAEQuitApplication),
    targetDescriptor: NSAppleEventDescriptor(processIdentifier: pid),
    returnID: AEReturnID(kAutoGenerateReturnID),
    transactionID: AETransactionID(kAnyTransactionID))

if arguments.count >= 3, arguments[2] == "logout" {
    event.setParam(
        NSAppleEventDescriptor(enumCode: OSType(kAELogOut)),
        forKeyword: AEKeyword(kAEQuitReason))
}

do {
    let reply = try event.sendEvent(options: [.waitForReply], timeout: 10)
    print(reply.paramDescriptor(forKeyword: AEKeyword(keyErrorNumber))?.int32Value ?? 0)
} catch let error as NSError {
    print(error.code)
}
