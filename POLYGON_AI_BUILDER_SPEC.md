---
title: "ĐẶC TẢ SẢN PHẨM VÀ KỸ THUẬT"
subtitle: "Polygon AI Problem Builder — Ứng dụng local tạo bài và đồng bộ trực tiếp lên Codeforces Polygon"
author: "Tài liệu đặc tả dành cho Codex"
date: "02/08/2026"
lang: vi-VN
---

# 1. Thông tin tài liệu

| Thuộc tính | Giá trị |
|---|---|
| Tên sản phẩm | Polygon AI Problem Builder |
| Phiên bản đặc tả | 1.0 |
| Trạng thái | Đã chốt để triển khai phiên bản đầu tiên hoàn chỉnh |
| Nền tảng mục tiêu | Windows 11 x64 |
| Cách chạy | Ứng dụng local khởi động một web server trên `127.0.0.1` và mở giao diện bằng trình duyệt mặc định |
| Người dùng mục tiêu | Một người dùng trên một máy tính |
| Công nghệ chính | .NET 10, ASP.NET Core Blazor Web App, EF Core 10, SQLite, Monaco Editor, MathJax, GNU C++17 |
| AI hỗ trợ | OpenAI và Gemini, người dùng tự chọn provider và model |
| Hệ thống đích | Codeforces Polygon API |

Tài liệu này là **nguồn yêu cầu chính thức** cho việc triển khai. Khi có xung đột giữa mã nguồn, suy đoán kỹ thuật và tài liệu này, phải ưu tiên tài liệu này, trừ khi một API bên ngoài đã thay đổi và việc giữ nguyên yêu cầu là bất khả thi. Trong trường hợp đó, phải ghi rõ thay đổi trong `IMPLEMENTATION_NOTES.md`.

# 2. Tầm nhìn sản phẩm

Polygon AI Problem Builder giúp người ra đề tạo một bài lập trình từ ý tưởng ban đầu đến khi có standard package trên Polygon, thông qua một quy trình trực quan gồm năm màn hình chính.

Ứng dụng phải cung cấp trải nghiệm trò chuyện gần giống AI bản web:

1. Người dùng trao đổi nhiều lượt với AI để hình thành và chỉnh sửa đề.
2. AI tự động cập nhật statement có cấu trúc.
3. AI sinh `solution.cpp` và `generate.cpp`.
4. Ứng dụng biên dịch local để phát hiện lỗi cú pháp và cho AI tự sửa.
5. Test số 1 được chạy local để tạo sample input/output.
6. Người dùng kiểm tra checker, script và điểm test.
7. Chỉ khi nhấn **Đồng bộ lên Polygon**, ứng dụng mới tạo problem và gửi toàn bộ dữ liệu.
8. Sau khi đồng bộ thành công, ứng dụng tự commit và build standard package có verify.

# 3. Mục tiêu và phạm vi

## 3.1. Mục tiêu bắt buộc

- Chạy local trên Windows, giao diện mở bằng trình duyệt.
- Có màn hình Settings riêng để lưu API key và cấu hình AI.
- Hỗ trợ OpenAI và Gemini.
- Cho phép đổi provider và model trong cùng một cuộc hội thoại.
- Mỗi problem local có đúng một cuộc hội thoại riêng.
- Hỗ trợ prompt văn bản, ảnh và file đính kèm.
- AI tự động cập nhật statement và tạo lịch sử phiên bản có diff/undo.
- Statement chỉ gồm `title`, `legend`, `input`, `output`, `note`.
- Statement mặc định có language code là `english` trên Polygon.
- Không đưa sample trực tiếp vào phần chỉnh sửa statement.
- Sinh và chỉnh sửa `solution.cpp`, `generate.cpp`.
- Dùng GNU C++17.
- Đóng gói sẵn compiler và `testlib.h`.
- Compile local solution và generator.
- Tự động gửi lỗi compiler cho AI và thử sửa tối đa ba lần.
- Chỉ chạy local test số 1 để tạo sample.
- Không chạy đủ 100 test local.
- Tạo problem mới trên Polygon; không chỉnh sửa problem cũ tùy ý.
- Kiểm tra trùng tên khi rời Màn hình 1 và kiểm tra lại trước khi tạo problem.
- Chỉ đồng bộ khi người dùng nhấn nút.
- Tự commit với commit message mặc định trống.
- Tự build standard package với verify.
- Không tải package ZIP về máy.

## 3.2. Ngoài phạm vi phiên bản này

- Validator.
- Brute-force solution.
- Wrong solutions để đánh giá test.
- Stress test toàn bộ local.
- Chỉnh sửa một Polygon problem cũ không do project local hiện tại tạo.
- Nhiều người dùng, đăng nhập nội bộ, phân quyền.
- macOS, Linux hoặc mobile.
- Custom checker do AI tự viết.
- Upload trực tiếp lên Codeforces contest.
- Tải package ZIP từ Polygon.
- Tự động xóa Polygon problem khi đồng bộ thất bại.

# 4. Nguyên tắc sản phẩm

1. **Local-first:** toàn bộ project, chat, code và lịch sử được lưu local trước khi đồng bộ.
2. **Không tự đồng bộ:** AI không được gọi Polygon API; chỉ ứng dụng gọi khi người dùng nhấn nút.
3. **AI có thể tự cập nhật statement nhưng phải có lịch sử:** mọi lần sửa đều tạo version và có Undo.
4. **Không làm mất dữ liệu:** lỗi AI, lỗi mạng, lỗi compile hoặc lỗi Polygon không được xóa nội dung hiện tại.
5. **Không giả vờ thành công:** chỉ hiện `PASSED`, `Synced`, `Committed` hoặc `Package built` khi có bằng chứng thực tế.
6. **Một project, một conversation:** lịch sử chat thuộc về problem local cụ thể.
7. **Provider-neutral:** dữ liệu nội bộ không phụ thuộc định dạng riêng của OpenAI hoặc Gemini.
8. **External API isolation:** OpenAI, Gemini và Polygon phải nằm sau các service interface riêng.
9. **Có khả năng tiếp tục sau lỗi:** nếu Polygon problem đã được tạo nhưng các bước sau thất bại, project phải lưu `problemId` để Resume Sync.

# 5. Kiến trúc tổng thể

