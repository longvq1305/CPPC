# Polygon AI Builder

Polygon AI Builder is a local-first Windows application for creating a new
competitive-programming problem with OpenAI or Gemini, validating the local
workflow with a pinned GNU C++17 toolchain, and explicitly synchronizing the
finished problem to Codeforces Polygon.

Version 1.0 implements the full five-step workflow:

1. General Info and a read-only Polygon name check.
2. One provider-neutral AI conversation per local problem.
3. A versioned five-field English statement with local LaTeX preview.
4. Editable/versioned `solution.cpp` and `generate.cpp`, compile diagnostics, and
   a real `test_id=1` local sample smoke test.
5. Checker/test configuration, deterministic plus AI Self-Audit, resumable explicit
   Polygon sync, statement render/caution checks, commit, and verified standard
   package polling.

The app never auto-syncs. It does not add validators, brute-force/wrong solutions,
run all 100 tests locally, or download Polygon packages.

## Prerequisites

- Windows 11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for source builds
- OpenAI, Gemini, and Polygon credentials entered only through Settings

Acquire the pinned compiler and testlib/checker sources once:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/acquire-toolchain.ps1
powershell -ExecutionPolicy Bypass -File scripts/verify-toolchain.ps1
```

The compiler archive version and SHA-256, testlib revision, and source checksums are
recorded in `toolchain/manifest.json`. Downloaded compiler binaries are intentionally
excluded from Git.

## Run locally

```powershell
dotnet run --project src/PolygonAiBuilder.Web
```

The server listens only on `http://127.0.0.1:5187`. After `/health` succeeds, the
application asks Windows to open that URL in the default browser. If browser launch
is blocked by Windows policy, open the URL manually while the command remains
running.

Runtime files are stored under `data/`, `projects/`, and `logs/`. Credentials are
outside SQLite in `data/secrets.local.json`, encrypted with Windows DPAPI
`CurrentUser`; plaintext keys are not written to logs or application data.

## Hướng dẫn sử dụng

### 1. Khởi động ứng dụng

Chạy ứng dụng từ mã nguồn bằng lệnh ở phần **Run locally** và giữ cửa sổ
PowerShell đang chạy. Ứng dụng sẽ tự mở trình duyệt sau khi hoàn tất health check.
Nếu không có tab mới xuất hiện, hãy tự mở
<http://127.0.0.1:5187> trong trình duyệt. Dừng ứng dụng bằng `Ctrl+C` tại cửa sổ
PowerShell.

Nếu dùng bản đã publish, chạy
`artifacts/publish/win-x64/PolygonAiBuilder.Web.exe`; bản self-contained không yêu
cầu cài .NET riêng.

### 2. Cấu hình API và compiler

Mở **Cài đặt** ở thanh điều hướng và thực hiện lần lượt:

1. Nhập OpenAI API key, nhấn **Lưu thay đổi**, sau đó **Refresh models**, chọn
   model mặc định, lưu lại lần nữa và nhấn **Test connection**.
2. Nhập Gemini API key và làm tương tự. Có thể chỉ dùng một trong hai AI provider,
   nhưng provider được chọn trong workspace phải có key và model hợp lệ.
3. Nhập Polygon API key cùng API secret, nhấn **Lưu thay đổi**, rồi
   **Test connection**. Polygon phải được cấu hình trước khi qua Bước 1 của một dự
   án.
4. Trong thẻ **Local toolchain**, nhấn **Verify toolchain**. Nếu trạng thái chưa
   sẵn sàng, nhấn **Repair toolchain** và xác minh lại.

Các ô key sẽ trống sau khi lưu; nhãn `Đã lưu key` hoặc `Đã lưu credential` cho
biết dữ liệu đã được lưu. Key được mã hóa bằng Windows DPAPI cho đúng tài khoản
Windows hiện tại. Không sao chép `data/secrets.local.json` sang máy hoặc tài khoản
Windows khác để dùng lại.

### 3. Tạo problem theo quy trình 5 bước

Tại trang **Dự án lập trình**, chọn **Dự án mới**, nhập tên nội bộ Polygon và
mở dự án vừa tạo.

#### Bước 1 — General Info

- Nhập tên nội bộ, input file, output file, time limit và memory limit. Giá trị mặc
  định là `stdin`, `stdout`, `1000 ms` và `256 MB`.
- Nhấn **Tiếp theo**. Ứng dụng chỉ gọi Polygon để kiểm tra trùng tên; chưa tạo
  problem từ xa ở bước này.
- Nếu Polygon không kết nối được hoặc tên đã tồn tại, sửa lỗi rồi thử lại. Tool chỉ
  tạo problem mới, không chỉnh sửa tùy ý problem Polygon có sẵn.

#### Bước 2 — AI Workspace

- Chọn OpenAI hoặc Gemini và model muốn dùng. Việc đổi provider/model không làm
  mất hội thoại của dự án.
- Mô tả ý tưởng, độ khó, constraints và các yêu cầu của bài; có thể đính kèm ảnh
  hoặc file rồi nhấn **Gửi**.
