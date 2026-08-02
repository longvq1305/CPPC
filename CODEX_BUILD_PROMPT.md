# Prompt triển khai cho Codex — GPT-5.6 Extra High

Bạn đang làm việc như principal software engineer và product engineer. Hãy xây dựng hoàn chỉnh ứng dụng **Polygon AI Problem Builder** trong repository hiện tại.

## Nguồn yêu cầu

1. Đọc toàn bộ file `POLYGON_AI_BUILDER_SPEC.md` trước khi làm bất kỳ thay đổi nào.
2. Xem file đó là nguồn sự thật cao nhất về phạm vi, kiến trúc, UX, dữ liệu, tích hợp và tiêu chí hoàn thành.
3. Đọc `AGENTS.md` và tuân thủ toàn bộ working agreements.
4. Không tự thêm validator, brute-force, wrong solutions, full local stress test, chỉnh sửa Polygon problem cũ hoặc tải package ZIP.

## Cách làm việc bắt buộc

- Dùng reasoning level **Extra High** trong toàn bộ task.
- Trước khi code, kiểm tra repository và tạo `IMPLEMENTATION_PLAN.md` gồm:
  - hiện trạng repo;
  - kiến trúc sẽ tạo;
  - các phase thực hiện;
  - rủi ro external API/toolchain;
  - test plan;
  - Definition of Done bám đặc tả.
- Kiểm tra tài liệu chính thức hiện hành cho OpenAI, Gemini, Polygon và .NET trước khi viết integration. Chỉ dùng primary/official documentation. Ghi mọi khác biệt so với đặc tả vào `IMPLEMENTATION_NOTES.md`.
- Sau khi lập kế hoạch, chủ động triển khai theo phase; không dừng lại để hỏi những câu có thể giải quyết bằng tài liệu, code hoặc quyết định đã có trong spec.
- Chỉ hỏi tôi khi có blocker thực sự không thể suy ra hoặc không thể tiếp tục an toàn.

## Yêu cầu kỹ thuật không được đơn giản hóa

- .NET 10, ASP.NET Core Blazor Web App Interactive Server, EF Core 10, SQLite.
- Self-contained Windows x64.
- Monaco Editor, MathJax, chat streaming.
- OpenAI và Gemini có provider abstraction, model discovery, file/image input, structured outputs/tool calls.
- Mỗi project có một conversation; cho đổi provider/model trong cùng chat.
- API keys lưu trong `data/secrets.local.json`, mã hóa Windows DPAPI CurrentUser, không plaintext.
- Statement auto-update bằng structured tool, có version history, field diff và Undo.
- Five-field statement editor: title, legend, input, output, note; language Polygon mặc định `english`; không có sample tại màn hình statement.
- GNU C++17 toolchain bundled, có script acquisition/verification, testlib.h và standard checker sources.
- Compile `solution.cpp` và `generate.cpp`; auto-fix bằng AI tối đa 3 lần; chạy local test 1 để tạo sample.
- Polygon sync chỉ khi nhấn nút; duplicate check; create new only; partial sync phải resume bằng saved problemId.
- Sync đầy đủ: general info, statement, MA solution, generator, selected checker, script, points, sample metadata, render, cautions, commit message trống nếu người dùng không nhập, build standard package với verify.
- Không download package.

## Chất lượng triển khai

- Tách Domain/Application/Infrastructure/Integrations/UI rõ ràng.
- Không để external API details tràn vào components.
- Dùng typed HttpClient, cancellation token, retries có chọn lọc, structured error handling.
- Không catch rỗng, không hardcode secret, không fake success.
- Process execution không dùng shell-concatenated command; có timeout, output limit, process-tree kill.
- Bảo vệ ZIP extraction, filename, localhost binding và secrets.
- Có migrations, autosave, crash recovery và sync operation log.
- UI phải usable, keyboard accessible, light/dark friendly và desktop-first.

## Test bắt buộc

Tạo và chạy:

- Unit tests cho validation, statement versioning/undo, LaTeX checks, Polygon signature, sync state machine, stale state, safe files và process limits.
- Integration tests cho SQLite, DPAPI, provider clients với mock HTTP, Polygon client và resume sync.
- Playwright E2E cho wizard, AI update statement, undo, code generation, sample và fake Polygon sync.
- Windows toolchain smoke test khi compiler asset có sẵn.

Mỗi phase phải kết thúc bằng build/test liên quan. Cuối task phải chạy ít nhất:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

và chạy E2E phù hợp nếu môi trường hỗ trợ.

## Toolchain và external credentials

- Không được tạo hoặc giả mạo compiler binary.
- Tạo `scripts/acquire-toolchain.ps1`, pin nguồn/version/checksum từ nguồn đáng tin cậy, copy license notices và tạo `scripts/verify-toolchain.ps1`.
- Nếu môi trường hiện tại không cho tải binary lớn hoặc không có API credentials, vẫn phải hoàn thành production code, mock/fake test infrastructure và hướng dẫn rõ bước acceptance thực tế còn lại.
- Không được tuyên bố real Polygon/OpenAI/Gemini test đã pass nếu không có credentials và log thực tế.

## Deliverables cuối cùng

Repository phải có tối thiểu:

- Source code hoàn chỉnh.
- `README.md` hướng dẫn chạy dev, publish và cấu hình.
- `AGENTS.md`.
- `IMPLEMENTATION_PLAN.md` cập nhật trạng thái.
- `IMPLEMENTATION_NOTES.md`.
- Database migrations.
- Unit/integration/E2E tests.
- Toolchain acquisition/verification scripts.
- Publish script cho win-x64.
- Third-party notices/licenses.

Khi hoàn tất, trả lời bằng báo cáo ngắn gồm:

1. Những gì đã triển khai.
2. Các lệnh build/test đã chạy và kết quả thật.
3. Những phần chưa thể kiểm tra thực tế vì thiếu credentials hoặc môi trường.
4. Đường dẫn file/thư mục quan trọng.
5. Các bước để tôi chạy ứng dụng và thực hiện acceptance test đầu tiên.

Bắt đầu bằng việc đọc spec, kiểm tra repo và tạo `IMPLEMENTATION_PLAN.md`, sau đó triển khai liên tục theo kế hoạch.