```text
Trình duyệt mặc định
        │
        │ HTTP/SignalR trên 127.0.0.1
        ▼
ASP.NET Core Blazor Web App (.NET 10)
        │
        ├── Application Services
        │     ├── Project workflow
        │     ├── Conversation orchestration
        │     ├── Statement versioning
        │     ├── Code generation
        │     ├── Local compile/run
        │     └── Polygon sync state machine
        │
        ├── Provider Adapters
        │     ├── OpenAI Responses API
        │     ├── Gemini Interactions API
        │     └── Polygon API
        │
        ├── Persistence
        │     ├── SQLite database
        │     ├── Project files
        │     └── Encrypted secrets file
        │
        └── Bundled Toolchain
              ├── MinGW-w64 g++.exe
              ├── Runtime DLLs
              ├── testlib.h
              └── Standard checker sources
```

## 5.1. Lựa chọn công nghệ bắt buộc

- **Runtime/framework:** .NET 10 LTS.
- **UI:** ASP.NET Core Blazor Web App dùng Interactive Server rendering.
- **Database:** SQLite qua EF Core 10.
- **Code editor:** Monaco Editor qua JavaScript interop.
- **Markdown hiển thị chat:** Markdig hoặc thư viện tương đương có sanitize HTML.
- **Math/LaTeX preview:** MathJax 3.
- **HTTP:** `HttpClientFactory` và typed clients.
- **Logging:** `Microsoft.Extensions.Logging` + rolling file provider.
- **Testing:** xUnit, FluentAssertions hoặc assertion library tương đương, Playwright cho E2E.
- **Distribution:** self-contained `win-x64`, không yêu cầu người dùng cài .NET.

## 5.2. Cấu trúc solution đề xuất

```text
PolygonAiBuilder.sln
src/
  PolygonAiBuilder.Web/              # Blazor UI và host
  PolygonAiBuilder.Application/      # Use cases, orchestration, DTOs
  PolygonAiBuilder.Domain/           # Entities, value objects, enums
  PolygonAiBuilder.Infrastructure/   # EF Core, file store, secrets, process runner
  PolygonAiBuilder.Integrations/     # OpenAI, Gemini, Polygon clients
  PolygonAiBuilder.Contracts/        # JSON schemas/tool contracts dùng chung
tests/
  PolygonAiBuilder.UnitTests/
  PolygonAiBuilder.IntegrationTests/
  PolygonAiBuilder.E2ETests/
toolchain/
  mingw64/
  testlib/
  checkers/
scripts/
  acquire-toolchain.ps1
  verify-toolchain.ps1
  publish-win-x64.ps1
data/
projects/
logs/
AGENTS.md
README.md
IMPLEMENTATION_NOTES.md
```

# 6. Cấu trúc thư mục runtime

```text
PolygonAiBuilder/
  PolygonAiBuilder.exe
  appsettings.json
  data/
    polygon-builder.db
    secrets.local.json
  projects/
    <project-id>/
      project.json
      attachments/
      statement/
      code/
        solution.cpp
        generate.cpp
      samples/
        sample-1.in
        sample-1.out
      temp/
  toolchain/
    mingw64/bin/g++.exe
    mingw64/bin/*.dll
    testlib/testlib.h
    checkers/ncmp.cpp
    checkers/wcmp.cpp
  logs/
    app-YYYYMMDD.log
```

- `data/secrets.local.json` chứa dữ liệu đã mã hóa, không phải plaintext.
- `projects/` chứa dữ liệu có thể đọc được của từng bài.
- File tạm khi compile/run phải nằm trong `projects/<id>/temp/` và được dọn sau khi hoàn tất.
- Ứng dụng chỉ bind vào `127.0.0.1`, không bind `0.0.0.0`.

# 7. Mô hình dữ liệu

## 7.1. ProblemProject

```text
Id: Guid
InternalName: string
Status: Draft | Ready | Syncing | Synced | SyncFailed
CreatedAt: DateTimeOffset
UpdatedAt: DateTimeOffset
CurrentScreen: int (1..5)
PolygonProblemId: int?
PolygonRevision: int?
PolygonSyncPhase: enum
SelectedProvider: OpenAI | Gemini
SelectedModel: string
ConversationId: Guid
StatementId: Guid
SolutionArtifactId: Guid?
GeneratorArtifactId: Guid?
TestConfigurationId: Guid
```

## 7.2. GeneralInfo

```text
ProblemProjectId: Guid
InputFile: string = "stdin"
OutputFile: string = "stdout"
TimeLimitMs: int = 1000
MemoryLimitMb: int = 256
```

Ràng buộc:

- `InternalName`: bắt buộc, trim khoảng trắng, không rỗng.
- `InputFile`, `OutputFile`: 1–64 ký tự UTF-8.
- Input và output không được giống nhau nếu bỏ qua hoa/thường.
- `TimeLimitMs`: 250–15000, chia hết cho 50.
- `MemoryLimitMb`: 4–1024.

## 7.3. Statement

```text
Id: Guid
ProblemProjectId: Guid
Language: string = "english"
Title: string
Legend: string
Input: string
Output: string
Note: string
CurrentVersion: int
IsCodeStale: bool
UpdatedAt: DateTimeOffset
```

## 7.4. StatementVersion

```text
Id: Guid
StatementId: Guid
VersionNumber: int
Title: string
Legend: string
Input: string
Output: string
Note: string
ChangedBy: User | AI
Provider: string?
Model: string?
MessageId: Guid?
CreatedAt: DateTimeOffset
```

## 7.5. Conversation và Message

```text
Conversation
- Id
- ProblemProjectId
- RollingSummary
- CreatedAt
- UpdatedAt

Message
- Id
- ConversationId
- Role: System | User | Assistant | Tool
- ContentMarkdown
- Provider
- Model
- Status: Streaming | Completed | Failed | Cancelled
- CreatedAt
- ParentMessageId?
- ProviderResponseId?
```

## 7.6. Attachment

```text
Id
MessageId
OriginalFileName
StoredFileName
MimeType
SizeBytes
Sha256
LocalPath
ExtractedTextPath?
ProviderFileId?
CreatedAt
```

## 7.7. CodeArtifact

```text
Id
ProblemProjectId
Type: Solution | Generator
FileName
Content
Version
GeneratedFromStatementVersion
IsStale
LastCompileStatus
LastCompileOutput
UpdatedAt
```

## 7.8. TestConfiguration

```text
Id
ProblemProjectId
TestsetName = "tests"
TestCount = 100
ScorePerTest = 1.0
PointsEnabled = true
Checker = "ncmp.cpp" hoặc "wcmp.cpp"
Script
SampleTestIndex = 1
UseSampleInStatement = true
CommitMessage = ""
```