- Trao đổi nhiều lượt đến khi nội dung ổn định, sau đó nhấn
  **Cập nhật statement**. Kiểm tra phần thay đổi và dùng **Undo** khi cần.
- Mỗi dự án có một conversation riêng; nội dung chat và statement được lưu local.

#### Bước 3 — Statement

- Chỉnh đúng năm trường `Title`, `Legend`, `Input`, `Output`, `Note` và xem preview
  LaTeX bên phải.
- Dùng **Lưu ngay**, **Lịch sử**, **Restore AI** hoặc khôi phục một phiên bản cũ
  khi cần. `Title`, `Legend`, `Input` và `Output` phải có nội dung, đồng thời không
  được còn lỗi LaTeX màu đỏ trước khi tiếp tục.
- Sample không nằm trong editor này; sample được tạo và quản lý ở Bước 4–5.

#### Bước 4 — Code

- Nhấn **Tạo code bằng AI** để sinh `solution.cpp` và `generate.cpp`, hoặc chỉnh
  trực tiếp từng file trong editor.
- Nhấn **Compile cả hai**. Khi có lỗi, xem **Compiler output** và dùng
  **Auto-fix**; ứng dụng giới hạn tối đa ba lần sửa tự động cho mỗi lượt.
- Sau khi cả hai file compile thành công, nhấn **Chạy lại Sample 1**. Ứng dụng chỉ
  chạy generator với `test_id=1`, rồi chạy solution để tạo sample input/output.
- Sample 1 chỉ là smoke test local, không chứng minh solution đúng cho toàn bộ test.
  Ứng dụng không chạy cả 100 test trên máy.

#### Bước 5 — Tests & Sync

1. Kiểm tra checker (`ncmp.cpp` hoặc `wcmp.cpp`), số test, điểm mỗi test và
   Freemarker test script; nhấn **Lưu cấu hình** sau khi chỉnh.
2. Kiểm tra Sample 1. Nếu sửa thủ công, nhấn **Lưu sample đã chỉnh** và lưu ý
   Self-Audit sẽ cảnh báo khác biệt với kết quả chạy local.
3. Nhấn **Chạy Self-Audit** và xử lý toàn bộ lỗi chặn cho đến khi trạng thái là
   `PASSED`.
4. Nhấn **Đồng bộ lên Polygon**, đọc lại bản tóm tắt rồi nhấn
   **Xác nhận đồng bộ**. Đây là thao tác duy nhất tạo/ghi problem trên Polygon;
   ứng dụng không tự sync.
5. Giữ ứng dụng chạy trong lúc upload, render statement, kiểm tra cautions, commit
   và build standard package. Ứng dụng chỉ hiển thị trạng thái package và không tải
   package ZIP về máy.

Nếu sync dừng giữa chừng sau khi Polygon đã tạo problem, ID từ xa được giữ lại.
Sau khi sửa nguyên nhân, dùng **Tiếp tục đồng bộ** để resume thay vì tạo một
problem thứ hai.

### 4. Mở lại dự án và xử lý lỗi thường gặp

- Dự án được autosave. Lần chạy sau, mở **Dự án lập trình** và chọn project để tiếp
  tục đúng bước gần nhất.
- Nếu trình duyệt báo không kết nối được, kiểm tra cửa sổ PowerShell còn chạy và
  mở lại <http://127.0.0.1:5187>.
- Nếu không tải được model hoặc AI trả lỗi `401`/`403`, lưu lại key rồi chạy
  **Test connection**. Lỗi `429` thường là hết quota hoặc bị giới hạn tốc độ; chờ
  hoặc bổ sung quota trước khi thử lại.
- Nếu compile không chạy, mở **Cài đặt** và dùng **Repair toolchain**, sau đó
  **Verify toolchain**.
- Nếu nút sync bị khóa, phải chạy lại Self-Audit sau mọi thay đổi liên quan và xử
  lý các mục chưa đạt.
- Dùng **Diagnostics** để xem phiên bản, đường dẫn dữ liệu và trạng thái kết nối;
  dùng **Open logs folder** trong Cài đặt để lấy log khi cần chẩn đoán.

## Build and test

```powershell
dotnet format PolygonAiBuilder.slnx --no-restore
dotnet build PolygonAiBuilder.slnx -c Release
dotnet test PolygonAiBuilder.slnx -c Release --no-build
```

Automated external-integration tests use mock HTTP handlers. A real Polygon write is
not part of the automated suite because sync requires a deliberate user action.

## Publish Windows x64

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-win-x64.ps1
```

The script verifies the pinned toolchain, runs the Release build and full suite,
publishes a self-contained `win-x64` distribution, copies compiler/testlib/checker
licenses and assets, and verifies the compiler again inside
`artifacts/publish/win-x64`.

See [architecture notes](docs/ARCHITECTURE.md),
[security notes](docs/SECURITY.md), [API research](docs/API_INTEGRATION_NOTES.md),
and [implementation notes](IMPLEMENTATION_NOTES.md).
