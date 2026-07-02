import Foundation

enum RunnerError: Error, CustomStringConvertible {
    case timeout(String)
    case nonZeroExit(Int32, String)
    case missingResponse(String)
    case rpcError(String)

    var description: String {
        switch self {
        case .timeout(let bin): return "timeout: \(bin)"
        case .nonZeroExit(let code, let bin): return "exit \(code): \(bin)"
        case .missingResponse(let bin): return "missing JSON-RPC response: \(bin)"
        case .rpcError(let message): return "JSON-RPC error: \(message)"
        }
    }
}

/// Process 실행 지점은 이 파일 하나로 제한한다.
/// 현재 유일한 용도는 Codex app-server rate-limit read(JSON-RPC) — usage 집계는 로컬 로그 직파싱.
enum ProcessRunner {
    /// newline-delimited JSON-RPC 서버에 요청을 보내고 특정 id의 `result` JSON만 반환.
    /// Codex app-server가 stdout에 로그/notification을 섞어 내보낼 수 있어 line 단위로 필터링한다.
    static func runJSONRPC(
        binary: String,
        arguments: [String],
        inputLines: [String],
        responseID: Int,
        timeout: TimeInterval = 20
    ) async throws -> Data {
        let outURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("poketokenbar-\(UUID().uuidString).jsonl")
        FileManager.default.createFile(atPath: outURL.path, contents: nil)
        defer { try? FileManager.default.removeItem(at: outURL) }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: binary)
        process.arguments = arguments
        process.qualityOfService = .userInitiated
        let stdoutHandle = try FileHandle(forWritingTo: outURL)
        defer { try? stdoutHandle.close() }
        let stdinPipe = Pipe()
        process.standardOutput = stdoutHandle
        process.standardError = FileHandle.nullDevice
        process.standardInput = stdinPipe
        var stdinClosed = false
        func closeStdin() {
            guard !stdinClosed else { return }
            stdinPipe.fileHandleForWriting.closeFile()
            stdinClosed = true
        }
        defer {
            closeStdin()
            if process.isRunning { process.terminate() }
        }

        do {
            try process.run()
        } catch {
            throw error
        }

        let payload = inputLines.joined(separator: "\n") + "\n"
        stdinPipe.fileHandleForWriting.write(Data(payload.utf8))

        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            let raw = (try? Data(contentsOf: outURL)) ?? Data()
            if let response = try Self.jsonRPCResultData(in: raw, responseID: responseID) {
                return response
            }
            if !process.isRunning {
                break
            }
            try await Task.sleep(nanoseconds: 200_000_000)
        }

        if process.isRunning {
            process.terminate()
            DispatchQueue.global().asyncAfter(deadline: .now() + 2) {
                if process.isRunning {
                    kill(process.processIdentifier, SIGKILL)
                }
            }
            throw RunnerError.timeout(binary)
        }
        if process.terminationStatus != 0 {
            throw RunnerError.nonZeroExit(process.terminationStatus, binary)
        }
        throw RunnerError.missingResponse(binary)
    }

    private static func jsonRPCResultData(in raw: Data, responseID: Int) throws -> Data? {
        guard let text = String(data: raw, encoding: .utf8) else { return nil }
        for line in text.split(whereSeparator: \.isNewline) {
            guard let lineData = String(line).data(using: .utf8),
                  let object = try? JSONSerialization.jsonObject(with: lineData) as? [String: Any]
            else { continue }
            guard let id = object["id"] as? NSNumber, id.intValue == responseID else { continue }
            if let error = object["error"] as? [String: Any] {
                let message = error["message"] as? String ?? "\(error)"
                throw RunnerError.rpcError(message)
            }
            guard let result = object["result"] else { continue }
            guard JSONSerialization.isValidJSONObject(result) else { return nil }
            return try JSONSerialization.data(withJSONObject: result, options: [])
        }
        return nil
    }
}