## 7.9. SyncOperationLog

Lưu từng bước Polygon để có thể resume:

```text
Id
ProblemProjectId
Phase
Endpoint
StartedAt
CompletedAt?
Success
RequestFingerprint
RemoteResultSummary
ErrorCode?
ErrorMessage?
RetryCount
```

# 8. Settings

Settings là màn hình riêng, không nằm trong wizard năm bước.

## 8.1. Giao diện

### OpenAI

- API Key (password input, có nút hiện/ẩn).
- Model mặc định.
- Nút `Refresh models`.
- Nút `Test connection`.
- Trạng thái kết nối.

### Gemini

- API Key.
- Model mặc định.
- Nút `Refresh models`.
- Nút `Test connection`.
- Trạng thái kết nối.

### Polygon

- API Key.
- API Secret.
- Nút `Test connection`.
- Trạng thái kết nối.

### Local toolchain

- Đường dẫn compiler bundled, chỉ đọc.
- Phiên bản `g++`.
- Trạng thái hỗ trợ GNU C++17.
- Trạng thái `testlib.h`.
- Trạng thái checker source.
- Nút `Verify toolchain`.
- Nút `Repair toolchain` nếu thiếu file.

## 8.2. Lưu secrets

- File: `data/secrets.local.json`.
- Giá trị phải được mã hóa bằng Windows DPAPI với scope CurrentUser.
- Không ghi key vào SQLite, log, exception message hoặc telemetry.
- Khi hiển thị lại chỉ hiện dạng masked.
- Ghi file theo cơ chế atomic: ghi file tạm rồi rename.
- `secrets.local.json` phải có trong `.gitignore`.

Cấu trúc logic:

```json
{
  "version": 1,
  "openAiApiKeyEncrypted": "...",
  "geminiApiKeyEncrypted": "...",
  "polygonApiKeyEncrypted": "...",
  "polygonApiSecretEncrypted": "..."
}
```

## 8.3. Model discovery

- Luôn dùng model người dùng chọn.
- Không tự động đổi model vì chi phí, tốc độ hoặc lỗi.
- Danh sách model phải được lấy từ provider khi có thể.
- Chỉ hiển thị model phù hợp cho text/chat; đánh dấu khả năng image/file/tool calling nếu provider cung cấp metadata.
- Cache danh sách model và thời điểm refresh.
- Khi API model list không cung cấp đầy đủ capability, dùng allowlist/denylist cấu hình có thể cập nhật, không rải tên model trong UI code.
- Nếu model được lưu trước đó không còn tồn tại, giữ tên nhưng hiển thị `Unavailable` và yêu cầu chọn lại trước khi gửi prompt.

# 9. Điều hướng chung

Wizard có progress header:

```text
1. General Info  →  2. AI Workspace  →  3. Statement  →  4. Code  →  5. Tests & Sync
```

Mỗi màn hình có:

- Nút **Quay lại** bên trái.
- Nút **Tiếp theo** bên phải.
- Autosave.
- Chỉ disable nút khi có thao tác không thể hủy đang chạy.
- Quay lại không làm mất dữ liệu và không tự gọi AI.
- Tiếp theo lưu dữ liệu rồi kiểm tra điều kiện màn hình.
- Reload trình duyệt phải mở lại đúng project và đúng màn hình.

# 10. Màn hình 1 — General Info

## 10.1. Trường dữ liệu

1. **Problem name** — tên nội bộ lưu trên Polygon.
2. **Input file** — mặc định `stdin`.
3. **Output file** — mặc định `stdout`.
4. **Time limit** — mặc định `1000 ms`.
5. **Memory limit** — mặc định `256 MB`.

Không hiển thị title, chủ đề, độ khó hoặc constraints tại đây. Những nội dung đó được trao đổi ở Màn hình 2.

## 10.2. Hành vi khi nhấn Tiếp theo

1. Validate local.
2. Kiểm tra Polygon credentials đã cấu hình và giải mã được.
3. Gọi `problems.list` với `name`.
4. So sánh kết quả chính xác với tên đã trim.
5. Nếu tồn tại, hiển thị lỗi và không chuyển màn hình.
6. Nếu không tồn tại, lưu trạng thái `NameAvailableCheckedAt` và chuyển Màn hình 2.
7. **Không gọi `problem.create` ở màn hình này.**

Thông báo lỗi:

```text
Problem “<name>” đã tồn tại trên Polygon.
Tool này chỉ tạo problem mới. Vui lòng chọn tên khác.
```

Nếu Polygon không truy cập được, không được coi là tên còn trống. Hiển thị lỗi kết nối và cho Retry.

# 11. Màn hình 2 — AI Workspace

## 11.1. Bố cục

Màn hình chia hai cột:

- **Cột trái:** preview statement hiện tại.
- **Cột phải phía trên:** lịch sử phản hồi AI giống ứng dụng chat web.
- **Cột phải phía dưới:** composer để nhập prompt, chọn file và gửi.

Tỷ lệ mặc định 45% / 55%, cho phép kéo resize.

## 11.2. Thanh công cụ chat

- Provider dropdown: OpenAI/Gemini.
- Model dropdown: model đã chọn.
- Nút refresh model.
- Nút new response/regenerate cho assistant message gần nhất.
- Nút stop generation.
- Nút attach file.
- Nút send.

## 11.3. Provider switching

Người dùng được đổi provider trong lúc chat. Khi đổi:

```text
Bạn đang chuyển từ <provider A> sang <provider B>.
Lịch sử hội thoại và các tệp liên quan sẽ được gửi cho provider mới để tiếp tục cùng ngữ cảnh.
[Hủy] [Chuyển provider]
```

- Lịch sử local không bị tách thành hai chat.
- Tin nhắn phải lưu provider/model đã tạo.
- Adapter chuyển lịch sử chuẩn nội bộ thành request của provider hiện tại.
- Nếu lịch sử vượt context, dùng rolling summary + các message gần nhất, không cắt message giữa chừng.

## 11.4. File và ảnh

Hỗ trợ tối thiểu:

- Ảnh: `.png`, `.jpg`, `.jpeg`, `.webp`.
- Tài liệu: `.pdf`, `.txt`, `.md`.
- Source: `.cpp`, `.c`, `.h`, `.hpp`, `.cs`, `.json`.
- Archive: `.zip` với xử lý an toàn.

Quy tắc:

