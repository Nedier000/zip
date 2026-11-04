# AoV / HOK Asset Editor Android — Alpha 0.2

**Đây là project Android Studio dạng mã nguồn, chưa phải APK cài đặt.** Môi trường làm việc dùng để tạo gói này không có Android SDK/`android.jar`, `sdkmanager` hoặc Gradle, nên không thể biên dịch và chạy ứng dụng Android tại đây.

## Chức năng có trong mã nguồn

- Mở AssetBundle qua Android Storage Access Framework; không cần cấp quyền đọc toàn bộ bộ nhớ.
- Nạp/kiểm kê object bằng bản parser `UnityPy_AOV` từ tool người dùng cung cấp.
- Danh sách Material / Texture2D / Mesh / các object phổ biến, tìm theo tên hoặc PathID.
- Đọc `m_SavedProperties.m_Colors`, `m_Floats`, `m_TexEnvs` từ typetree của Material.
- Sửa RGBA hoặc float trên typetree thật, ghi lại SerializedFile/AssetBundle, mở lại output và so sánh giá trị vừa ghi.
- Preview texture từ dữ liệu `Texture2D`: giải mã PNG cho các định dạng pixel thô được hỗ trợ; hoặc upload các block texture ASTC/ETC/DXT/PVRTC/ATC/BC lên GPU khi thiết bị công bố extension tương ứng.
- Preview geometry tĩnh của Mesh thật bằng OpenGL ES 3.0; hỗ trợ xoay, zoom, UV texture và tint Material khi có thể tìm được liên kết MeshRenderer/Material/Texture.
- Xuất bản sửa qua Android `ACTION_CREATE_DOCUMENT`, không ghi đè file gốc.
- Có fallback AES-CBC và SM4-CBC thuần Python; các vector chuẩn được kiểm tra trong môi trường Python.

## Giới hạn quan trọng

- **Chưa có APK đã biên dịch/ký và chưa chạy trên điện thoại.** Mã Kotlin chưa được biên dịch với Android SDK.
- Preview 3D là preview geometry tĩnh, không phải Unity runtime. Shader độc quyền, skeleton/skin deformation, rig, animation, particle system và hiệu ứng game không được tái tạo đầy đủ.
- Preview texture nén phụ thuộc GPU/driver Android. Nếu GPU không hỗ trợ định dạng gốc và định dạng đó không có decoder CPU được đóng gói, app sẽ báo không hỗ trợ thay vì hiển thị texture giả.
- Mesh preview hiện chọn Material/texture liên kết đầu tiên khi resolver tìm thấy; Mesh nhiều submesh/material có thể không thể hiện chính xác toàn bộ vật liệu.
- Parser vẫn phát hiện một số reference bị thiếu và một số typetree game-specific không đọc được. Việc đọc lại đúng một thuộc tính Material không chứng minh mọi object của bundle đã được hiểu đúng hoặc file chắc chắn chạy được trong game.
- Ghi hiện hỗ trợ Material color và float đã parse được. Chưa có bộ biên tập pixel texture nén/encoder ASTC hoặc editor vertex/skin/animation.

## Phụ thuộc

- Android Gradle Plugin `8.7.2`, Kotlin Gradle Plugin `2.1.21`
- Gradle `8.9` (launcher `gradlew`/`gradlew.bat` tải bản phân phối chính thức lần đầu)
- Chaquopy `16.1.0`, Python `3.11`
- `lz4==4.3.3`, `brotli==1.1.0`, `Pillow==11.0.0`
- Android SDK Platform 35; JDK tương thích với Android Gradle Plugin đã chọn.

`sm4` không còn là phụ thuộc pip bắt buộc: app dùng fallback trong `UnityPy_AOV/helpers/sm4_impl.py`. `Crypto.Cipher.AES` cũng có fallback thuần Python. Các decoder native tùy chọn như `texture2ddecoder`/`etcpak` không bắt buộc cho preview GPU các block texture được hỗ trợ.

## Build trong Android Studio

1. Giải nén ZIP trên máy có JDK 17+ và Android SDK; cài Android SDK Platform 35.
2. Kết nối mạng để tải Gradle 8.9, plugin Android/Kotlin/Chaquopy và các wheel Python. Script `gradlew` (Linux/macOS) hoặc `gradlew.bat` (Windows) gọi Gradle 8.9 từ phân phối chính thức; bản ZIP không kèm wrapper JAR chuẩn.
3. Trên terminal, chạy `./gradlew assembleDebug` (Linux/macOS) hoặc `gradlew.bat assembleDebug` (Windows), hoặc mở project trong Android Studio và chạy **Build > Build APK(s)**.
4. APK debug dự kiến nằm ở `app/build/outputs/apk/debug/app-debug.apk` sau khi build thành công.
5. Đã thêm `.github/workflows/build-apk.yml`: nếu đưa nội dung project vào repository GitHub và bật Actions, workflow sẽ build APK khi push lên `main`/`master` hoặc khi chạy thủ công; APK được đính kèm dưới dạng workflow artifact. Đây là cơ chế build từ xa, không có APK nào được tạo trong ZIP hiện tại.
6. Nếu build lỗi, lưu toàn bộ `Build Output` (nhất là lỗi pip/wheel Chaquopy) để chẩn đoán; chưa nên cài APK nếu build chưa thành công.

## Test đã chạy trong môi trường tạo source

- `python -m compileall` cho parser và `asset_engine.py`: PASS.
- SM4 known-answer test: PASS; AES-CBC known-answer/round-trip test: PASS.
- Đọc bundle `593_machao_battle_base.assetbundle`: đọc lại 3.752 objects; kiểm kê 343 Material, 399 Texture2D và 159 Mesh.
- Xuất OBJ từ một Mesh thật; resolver tìm được Material `Sinon2077XD`, texture `ASTC_RGB_6x6` 128×128 và tint từ typetree.
- Ánh xạ ASTC 6×6 đã sửa sang enum OpenGL ES `0x93B4`.
- Sửa một RGBA Material color và một Material float trên bản sao: output được nạp lại và thuộc tính đọc lại khớp (`readback-ok`). Các file test tạm được xóa sau khi kiểm thử.
- Không thể chạy Kotlin/Gradle build hoặc xác minh ứng dụng/preview GPU trên Android trong môi trường hiện tại.

## File chính

- `app/src/main/java/com/aov/asseteditor/MainActivity.kt`: UI native, file picker, danh sách object, chỉnh Material, lưu file.
- `app/src/main/java/com/aov/asseteditor/AssetPreview.kt`: viewport OpenGL ES 3.0 và import OBJ preview.
- `app/src/main/python/asset_engine.py`: inspect, export preview data và ghi Material typetree.
- `app/src/main/python/UnityPy_AOV/`: parser cùng fallback mã hóa.