- Tối đa 20 MB/file và 50 MB/message ở mức ứng dụng.
- Không thực thi file upload.
- ZIP phải chống path traversal, giới hạn số file và tổng dung lượng sau giải nén.
- File text được normalize UTF-8 khi có thể.
- Nếu model không hỗ trợ loại file, báo trước khi gửi.
- Mọi attachment được copy vào thư mục project và tính SHA-256.

## 11.5. Streaming

- Assistant message xuất hiện ngay và được append token/chunk theo thời gian thực.
- Refresh trang giữa lúc stream không làm corrupt DB.
- Cancel lưu message là `Cancelled`.
- Provider error lưu `Failed`, giữ nội dung đã stream nếu có.

## 11.6. Hành vi AI trong workspace

AI phải hỗ trợ quy trình bốn bước gốc:

1. Tiếp nhận yêu cầu và định hình ý tưởng.
2. Phác thảo statement và tiếp tục chỉnh qua chat.
3. Khi statement đủ thông tin, tự cập nhật dữ liệu có cấu trúc.
4. Sau này sinh code/test và self-audit.

AI phản hồi tự nhiên, không ép mọi message thành JSON. Khi cần sửa statement, model gọi internal tool `update_statement`.

## 11.7. Tool `update_statement`

Schema:

```json
{
  "title": "string hoặc null nếu không đổi",
  "legend": "string hoặc null nếu không đổi",
  "input": "string hoặc null nếu không đổi",
  "output": "string hoặc null nếu không đổi",
  "note": "string hoặc null nếu không đổi",
  "changeSummary": "mô tả ngắn"
}
```

Quy tắc thực thi:

- Merge theo field, không ghi null đè dữ liệu.
- Validate LaTeX cơ bản trước khi lưu.
- Tạo `StatementVersion` mới.
- Đánh dấu code stale nếu statement đã có code.
- Hiện toast/card:

```text
Statement đã được AI cập nhật.
[Xem thay đổi] [Hoàn tác]
```

- `Xem thay đổi` mở diff theo từng field.
- `Hoàn tác` tạo version mới bằng nội dung version trước, không xóa lịch sử.

## 11.8. Preview bên trái

- Hiển thị title, legend, input, output, note.
- Không hiển thị sample ở màn hình này.
- Render công thức bằng MathJax.
- Preview là gần đúng; Polygon render là nguồn chính xác cuối cùng.
- Nếu LaTeX lỗi, preview hiện lỗi tại khu vực tương ứng nhưng vẫn giữ text editor.

# 12. Màn hình 3 — Statement Editor

## 12.1. Các phần duy nhất

- `Title`
- `Legend`
- `Input`
- `Output`
- `Note`

Không thêm sample, scoring, tutorial, interaction hoặc validator.

## 12.2. Layout

- Bên trái: editor theo tab/accordion cho năm trường.
- Bên phải: preview realtime.
- Cho phép kéo resize.
- Có `Undo`, `Redo`, `Restore AI version`, `View history`.

## 12.3. Quy tắc LaTeX Polygon

Mọi text gửi Polygon phải là LaTeX tương thích Polygon. Ứng dụng phải:

- Cho phép inline math `$...$` và display math `$$...$$` nếu preview hỗ trợ.
- Hỗ trợ các lệnh phổ biến như `\textbf{}`, `\textit{}`, `\texttt{}`.
- Hỗ trợ `itemize`, `enumerate`, `tabular` ở mức preview.
- Không tự động chuyển thành Markdown trước khi gửi Polygon.
- Không escape toàn bộ backslash của người dùng.
- Validate dấu ngoặc `{}` cân bằng, môi trường `\begin`/`\end` khớp và delimiter math cơ bản.
- Chỉ cảnh báo với lệnh lạ; không tự xóa dữ liệu.

Language cố định mặc định:

```text
english
```

Nội dung có thể viết tiếng Việt; language ở đây là slot statement trên Polygon.

## 12.4. Chuyển sang Màn hình 4

- Title, legend, input, output phải không rỗng; note có thể rỗng.
- Lần đầu chuyển: tự gọi AI để sinh solution và generator bằng structured output.
- Nếu đã có code và statement không stale: giữ code.
- Nếu code stale do statement đổi: mở dialog:

```text
Statement đã thay đổi sau khi code được tạo.
[Tạo lại bằng AI] [Giữ code hiện tại]
```

Mặc định focus `Tạo lại bằng AI`, nhưng không tự ghi đè code đã chỉnh thủ công nếu người dùng chưa xác nhận.

# 13. Màn hình 4 — Code

## 13.1. Tabs

- `solution.cpp`
- `generate.cpp`

Dùng Monaco Editor với syntax C++, minimap tùy chọn, find/replace và line numbers.

## 13.2. Nút chức năng

- Tạo lại bằng AI.
- Compile.
- Auto-fix compile error.
- View compiler output.
- Compare version.
- Undo/redo editor.
- Copy code.

## 13.3. Quy chuẩn `solution.cpp`

- GNU C++17.
- Có:

```cpp
#include <bits/stdc++.h>
using namespace std;

int main() {
    ios_base::sync_with_stdio(false);
    cin.tie(NULL);
    // ...
}
```

- Thuật toán đáp ứng time/memory limit.
- Dùng `long long` khi cần chống tràn.
- Có thể dùng `#define int long long` khi AI đánh giá cần và không phá chữ ký/hàm; đây không phải yêu cầu bắt buộc cho mọi bài.
- Không dùng file I/O nếu Input file là `stdin` và Output file là `stdout`; nếu general info dùng file khác thì code phải phù hợp.
- Không chứa giải thích ngoài code fence trong dữ liệu artifact.

## 13.4. Quy chuẩn `generate.cpp`

Generator phải bám phong cách đã chốt:

- `#include "testlib.h"`.
- `#include <bits/stdc++.h>`.
- Dùng `mt19937_64` và helper random theo yêu cầu sản phẩm.
- Gọi `registerGen(argc, argv, 1)`.
- Đọc `test_id` từ `argv[1]`.
- Seed phụ thuộc `test_id`.
- Một lần chạy sinh đúng một test ra stdout.
- Mặc định 100 test:
  - 1–10: sample và corner/edge cases.
  - 11–40: medium, bao phủ nhánh.
  - 41–100: random full bounds.
- Test 1 phải chính xác là Sample 1.
- Không sinh dữ liệu ngoài constraints.
- Kết thúc input bằng newline.
- Cấu trúc dữ liệu generator phải được AI tạo theo input của bài, không sao chép cố định biến `n`, `x`, `c` từ ví dụ.

Lưu ý: yêu cầu dùng `mt19937_64` là quyết định sản phẩm. Không tự thay bằng `testlib::rnd` nếu chưa được người dùng đổi yêu cầu.

## 13.5. Structured output khi sinh code

AI trả dữ liệu:

```json
{
  "solutionCpp": "...",
  "generatorCpp": "...",
  "algorithmSummary": "...",
  "timeComplexity": "...",
  "memoryComplexity": "...",
  "recommendedChecker": "ncmp.cpp hoặc wcmp.cpp",
  "auditNotes": []
}
```

- Chỉ nhận code từ field JSON, không parse code fence bằng regex.
- Lưu version trước khi ghi đè.
- Gắn `GeneratedFromStatementVersion`.

# 14. Compile và chạy local

## 14.1. Toolchain

Bundled directory phải chứa `g++.exe`, DLL cần thiết và `testlib.h`.

Compile solution:

```text
g++.exe solution.cpp -std=gnu++17 -O2 -pipe -Wall -Wextra -o solution.exe
```

Compile generator:

```text
g++.exe generate.cpp -std=gnu++17 -O2 -pipe -Wall -Wextra -I"<testlib-dir>" -o generate.exe
```

Warnings không làm compile fail nhưng được hiển thị.

## 14.2. Process isolation

- Working directory riêng cho mỗi compile/run.
- Không dùng shell string nối trực tiếp; dùng `ProcessStartInfo.ArgumentList`.
- Redirect stdin/stdout/stderr.
- Compile timeout: 30 giây.
- Generator timeout: tối đa 5 giây.
- Solution sample timeout: `max(5 giây, 2 × timeLimit)`.
- Output limit: 10 MB mỗi process.
- Kill process tree khi timeout/cancel.
- Không chạy file upload của người dùng.

## 14.3. Auto-fix

Khi compile fail:

1. Lưu stderr và command.
2. Gửi statement, code hiện tại, compiler output và mục tiêu file cho provider/model đang chọn.
3. AI trả code thay thế có cấu trúc.
4. Lưu version.
5. Compile lại.
6. Tối đa 3 lần tự sửa cho mỗi thao tác.

Nếu vẫn lỗi:

```text
Không thể tự động sửa code sau 3 lần.
[Xem lỗi] [Chỉnh sửa thủ công] [Yêu cầu AI thử lại]
```

Không được lặp vô hạn hoặc tự đổi model.

## 14.4. Tạo sample local

Sau khi cả hai file compile:

1. Chạy `generate.exe 1`.
2. Lưu stdout thành `sample-1.in`.
3. Chạy `solution.exe` với stdin là sample input.
4. Lưu stdout thành `sample-1.out`.
5. Kiểm tra output không vượt limit và process exit code 0.
6. Cập nhật Màn hình 5.

Compile chỉ giúp phát hiện lỗi build; sample run chỉ là smoke test, không phải bằng chứng thuật toán đúng hoàn toàn.

# 15. Màn hình 5 — Tests & Sync

## 15.1. Checker

Dropdown tối thiểu:

- `ncmp.cpp`
- `wcmp.cpp`

Mặc định lấy từ recommendation của AI; người dùng được đổi.

Ứng dụng bundle source checker tương ứng để upload lên Polygon, không giả định checker đã có sẵn trong problem mới.

## 15.2. Test script

Mặc định:

```freemarker
<#list 1..100 as i>
    gen ${i} > $
</#list>
```

- Editor cho phép sửa.
- Testset name cố định mặc định `tests`.
- Khi test count thay đổi, có nút regenerate script.
- Không tự sửa script thủ công mà không cảnh báo.

## 15.3. Test settings

- Number of tests: mặc định 100.
- Score per test: mặc định 1.
- Points enabled: bật.
- Sample test index: mặc định 1.
- Use in statements: bật cho test 1.
- Test 1 input/output hiển thị từ local sample.
- Cho phép sửa sample input/output hiển thị, nhưng nếu khác output chạy thực tế phải hiện cảnh báo.

Nếu test count không phải 100, generator AI phải được tạo lại để xử lý đầy đủ range mới. Với cấu hình mặc định, nhóm vẫn là 1–10, 11–40, 41–100.

## 15.4. Self-audit

Nút `Self-Audit` chạy các kiểm tra:

- Statement fields đầy đủ.
- LaTeX validation cơ bản.
- Input/output mô tả khớp với code theo review AI.
- Solution compile pass.
- Generator compile pass.
- Test 1 chạy pass.
- Test 1 input/output tồn tại.
- Test 1 trong generator được xác nhận là sample.
- Generator có nhánh cho toàn bộ test id.
- Script gọi đúng tên generator và đúng số test.
- Checker phù hợp kiểu output.
- Time/memory complexity hợp lý.
- Overflow review.

Kết quả:

```text
Status: PASSED
```

hoặc:

```text
Status: FAILED
- <lỗi 1>
- <lỗi 2>
```

Không được luôn in PASSED.

## 15.5. Đồng bộ

Nút chính: **Đồng bộ lên Polygon**.

Trước khi chạy hiển thị review:

- Internal problem name.
- Title.
- Time/memory.
- Selected checker.
- Test count/points.
- Provider/model cuối cùng đã dùng.
- Commit message, mặc định trống.

# 16. Tích hợp AI

## 16.1. Abstraction

```csharp
public interface IAiProvider
{
    AiProviderKind Kind { get; }
    Task<IReadOnlyList<AiModelInfo>> ListModelsAsync(CancellationToken ct);
    IAsyncEnumerable<AiStreamEvent> StreamChatAsync(AiChatRequest request, CancellationToken ct);
    Task<T> GenerateStructuredAsync<T>(AiStructuredRequest request, CancellationToken ct);
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
}
```

Implementations:

- `OpenAiProvider`
- `GeminiProvider`

## 16.2. OpenAI

- Dùng API hiện hành được OpenAI khuyến nghị cho ứng dụng mới; tại thời điểm đặc tả là Responses API.
- Dùng streaming cho chat.
- Dùng function calling/structured outputs cho update statement và code generation.
- Không đưa API key vào browser.
- Lưu provider response id khi hữu ích, nhưng local history là nguồn chính.

## 16.3. Gemini

- Dùng API hiện hành được Google khuyến nghị cho ứng dụng mới; tại thời điểm đặc tả là Interactions API.
- Hỗ trợ multi-turn, image/document input, function calling và structured output.
- Không phụ thuộc server-side conversation state để có thể chuyển provider; local DB vẫn là nguồn lịch sử chính.

## 16.4. Prompt hệ thống của ứng dụng

System instruction phải yêu cầu AI:

- Đóng vai trợ lý ra đề Polygon cho học sinh.
- Bám yêu cầu người dùng, không tự tăng độ khó.
- Tạo ngữ cảnh gần gũi khi phù hợp.
- Không chuyển bước thay người dùng.
- Statement chỉ có title/legend/input/output/note.
- Không chèn sample vào statement.
- Dùng LaTeX Polygon.
- Code GNU C++17.
- Generator đúng cấu trúc test id và nhóm test.
- Không tạo validator.
- Khi sửa statement phải gọi `update_statement`.
- Khi không đủ thông tin, hỏi trong chat thay vì tự bịa constraints quan trọng.

## 16.5. Context construction

Mỗi request gồm:

1. System instruction.
2. General info.
3. Statement hiện tại.
4. Code hiện tại nếu task liên quan.
5. Test config nếu task liên quan.
6. Rolling summary.
7. Các message gần nhất.
8. Attachments của message hiện tại.

Không gửi API keys, Polygon secret, local absolute path hoặc nội dung secrets file cho AI.

# 17. Polygon API client

## 17.1. Authorization

Mỗi request đến:

```text
https://polygon.codeforces.com/api/{methodName}
```

phải có:

- `apiKey`
- `time` Unix seconds
- `apiSig`

Signature:

```text
<rand>/<methodName>?<sorted-parameters>#<secret>
```

Hash SHA-512, prefix sáu ký tự random. Parameter phải sort đúng quy tắc API. Multipart file content cũng phải được xử lý đúng theo tài liệu/API client implementation.

Tạo class độc lập có unit test bằng test vectors tự xây dựng.

## 17.2. Interface

```csharp
public interface IPolygonClient
{
    Task<IReadOnlyList<PolygonProblem>> ListProblemsAsync(string? name, CancellationToken ct);
    Task<PolygonProblem> CreateProblemAsync(string name, CancellationToken ct);
    Task UpdateInfoAsync(long problemId, GeneralInfo info, CancellationToken ct);
    Task SaveStatementAsync(long problemId, StatementDto statement, CancellationToken ct);
    Task SaveSolutionAsync(long problemId, string name, string source, string sourceType, string tag, CancellationToken ct);
    Task SaveSourceFileAsync(long problemId, string name, string source, string sourceType, CancellationToken ct);
    Task SetCheckerAsync(long problemId, string checkerName, CancellationToken ct);
    Task SaveScriptAsync(long problemId, string testset, string source, CancellationToken ct);
    Task EnablePointsAsync(long problemId, bool enabled, CancellationToken ct);
    Task SaveTestMetadataAsync(long problemId, PolygonTestMetadata metadata, CancellationToken ct);
    Task<RenderStatementsResult> RenderStatementsAsync(long problemId, bool includeContent, CancellationToken ct);
    Task<CommitResult> CommitAsync(long problemId, string? message, CancellationToken ct);
    Task BuildStandardPackageAsync(long problemId, bool verify, CancellationToken ct);
    Task<IReadOnlyList<PolygonPackage>> ListPackagesAsync(long problemId, CancellationToken ct);
    Task<PolygonCautions> GetCautionsAsync(long problemId, CancellationToken ct);
}
```

## 17.3. Source types

- Solution: `cpp.g++17`.
- Generator: `cpp.g++17`.
- Bundled checker source: `cpp.g++17`.
- Main solution tag: `MA`.
- Generator filename trên Polygon: `gen.cpp` hoặc `generate.cpp`; script và upload phải thống nhất. Chọn cố định `gen.cpp` ở remote để script ngắn gọn, trong UI vẫn có thể hiển thị `generate.cpp` local. Mapping phải rõ ràng.

## 17.4. Sync state machine

```text
NotCreated
  → NameRechecked
  → ProblemCreated
  → GeneralInfoSaved
  → StatementSaved
  → SolutionSaved
  → GeneratorSaved
  → CheckerUploaded
  → CheckerSelected
  → ScriptSaved
  → PointsEnabled
  → TestMetadataSaved
  → StatementRendered
  → Committed
  → PackageBuildStarted
  → PackageReady
```

Mỗi phase được ghi DB ngay sau khi thành công.

## 17.5. Trình tự đồng bộ chi tiết

### Preflight local

- Self-audit pass.
- API credentials valid.
- Sample có input/output.
- Code không stale hoặc người dùng xác nhận.

### Trước khi tạo

- Gọi `problems.list(name=...)` lần cuối.
- Nếu project chưa có `PolygonProblemId` và tên tồn tại: dừng.
- Nếu project đã có `PolygonProblemId` do lần sync trước tạo: cho Resume với chính id đó; không coi đây là mở problem cũ tùy ý.

### Tạo và upload

1. `problem.create`.
2. Lưu `problemId` ngay lập tức.
3. `problem.updateInfo` với input/output/time/memory.
4. `problem.saveStatement`:
   - `lang=english`
   - `name=Title`
   - `legend=Legend`
   - `input=Input`
   - `output=Output`
   - `notes=Note`
5. `problem.saveSolution`:
   - name `solution.cpp`
   - sourceType `cpp.g++17`
   - tag `MA`
6. `problem.saveFile` generator:
   - type `source`
   - name `gen.cpp`
   - sourceType `cpp.g++17`
7. Upload selected checker source qua `problem.saveFile` nếu chưa tồn tại.
8. `problem.setChecker`.
9. `problem.saveScript(testset=tests)`.
10. `problem.enablePoints(enable=true)`.
11. Gán 1 point cho mỗi test thông qua metadata/API phù hợp.
12. Với test 1:
    - `testUseInStatements=true`
    - `testInputForStatements=<sample input>`
    - `testOutputForStatements=<sample output>`
    - `verifyInputOutputForStatements=true`
13. `problem.renderStatements(includeContent=true)` để phát hiện lỗi LaTeX trước commit.
14. Nếu render lỗi: dừng, hiển thị lỗi, giữ working copy và cho sửa/resume.
15. Gọi `problem.cautions`; hiển thị blocking issues nếu có.
16. `problem.commitChanges`. Nếu commit message rỗng, bỏ parameter `message`.
17. `problem.buildPackage(full=false, verify=true)`.
18. Poll `problem.packages` với backoff đến khi package hoàn tất hoặc timeout hợp lý.
19. Hiển thị package id/revision/status, không download.

## 17.6. Partial failure và Resume Sync

Polygon API không phải transaction. Vì vậy:

- Sau `problem.create`, không được xóa `problemId` khi bước sau lỗi.
- Nút thay thành `Tiếp tục đồng bộ`.
- Resume bắt đầu từ phase chưa thành công.
- Trước mỗi write, có thể read-back khi API hỗ trợ để tránh upload trùng.
- Các bước save phải idempotent theo filename/language/test index.
- Nếu người dùng thay đổi local data sau lỗi, phase liên quan và các phase sau phải invalidated để gửi lại.
- Không tự tạo problem thứ hai với tên khác trừ khi người dùng chủ động tạo project mới.

# 18. Điểm test và sample

- Points luôn bật trong phiên bản này.
- Mỗi test mặc định 1 point.
- 100 test mặc định tương ứng tổng 100 điểm.
- Test 1 luôn dùng làm sample.
- Sample trong statement được quản lý bằng metadata test, không nằm trong five-field statement editor.
- Nếu sample input/output được sửa thủ công sau khi chạy local, self-audit phải cảnh báo khác biệt.
- Nếu generator thay đổi, sample trở thành stale và phải chạy lại test 1.
- Nếu solution thay đổi, sample output trở thành stale và phải chạy lại solution.

# 19. Bảo mật

## 19.1. Secrets

- DPAPI encryption.
- Không log secrets.
- Mask mọi key trong UI.
- Không đưa secrets vào exception detail trả browser.
- Không commit secrets.

## 19.2. Local web server

- Bind loopback only.
- Dùng random available port hoặc port cấu hình.
- Mở browser sau khi health check thành công.
- Chống CSRF cho endpoint ghi.
- Không bật CORS rộng.
- Không phục vụ trực tiếp thư mục project như static files.

## 19.3. File upload

- Sanitize filename.
- Không tin MIME do browser gửi.
- Giới hạn size/count.
- Chống ZIP bomb/path traversal.
- Không thực thi file upload.

## 19.4. Process execution

- Chỉ compile/run code từ editor project.
- Không cho người dùng nhập tùy ý executable path hoặc compiler command trong UI phiên bản này.
- Không dùng `cmd.exe /c` với string ghép.
- Timeout, output cap, process-tree kill.
- Cảnh báo rằng code C++ do AI sinh vẫn là code local; phiên bản đầu cung cấp giới hạn tiến trình nhưng không phải sandbox bảo mật tuyệt đối như VM/container.

# 20. Lưu trữ và autosave

- SQLite migration tự chạy lúc startup.
- Dùng `IDbContextFactory` trong Blazor server-side.
- Autosave debounce 500–1000 ms cho text editor.
- Code lớn được lưu cả DB metadata và file disk; nguồn chính phải được xác định nhất quán. Khuyến nghị source content ở file, version metadata/hash ở DB.
- Atomic file writes.
- Recovery khi app crash: draft cuối cùng phải có thể mở lại.
- Có màn hình danh sách project local để tạo mới/mở project, dù wizard chỉ tạo problem mới trên Polygon.

# 21. Error handling và UX

Mọi lỗi hiển thị theo cấu trúc:

```text
Tiêu đề ngắn
Mô tả dễ hiểu
Chi tiết kỹ thuật có thể mở rộng
[Thử lại] [Sao chép chi tiết]
```

Phân loại:

- Validation error.
- Provider authentication/quota/rate limit.
- Provider unsupported file/model.
- Compile error.
- Process timeout/runtime error.
- Polygon auth/signature/time drift.
- Polygon duplicate name.
- Polygon working copy/render/caution/commit/build error.
- Local filesystem/database error.

Retry phải có exponential backoff cho lỗi tạm thời và không retry tự động lỗi validation/auth.

# 22. Logging và diagnostics

- Rolling log theo ngày, giới hạn retention.
- Correlation ID cho mỗi AI request và sync attempt.
- Không log full prompt/file mặc định; có debug mode opt-in.
- Không log API keys/signatures.
- Log process command dưới dạng executable + escaped arguments, không chứa secrets.
- Settings có nút `Open logs folder`.
- Có trang Diagnostics hiển thị app version, DB path, toolchain version, provider test status và Polygon server time offset nếu biết.

# 23. Kiểm thử

## 23.1. Unit tests

- General info validation.
- Statement merge/update/undo.
- LaTeX validator cơ bản.
- Context window selection/rolling summary.
- API signature canonicalization.
- Sync state transitions.
- Stale flags.
- Safe file name và ZIP extraction.
- Process timeout/output cap.

## 23.2. Integration tests

- SQLite migrations và repositories.
- Secret encrypt/decrypt roundtrip.
- OpenAI/Gemini clients với mock HTTP handlers.
- Polygon client với recorded/mock responses.
- Compile sample code bằng bundled toolchain trong CI Windows nếu asset có sẵn.
- Resume sync sau lỗi giả lập ở từng phase.

## 23.3. E2E tests

Playwright:

1. Mở Settings, lưu fake/test configuration.
2. Tạo project.
3. Validate Màn hình 1.
4. Chat stream giả lập.
5. AI tool cập nhật statement.
6. Undo statement.
7. Edit statement.
8. Generate code giả lập.
9. Compile mock/real smoke test.
10. Xem sample.
11. Self-audit.
12. Sync bằng fake Polygon server.
13. Resume sau partial failure.

## 23.4. Manual acceptance

- Chạy bản publish trên máy Windows sạch không cài .NET/g++.
- Reload browser không mất dữ liệu.
- Đổi provider giữa chat không mất lịch sử.
- Upload ảnh/PDF và nhận response.
- Tạo một bài thực trên Polygon test account.
- Commit và standard package build thành công.

# 24. Packaging và toolchain

## 24.1. Publish

```powershell
dotnet publish src/PolygonAiBuilder.Web \
  -c Release \
  -r win-x64 \
  --self-contained true
```

Tạo script `publish-win-x64.ps1` để:

- Build/test solution.
- Publish self-contained.
- Copy toolchain/testlib/checkers.
- Copy third-party license files.
- Verify g++ version và compile một C++17 smoke program.
- Tạo thư mục distribution hoặc installer.

## 24.2. Compiler bundle

- Dùng MinGW-w64 distribution có nguồn và license rõ ràng.
- Pin version và checksum.
- Không commit binary không rõ nguồn.
- Có `acquire-toolchain.ps1` tải từ nguồn chính thức/trusted repository trong quá trình build release.
- Distribution phải kèm license/GPL notices và các nghĩa vụ phân phối tương ứng.
- `testlib.h` và checker source phải pin revision và lưu attribution.

# 25. Hiệu năng và giới hạn

- Startup đến khi browser sẵn sàng: mục tiêu dưới 5 giây trên máy phổ thông, không tính first-run migration/toolchain repair.
- Chat stream phải hiển thị chunk trong thời gian ngắn nhất provider trả.
- Editor không lag rõ rệt với code/statement dưới 1 MB.
- Database operations không block UI circuit lâu; dùng async và service scope đúng.
- Polygon package build có thể lâu; UI phải hiển thị progress/polling và cho người dùng rời màn hình mà job vẫn được theo dõi.

# 26. Accessibility và giao diện

- Keyboard navigation cho wizard, tabs, dialogs.
- Label liên kết đúng input.
- Contrast đạt mức đọc được trong light/dark mode.
- Error không chỉ biểu thị bằng màu.
- Monaco có accessible mode.
- Nút quay lại/tiếp theo cố định nhưng không che nội dung.
- Responsive tối thiểu cho desktop width 1280 trở lên; không cần tối ưu mobile.

# 27. Tiêu chí chấp nhận theo màn hình

## Settings

- Lưu/đọc key sau khi đóng mở app.
- File secrets không chứa plaintext.
- Test connection cho cả ba dịch vụ.
- Danh sách model có refresh và model selection được lưu.

## Màn hình 1

- Chỉ có năm field đã chốt.
- Validate đúng giới hạn Polygon.
- Kiểm tra duplicate trước khi đi tiếp.
- Không tạo remote problem.

## Màn hình 2

- Chat nhiều lượt, streaming.
- Upload ảnh/file.
- Đổi provider/model.
- Một conversation/project.
- Statement auto-update, diff, undo.

## Màn hình 3

- Chỉ title/legend/input/output/note.
- Editor + preview.
- LaTeX giữ nguyên khi save.
- Không có sample editor.

## Màn hình 4

- Hai file code editable.
- AI generate structured.
- GNU C++17 compile.
- Auto-fix tối đa ba lần.
- Test 1 tạo sample.

## Màn hình 5

- Checker, script, test count, point, sample.
- Self-audit trung thực.
- Sync chỉ khi nhấn nút.
- Commit message trống mặc định.
- Commit + standard package verify.
- Không download ZIP.

# 28. Definition of Done

Sản phẩm chỉ được coi là hoàn thành khi:

- `dotnet build` và toàn bộ automated tests pass.
- Bản self-contained chạy trên Windows sạch.
- Toolchain bundled compile được solution/generator C++17.
- Cả OpenAI và Gemini adapter có real implementation và mock tests.
- Polygon signature và endpoint mapping được test.
- Một end-to-end sync thật tạo được problem mới, statement, solution, generator, checker, script, points, sample, commit và standard package.
- Partial failure có thể resume bằng `problemId` đã lưu.
- Không có API key plaintext trong DB/log/distribution sample.
- README có hướng dẫn cài/chạy/cấu hình API key.
- `IMPLEMENTATION_NOTES.md` ghi các deviation nếu API hiện tại khác tài liệu.
- Không có validator trong UI, code generation hay sync.

# 29. Kế hoạch triển khai đề xuất

## Phase 1 — Foundation

- Solution structure.
- Domain/data model.
- SQLite migrations.
- Project list + wizard shell.
- Settings + encrypted secret store.

## Phase 2 — General Info và Polygon read-only

- Screen 1.
- Polygon auth/signature.
- Test connection.
- Duplicate name check.

## Phase 3 — AI Workspace

- Provider abstraction.
- OpenAI streaming.
- Gemini streaming.
- Model discovery.
- Attachments.
- Conversation persistence.
- `update_statement` + version/diff/undo.

## Phase 4 — Statement

- Five-field editor.
- Monaco/text editor integration.
- MathJax preview.
- LaTeX validation.

## Phase 5 — Code và local toolchain

- Structured code generation.
- Code versioning.
- Toolchain verification.
- Compile process runner.
- Auto-fix.
- Sample generation.

## Phase 6 — Tests & Polygon sync

- Checker/script/test config.
- Self-audit.
- Full sync state machine.
- Render/cautions/commit/build/polling.
- Resume after failure.

## Phase 7 — Hardening và release

- E2E tests.
- Security review.
- Accessibility.
- Publish/self-contained/toolchain acquisition.
- Real Polygon acceptance test.

# 30. Chỉ dẫn cho coding agent

- Đọc toàn bộ tài liệu trước khi viết code.
- Không thêm validator hoặc workflow ngoài phạm vi.
- Không thay công nghệ chính nếu chưa có lý do bắt buộc.
- Không dùng fake client trong production path; fake chỉ dành cho tests/dev mode.
- Khi thiếu credential, vẫn phải implement real integration và test bằng mock.
- Không đánh dấu hoàn thành khi chưa build/test.
- Không giấu failure bằng catch rỗng hoặc fallback giả.
- Mọi assumption mới phải ghi `IMPLEMENTATION_NOTES.md`.
- Trước khi tích hợp external API, kiểm tra tài liệu chính thức hiện hành vì endpoint/model có thể thay đổi.

# 31. Tài liệu tham khảo chính thức

1. Polygon API: https://github.com/Codeforces/polygon-misc/blob/main/API.md
2. Polygon statements TeX manual: https://polygon.codeforces.com/docs/statements-tex-manual
3. OpenAI API documentation: https://developers.openai.com/api/docs/
4. OpenAI Structured Outputs: https://developers.openai.com/api/docs/guides/structured-outputs
5. OpenAI Function Calling: https://developers.openai.com/api/docs/guides/function-calling
6. Gemini API documentation: https://ai.google.dev/gemini-api/docs
7. Gemini Interactions API: https://ai.google.dev/gemini-api/docs/interactions-overview
8. Gemini Structured Output: https://ai.google.dev/gemini-api/docs/structured-output
9. .NET support policy: https://dotnet.microsoft.com/platform/support/policy/dotnet-core
10. ASP.NET Core Blazor documentation: https://learn.microsoft.com/aspnet/core/blazor/
11. EF Core 10: https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew
12. Codex AGENTS.md guidance: https://learn.chatgpt.com/docs/agent-configuration/agents-md

