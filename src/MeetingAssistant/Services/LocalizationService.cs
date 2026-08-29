using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using WpfBinding = System.Windows.Data.Binding;

namespace MeetingAssistant.Services;

public static class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> Vietnamese =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PRIVATE BY DEFAULT"] = "MẶC ĐỊNH RIÊNG TƯ",
            ["  ·  PRIVATE BY DEFAULT"] = "  ·  RIÊNG TƯ MẶC ĐỊNH",
            ["A quieter memory for busy teams"] = "Ghi nhớ nhẹ nhàng cho đội ngũ bận rộn",
            ["Make every meeting\ncome back to you."] = "Để mọi cuộc họp\nluôn quay về với bạn.",
            ["Record locally. Understand speakers. Leave with the signal — not another page of notes to reconstruct."] = "Ghi âm cục bộ. Nhận diện người nói. Giữ lại điều quan trọng — không phải một trang ghi chú cần dựng lại.",
            ["Local capture"] = "Ghi âm cục bộ",
            ["No bot joins your call"] = "Không có bot tham gia cuộc gọi",
            ["Your name"] = "Tên của bạn",
            ["Email address"] = "Địa chỉ email",
            ["Password"] = "Mật khẩu",
            ["At least 6 characters"] = "Ít nhất 6 ký tự",
            ["Forgot password?"] = "Quên mật khẩu?",
            ["Continue with local workspace"] = "Tiếp tục với workspace cục bộ",
            ["Your data is encrypted in transit when cloud sync is connected. Offline mode keeps meetings on this device."] = "Dữ liệu được mã hóa khi đồng bộ đám mây. Chế độ offline giữ cuộc họp trên thiết bị này.",
            ["Meeting"] = "Cuộc họp",
            ["Meetings"] = "Cuộc họp",
            ["Speakers"] = "Người nói",
            ["Settings"] = "Cài đặt",
            ["Speaker profiles"] = "Hồ sơ người nói",
            ["WORKSPACE"] = "WORKSPACE",
            ["QUICK START"] = "BẮT ĐẦU NHANH",
            ["Start recording"] = "Bắt đầu ghi âm",
            ["New recording"] = "Ghi âm mới",
            ["Record"] = "Ghi âm",
            ["Record locally"] = "Ghi âm cục bộ",
            ["Good morning, "] = "Chào buổi sáng, ",
            ["Here is the thread of work your conversations are creating."] = "Đây là mạch công việc đang được tạo ra từ các cuộc trò chuyện của bạn.",
            ["Recent meetings"] = "Cuộc họp gần đây",
            ["Search meetings..."] = "Tìm cuộc họp...",
            ["Loading meetings..."] = "Đang tải cuộc họp...",
            ["conversations"] = "cuộc trò chuyện",
            ["MEETINGS"] = "CUỘC HỌP",
            ["in your workspace"] = "trong workspace",
            ["THIS WEEK"] = "TUẦN NÀY",
            ["recorded today"] = "đã ghi hôm nay",
            ["ACTION ITEMS"] = "VIỆC CẦN LÀM",
            ["ready to move forward"] = "sẵn sàng triển khai",
            ["No meetings match that search"] = "Không có cuộc họp phù hợp",
            ["Try a title, speaker name, date, or phrase from the transcript."] = "Hãy thử tên cuộc họp, người nói, ngày hoặc một cụm từ trong transcript.",
            ["Capture a meeting with mic + system audio, then let Meeting Assistant shape the signal into a transcript and clear next steps."] = "Ghi âm bằng mic và âm thanh hệ thống, rồi để Meeting Assistant chuyển nội dung thành transcript và các bước tiếp theo rõ ràng.",
            ["READY WHEN YOU ARE"] = "SẴN SÀNG KHI BẠN SẴN SÀNG",
            ["Keep your attention in the room."] = "Giữ sự tập trung trong cuộc họp.",
            ["Start a recording"] = "Bắt đầu ghi âm",
            ["MIC  +  SYSTEM"] = "MIC  +  HỆ THỐNG",
            ["NO BOT"] = "KHÔNG BOT",
            ["No meeting bot. No calendar access. No recording leaves this device until you choose to sync it."] = "Không có bot dự họp. Không truy cập lịch. Bản ghi không rời thiết bị cho đến khi bạn chọn đồng bộ.",
            ["Keep a local copy for recovery"] = "Giữ bản cục bộ để khôi phục",
            ["Set up your capture"] = "Thiết lập ghi âm",
            ["Select the sources you want to remember. This recorder stays on your device and never joins the call."] = "Chọn nguồn bạn muốn lưu lại. Trình ghi âm chạy trên thiết bị và không bao giờ tham gia cuộc gọi.",
            ["Back to meetings"] = "Quay lại danh sách cuộc họp",
            ["←  Back to meetings"] = "←  Quay lại danh sách cuộc họp",
            ["All meetings"] = "Tất cả cuộc họp",
            ["←  All meetings"] = "←  Tất cả cuộc họp",
            ["Leave recording"] = "Rời phiên ghi âm",
            ["←  Leave recording"] = "←  Rời phiên ghi âm",
            ["RECORDING DETAILS"] = "THÔNG TIN BẢN GHI",
            ["Meeting title"] = "Tên cuộc họp",
            ["AUDIO SOURCES"] = "NGUỒN ÂM THANH",
            ["Microphone input"] = "Đầu vào micro",
            ["System audio"] = "Âm thanh hệ thống",
            ["Capture quality"] = "Chất lượng ghi",
            ["DEVICE CHECK"] = "KIỂM TRA THIẾT BỊ",
            ["A quick check gives you confidence before the call begins."] = "Kiểm tra nhanh giúp bạn yên tâm trước khi cuộc gọi bắt đầu.",
            ["Microphone"] = "Micro",
            ["Privacy promise"] = "Cam kết riêng tư",
            ["PRIVACY PROMISE"] = "CAM KẾT RIÊNG TƯ",
            ["Your voice is being captured"] = "Giọng nói của bạn đang được ghi",
            ["You can keep this window minimized. Recording continues in the background."] = "Bạn có thể thu nhỏ cửa sổ này. Việc ghi âm vẫn tiếp tục ở chế độ nền.",
            ["Stop & process"] = "Dừng và xử lý",
            ["Pause"] = "Tạm dừng",
            ["Resume"] = "Tiếp tục",
            ["Meeting audio is being captured"] = "Âm thanh cuộc họp đang được ghi",
            ["ACTIVE SPEAKER"] = "NGƯỜI ĐANG NÓI",
            ["Making sense of your meeting"] = "Đang tổng hợp cuộc họp",
            ["Audio tracks prepared"] = "Đã chuẩn bị các track âm thanh",
            ["Transcribing conversation"] = "Đang chuyển lời nói thành văn bản",
            ["Speaker turns identified"] = "Đã nhận diện lượt nói",
            ["Summary and action items"] = "Tóm tắt và việc cần làm",
            ["Retry processing"] = "Xử lý lại",
            ["Saved to your local workspace"] = "Đã lưu vào workspace cục bộ",
            ["MEETING REVIEW"] = "XEM LẠI CUỘC HỌP",
            ["Transcript"] = "Transcript",
            ["speaker turns"] = "lượt nói",
            ["Summary"] = "Tóm tắt",
            ["OVERVIEW"] = "TỔNG QUAN",
            ["KEY POINTS"] = "ĐIỂM CHÍNH",
            ["DECISIONS"] = "QUYẾT ĐỊNH",
            ["DEADLINES"] = "HẠN CHÓT",
            ["QUESTIONS TO CARRY"] = "CÂU HỎI CẦN THEO DÕI",
            ["IMPORTANT MOMENTS"] = "KHOẢNH KHẮC QUAN TRỌNG",
            ["SPEAKER NAMES"] = "TÊN NGƯỜI NÓI",
            ["Rename a speaker once and keep the transcript readable."] = "Đổi tên người nói một lần để transcript luôn dễ đọc.",
            ["Save speaker names"] = "Lưu tên người nói",
            ["Save all names"] = "Lưu tất cả tên",
            ["Speaker recognition is on"] = "Đã bật nhận diện người nói",
            ["Meeting Assistant groups voice turns locally before processing them."] = "Meeting Assistant nhóm các lượt nói cục bộ trước khi xử lý.",
            ["Speaker detection is grouping voices as you talk"] = "Nhận diện người nói đang nhóm giọng nói khi bạn trò chuyện",
            ["YOUR SPEAKER PROFILES"] = "HỒ SƠ NGƯỜI NÓI",
            ["Voice profile"] = "Hồ sơ giọng nói",
            ["meetings together"] = "cuộc họp cùng nhau",
            ["Profiles grow with every review"] = "Hồ sơ hoàn thiện sau mỗi lần xem lại",
            ["As you rename speakers, their names are carried into future local transcripts."] = "Khi bạn đổi tên người nói, tên đó sẽ được dùng trong các transcript cục bộ sau này.",
            ["Names are applied across your saved transcripts, so the story gets easier to follow over time."] = "Tên được áp dụng cho các transcript đã lưu để nội dung ngày càng dễ theo dõi.",
            ["ACCOUNT"] = "TÀI KHOẢN",
            ["CAPTURE"] = "GHI ÂM",
            ["Input used for new recordings"] = "Nguồn dùng cho bản ghi mới",
            ["Loopback source for other participants"] = "Nguồn loopback cho những người tham gia khác",
            ["PRIVACY & SYNC"] = "RIÊNG TƯ VÀ ĐỒNG BỘ",
            ["Recording retention"] = "Thời gian lưu bản ghi",
            ["Local copies are the recovery path when a provider is unavailable."] = "Bản cục bộ giúp khôi phục khi dịch vụ không khả dụng.",
            ["Pause cloud sync"] = "Tạm dừng đồng bộ đám mây",
            ["Keep a local copy of every recording"] = "Giữ bản cục bộ cho mọi bản ghi",
            ["SHORTCUTS"] = "PHÍM TẮT",
            ["Start / stop recording"] = "Bắt đầu / dừng ghi âm",
            ["Hotkey  Ctrl + Shift + R"] = "Phím tắt  Ctrl + Shift + R",
            ["Works even when Meeting Assistant is minimized."] = "Hoạt động cả khi Meeting Assistant đang thu nhỏ.",
            ["Open Meeting Assistant when Windows starts"] = "Mở Meeting Assistant khi Windows khởi động",
            ["Switch account"] = "Chuyển tài khoản",
            ["The people in your conversations"] = "Những người trong các cuộc trò chuyện của bạn",
            ["Transcript with timestamps"] = "Transcript kèm mốc thời gian",
            ["You can change retention and sync behavior in Settings."] = "Bạn có thể đổi thời gian lưu và cách đồng bộ trong Cài đặt.",
            ["Save settings"] = "Lưu cài đặt",
            ["Save changes"] = "Lưu thay đổi",
            ["APPEARANCE"] = "GIAO DIỆN",
            ["Theme"] = "Chủ đề",
            ["Choose a comfortable light or dark workspace."] = "Chọn workspace sáng hoặc tối phù hợp với bạn.",
            ["Language"] = "Ngôn ngữ",
            ["Use Vietnamese or English throughout the app."] = "Dùng tiếng Việt hoặc tiếng Anh trong toàn bộ ứng dụng.",
            ["AI PROVIDERS"] = "NHÀ CUNG CẤP AI",
            ["OpenAI API key"] = "API key OpenAI",
            ["Transcription model"] = "Model chuyển lời nói",
            ["Summary model"] = "Model tóm tắt",
            ["Test connection"] = "Kiểm tra kết nối",
            ["The key is stored in this Windows user profile, never in the installer."] = "Key được lưu trong hồ sơ Windows này, không bao giờ nằm trong installer.",
            ["Testing OpenAI connection…"] = "Đang kiểm tra kết nối OpenAI…",
            ["Enter an OpenAI API key first."] = "Hãy nhập API key OpenAI trước.",
            ["OpenAI connection is ready"] = "Kết nối OpenAI sẵn sàng",
            ["OpenAI connection is ready."] = "Kết nối OpenAI sẵn sàng.",
            ["OpenAI rejected the connection."] = "OpenAI từ chối kết nối.",
            ["OpenAI rejected the API key. Check the key in Settings and retry."] = "OpenAI từ chối API key. Hãy kiểm tra key trong Cài đặt rồi xử lý lại.",
            ["OpenAI is rate limiting requests. Wait a moment and retry."] = "OpenAI đang giới hạn số lần gọi. Hãy đợi một lát rồi xử lý lại.",
            ["OpenAI quota has been exceeded. Check your OpenAI account and retry."] = "Đã hết hạn mức OpenAI. Hãy kiểm tra tài khoản OpenAI rồi xử lý lại.",
            ["The recording is too large for OpenAI. Record a shorter meeting and retry."] = "Bản ghi quá lớn để OpenAI chuyển lời. Hãy ghi cuộc họp ngắn hơn rồi xử lý lại.",
            ["OpenAI could not read the uploaded audio. Your recording is still available; retry processing or record again."] = "OpenAI không đọc được file âm thanh đã tải lên. Bản ghi vẫn còn; hãy xử lý lại hoặc ghi cuộc họp mới.",
            ["OpenAI is not reachable. Check the network and try again."] = "Không thể kết nối OpenAI. Hãy kiểm tra mạng và thử lại.",
            ["Converting captured tracks to provider-compatible PCM WAV"] = "Đang chuyển bản ghi sang PCM WAV tương thích với nhà cung cấp",
            ["OpenAI took too long to respond."] = "OpenAI phản hồi quá lâu.",
            ["Configured"] = "Đã cấu hình",
            ["Not configured · local demo will be used"] = "Chưa cấu hình · sẽ dùng bản demo cục bộ",
            ["Local demo · OpenAI key not configured"] = "Demo cục bộ · chưa cấu hình key OpenAI",
            ["Dark"] = "Tối",
            ["Light"] = "Sáng",
            ["Default microphone"] = "Micro mặc định",
            ["Headset microphone"] = "Micro tai nghe",
            ["USB studio microphone"] = "Micro phòng thu USB",
            ["Default system audio"] = "Âm thanh hệ thống mặc định",
            ["All system audio"] = "Toàn bộ âm thanh hệ thống",
            ["Meeting app audio only"] = "Chỉ âm thanh ứng dụng họp",
            ["Balanced · 48 kHz"] = "Cân bằng · 48 kHz",
            ["High quality · 48 kHz"] = "Chất lượng cao · 48 kHz",
            ["Compact · 16 kHz"] = "Tiết kiệm · 16 kHz",
            ["Keep recordings for 7 days"] = "Giữ bản ghi trong 7 ngày",
            ["Keep recordings for 30 days"] = "Giữ bản ghi trong 30 ngày",
            ["Keep recordings until deleted"] = "Giữ bản ghi đến khi xóa",
            ["Today"] = "Hôm nay",
            ["Yesterday"] = "Hôm qua",
            ["there"] = "bạn",
            ["Connecting…"] = "Đang kết nối…",
            ["Create account"] = "Tạo tài khoản",
            ["Sign in"] = "Đăng nhập",
            ["Already have an account? Sign in"] = "Đã có tài khoản? Đăng nhập",
            ["New here? Create a workspace"] = "Bạn mới dùng? Tạo workspace",
            ["Offline workspace"] = "Workspace offline",
            ["Firebase-ready workspace"] = "Workspace đã sẵn sàng với Firebase",
            ["Sync paused"] = "Đã tạm dừng đồng bộ",
            ["Just now · local cache"] = "Vừa xong · bộ nhớ cục bộ",
            ["Registered · Ctrl + Shift + R"] = "Đã đăng ký · Ctrl + Shift + R",
            ["Unavailable · another app may own this shortcut"] = "Không khả dụng · ứng dụng khác có thể đang dùng phím tắt",
            ["Not tested yet"] = "Chưa kiểm tra",
            ["Listening for both audio sources…"] = "Đang nghe cả hai nguồn âm thanh…",
            ["Both sources look good · ready to record"] = "Cả hai nguồn đều ổn · sẵn sàng ghi âm",
            ["Listening for a speaker"] = "Đang chờ người nói",
            ["Recording live"] = "Đang ghi âm",
            ["Recording paused"] = "Đã tạm dừng ghi âm",
            ["Preparing audio"] = "Đang chuẩn bị âm thanh",
            ["Checking microphone and system audio tracks"] = "Đang kiểm tra track micro và âm thanh hệ thống",
            ["We could not sign you in. Try again."] = "Không thể đăng nhập. Hãy thử lại.",
            ["Offline workspace could not be opened."] = "Không thể mở workspace offline.",
            ["We could not start a password reset."] = "Không thể bắt đầu đặt lại mật khẩu.",
            ["If an account exists for this email, reset instructions are on their way."] = "Nếu email này đã có tài khoản, hướng dẫn đặt lại mật khẩu sẽ được gửi đến bạn.",
            ["Recording setup opened · press Ctrl + Shift + R again to start"] = "Đã mở thiết lập ghi âm · nhấn Ctrl + Shift + R lần nữa để bắt đầu",
            ["Offline workspace ready · your meetings are stored locally"] = "Workspace offline đã sẵn sàng · cuộc họp được lưu cục bộ",
            ["Workspace ready · your session is secure"] = "Workspace đã sẵn sàng · phiên của bạn được bảo vệ",
            ["Capture paused · press Resume when you are ready"] = "Đã tạm dừng ghi âm · nhấn Tiếp tục khi bạn sẵn sàng",
            ["Capture resumed"] = "Đã tiếp tục ghi âm",
            ["Meeting ready · transcript and summary are saved locally"] = "Cuộc họp đã sẵn sàng · transcript và tóm tắt đã được lưu cục bộ",
            ["Processing paused"] = "Đã tạm dừng xử lý",
            ["Your capture is still available. Retry whenever you are ready."] = "Bản ghi vẫn còn. Hãy xử lý lại khi bạn sẵn sàng.",
            ["We hit a processing problem"] = "Đã xảy ra lỗi khi xử lý",
            ["The meeting stays available locally. Check your connection or retry."] = "Cuộc họp vẫn được lưu cục bộ. Hãy kiểm tra kết nối hoặc thử lại.",
            ["Processing paused · your recording is available to retry"] = "Đã tạm dừng xử lý · bạn có thể xử lý lại bản ghi",
            ["Meeting changes saved to your local workspace"] = "Đã lưu thay đổi cuộc họp vào workspace cục bộ",
            ["Speaker names updated across your meetings"] = "Đã cập nhật tên người nói trong các cuộc họp",
            ["Settings saved · your preferences apply to the next recording"] = "Đã lưu cài đặt · tùy chọn sẽ áp dụng cho bản ghi tiếp theo",
            ["Could not save settings in time. Try again."] = "Không thể lưu cài đặt kịp thời. Hãy thử lại.",
            ["Could not save settings. Check your Windows profile and try again."] = "Không thể lưu cài đặt. Hãy kiểm tra hồ sơ Windows và thử lại.",
            ["Could not test OpenAI. Check the key and try again."] = "Không thể kiểm tra OpenAI. Hãy kiểm tra key và thử lại.",
            ["The OpenAI API key format is invalid."] = "Định dạng API key OpenAI không hợp lệ.",
            ["Meetings stay on this device until you turn sync back on."] = "Cuộc họp ở trên thiết bị này cho đến khi bạn bật lại đồng bộ.",
            ["Cloud sync will run in the background when Firebase is connected."] = "Đồng bộ đám mây sẽ chạy nền khi Firebase được kết nối.",
            ["Create your workspace"] = "Tạo workspace của bạn",
            ["Welcome back"] = "Chào mừng bạn trở lại",
            ["A quieter way to remember every meeting."] = "Một cách nhẹ nhàng hơn để ghi nhớ mọi cuộc họp.",
            ["Your meetings, speakers, and next steps in one calm place."] = "Cuộc họp, người nói và bước tiếp theo trong một nơi rõ ràng.",
            ["Processing your meeting"] = "Đang xử lý cuộc họp",
            ["A clear record of every conversation and what happens next."] = "Bản ghi rõ ràng của mọi cuộc trò chuyện và điều xảy ra tiếp theo.",
            ["Choose what to capture. Nothing joins your call."] = "Chọn nội dung cần ghi. Không có bot tham gia cuộc gọi.",
            ["Local capture is active. Your meeting stays yours."] = "Đang ghi âm cục bộ. Cuộc họp vẫn thuộc về bạn.",
            ["Turning conversation into something you can use."] = "Đang chuyển cuộc trò chuyện thành nội dung hữu ích.",
            ["Review the signal, correct the record, and share the next steps."] = "Xem lại nội dung, chỉnh bản ghi và chia sẻ bước tiếp theo.",
            ["Keep names and voices consistent across your meetings."] = "Giữ tên và giọng nói nhất quán giữa các cuộc họp.",
            ["Tune capture, privacy, and your workspace preferences."] = "Điều chỉnh ghi âm, quyền riêng tư và tùy chọn workspace.",
            ["Capture provider"] = "Bộ ghi âm",
            ["Preview fallback"] = "Bản xem trước dự phòng",
            ["WASAPI ready"] = "WASAPI sẵn sàng",
            ["WASAPI · mic + system audio"] = "WASAPI · mic + âm thanh hệ thống",
            ["Preview fallback · check device permissions"] = "Bản xem trước dự phòng · kiểm tra quyền thiết bị",
            ["Local cache · ready to sync"] = "Bộ nhớ cục bộ · sẵn sàng đồng bộ",
            ["Firebase sync"] = "Đồng bộ Firebase",
            ["Firebase ready"] = "Firebase sẵn sàng",
            ["Local workspace"] = "Workspace cục bộ",
            ["Synced to Firebase"] = "Đã đồng bộ lên Firebase",
            ["Saved locally"] = "Đã lưu cục bộ",
            ["Enter your email address first."] = "Hãy nhập email trước.",
            ["Enter a valid email address."] = "Hãy nhập địa chỉ email hợp lệ.",
            ["Password must be at least 6 characters."] = "Mật khẩu phải có ít nhất 6 ký tự.",
            ["Tell us your name to create the workspace."] = "Hãy nhập tên để tạo workspace.",
            ["Firebase is not reachable. Try again later."] = "Không thể kết nối Firebase. Hãy thử lại sau.",
            ["Firebase could not authenticate this workspace."] = "Firebase không thể xác thực workspace này.",
            ["That email or password is not correct."] = "Email hoặc mật khẩu không đúng.",
            ["An account already exists for that email."] = "Email này đã có tài khoản.",
            ["Choose a stronger password."] = "Hãy chọn mật khẩu mạnh hơn.",
            ["Firebase is not reachable. Try again or use the local workspace."] = "Không thể kết nối Firebase. Hãy thử lại hoặc dùng workspace cục bộ.",
            ["Firebase returned an unexpected response. Try again."] = "Firebase trả về phản hồi không mong đợi. Hãy thử lại.",
            ["UPDATES"] = "C\u1eacP NH\u1eacT",
            ["Check GitHub for the latest Meeting Assistant release."] = "Ki\u1ec3m tra GitHub \u0111\u1ec3 xem b\u1ea3n ph\u00e1t h\u00e0nh Meeting Assistant m\u1edbi nh\u1ea5t.",
            ["GitHub Releases"] = "B\u1ea3n ph\u00e1t h\u00e0nh GitHub",
            ["Not configured"] = "Ch\u01b0a c\u1ea5u h\u00ecnh",
            ["Installed"] = "\u0110\u00e3 c\u00e0i",
            ["Check for updates"] = "Ki\u1ec3m tra c\u1eadp nh\u1eadt",
            ["Download and restart"] = "T\u1ea3i v\u00e0 kh\u1edfi \u0111\u1ed9ng l\u1ea1i",
            ["Update available"] = "C\u00f3 b\u1ea3n c\u1eadp nh\u1eadt",
            ["Updates are not configured for this build."] = "B\u1ea3n build n\u00e0y ch\u01b0a \u0111\u01b0\u1ee3c c\u1ea5u h\u00ecnh k\u00eanh c\u1eadp nh\u1eadt.",
            ["Updates are ready to check."] = "K\u00eanh c\u1eadp nh\u1eadt \u0111\u00e3 s\u1eb5n s\u00e0ng.",
            ["Checking for updates..."] = "\u0110ang ki\u1ec3m tra c\u1eadp nh\u1eadt...",
            ["You are up to date."] = "B\u1ea1n \u0111ang d\u00f9ng phi\u00ean b\u1ea3n m\u1edbi nh\u1ea5t.",
            ["Could not check for updates. Check your network and try again."] = "Kh\u00f4ng th\u1ec3 ki\u1ec3m tra c\u1eadp nh\u1eadt. H\u00e3y ki\u1ec3m tra m\u1ea1ng v\u00e0 th\u1eed l\u1ea1i.",
            ["Downloading update..."] = "\u0110ang t\u1ea3i b\u1ea3n c\u1eadp nh\u1eadt...",
            ["Update downloaded. Restarting Meeting Assistant."] = "\u0110\u00e3 t\u1ea3i b\u1ea3n c\u1eadp nh\u1eadt. Meeting Assistant s\u1ebd kh\u1edfi \u0111\u1ed9ng l\u1ea1i.",
            ["Could not install the update. Try again later."] = "Kh\u00f4ng th\u1ec3 c\u00e0i b\u1ea3n c\u1eadp nh\u1eadt. H\u00e3y th\u1eed l\u1ea1i sau.",
            ["No public release has been published yet."] = "Ch\u01b0a c\u00f3 b\u1ea3n ph\u00e1t h\u00e0nh c\u00f4ng khai.",
            ["GitHub update checks are rate limited. Try again later."] = "GitHub \u0111ang gi\u1edbi h\u1ea1n s\u1ed1 l\u1ea7n ki\u1ec3m tra. H\u00e3y th\u1eed l\u1ea1i sau.",
            ["Could not reach GitHub. Check the network and try again."] = "Kh\u00f4ng th\u1ec3 k\u1ebft n\u1ed1i GitHub. H\u00e3y ki\u1ec3m tra m\u1ea1ng v\u00e0 th\u1eed l\u1ea1i.",
            ["Updates are downloaded and installed automatically when the app is idle."] = "B\u1ea3n c\u1eadp nh\u1eadt s\u1ebd \u0111\u01b0\u1ee3c t\u1ea3i v\u00e0 c\u00e0i \u0111\u1eb7t t\u1ef1 \u0111\u1ed9ng khi \u1ee9ng d\u1ee5ng r\u1ea3nh.",
            ["AUTOMATIC UPDATE"] = "C\u1eacP NH\u1eacT T\u1ef0 \u0110\u1ed8NG",
            ["Downloading update"] = "\u0110ang t\u1ea3i b\u1ea3n c\u1eadp nh\u1eadt",
            ["Installing update"] = "\u0110ang c\u00e0i b\u1ea3n c\u1eadp nh\u1eadt",
            ["Updating Meeting Assistant"] = "\u0110ang c\u1eadp nh\u1eadt Meeting Assistant",
            ["A new version is being installed automatically. You can keep watching here while we finish."] = "Phi\u00ean b\u1ea3n m\u1edbi \u0111ang \u0111\u01b0\u1ee3c c\u00e0i t\u1ef1 \u0111\u1ed9ng. B\u1ea1n c\u00f3 th\u1ec3 theo d\u00f5i ti\u1ebfn tr\u00ecnh t\u1ea1i \u0111\u00e2y.",
            ["Preparing a secure download..."] = "\u0110ang chu\u1ea9n b\u1ecb t\u1ea3i xu\u1ed1ng an to\u00e0n...",
            ["Installing update in the background..."] = "\u0110ang c\u00e0i b\u1ea3n c\u1eadp nh\u1eadt trong n\u1ec1n...",
            ["Restarting Meeting Assistant"] = "\u0110ang kh\u1edfi \u0111\u1ed9ng l\u1ea1i Meeting Assistant",
            ["Restarting Meeting Assistant automatically..."] = "Meeting Assistant s\u1ebd t\u1ef1 \u0111\u1ed9ng kh\u1edfi \u0111\u1ed9ng l\u1ea1i...",
            ["Update is installing automatically. Restarting Meeting Assistant."] = "B\u1ea3n c\u1eadp nh\u1eadt \u0111ang \u0111\u01b0\u1ee3c c\u00e0i t\u1ef1 \u0111\u1ed9ng. Meeting Assistant s\u1ebd kh\u1edfi \u0111\u1ed9ng l\u1ea1i.",
            ["Update installed automatically. Restarting Meeting Assistant."] = "B\u1ea3n c\u1eadp nh\u1eadt \u0111\u00e3 \u0111\u01b0\u1ee3c c\u00e0i t\u1ef1 \u0111\u1ed9ng. Meeting Assistant s\u1ebd kh\u1edfi \u0111\u1ed9ng l\u1ea1i.",
            ["An update will install automatically when your meeting is finished."] = "B\u1ea3n c\u1eadp nh\u1eadt s\u1ebd c\u00e0i \u0111\u1eb7t khi cu\u1ed9c h\u1ecdp c\u1ee7a b\u1ea1n k\u1ebft th\u00fac.",
            ["Download"] = "T\u1ea3i xu\u1ed1ng",
            ["Install"] = "C\u00e0i \u0111\u1eb7t",
            ["Restart"] = "Kh\u1edfi \u0111\u1ed9ng l\u1ea1i",
            ["No action needed. Meeting Assistant will reopen when the update is complete."] = "Kh\u00f4ng c\u1ea7n thao t\u00e1c. Meeting Assistant s\u1ebd m\u1edf l\u1ea1i khi c\u1eadp nh\u1eadt xong."
        };

    private static readonly List<WeakReference<FrameworkElement>> Roots = [];
    private static readonly object RootLock = new();
    private static string _language = "vi";

    public static event EventHandler? LanguageChanged;

    public static string CurrentLanguage => _language;
    public static bool IsVietnamese => _language == "vi";

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(LocalizationService),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty OriginalTextProperty =
        DependencyProperty.RegisterAttached("OriginalText", typeof(string), typeof(LocalizationService));

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    public static void SetLanguage(string? language)
    {
        var normalized = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
        if (_language == normalized) return;
        _language = normalized;
        LanguageChanged?.Invoke(null, EventArgs.Empty);

        List<FrameworkElement> roots;
        lock (RootLock)
        {
            roots = Roots.Select(reference => reference.TryGetTarget(out var root) ? root : null)
                .Where(root => root is not null)
                .Cast<FrameworkElement>()
                .ToList();
            Roots.RemoveAll(reference => !reference.TryGetTarget(out _));
        }

        foreach (var root in roots)
        {
            if (root.Dispatcher.CheckAccess()) ApplyTo(root);
            else _ = root.Dispatcher.BeginInvoke(new Action(() => ApplyTo(root)), DispatcherPriority.Loaded);
        }
    }

    public static string Translate(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsVietnamese) return value ?? string.Empty;
        return Vietnamese.TryGetValue(value, out var translated) ? translated : value;
    }

    public static void Refresh()
    {
        List<FrameworkElement> roots;
        lock (RootLock)
        {
            roots = Roots.Select(reference => reference.TryGetTarget(out var root) ? root : null)
                .Where(root => root is not null)
                .Cast<FrameworkElement>()
                .ToList();
            Roots.RemoveAll(reference => !reference.TryGetTarget(out _));
        }

        foreach (var root in roots)
        {
            if (root.Dispatcher.CheckAccess()) ApplyTo(root);
            else _ = root.Dispatcher.BeginInvoke(new Action(() => ApplyTo(root)), DispatcherPriority.Loaded);
        }
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element) return;
        if ((bool)args.NewValue)
        {
            element.Loaded += RootLoaded;
            element.Unloaded += RootUnloaded;
            lock (RootLock) Roots.Add(new WeakReference<FrameworkElement>(element));
            if (element.IsLoaded) ApplyTo(element);
        }
        else
        {
            element.Loaded -= RootLoaded;
            element.Unloaded -= RootUnloaded;
            lock (RootLock) Roots.RemoveAll(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, element));
        }
    }

    private static void RootLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement element) ApplyTo(element);
    }

    private static void RootUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        lock (RootLock) Roots.RemoveAll(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, element));
    }

    private static void ApplyTo(DependencyObject node)
    {
        if (node is TextBlock textBlock && !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
        {
            var original = (string?)textBlock.GetValue(OriginalTextProperty);
            if (original is null)
            {
                original = textBlock.Text;
                textBlock.SetValue(OriginalTextProperty, original);
            }

            textBlock.Text = Translate(original);
        }

        if (node is ContentControl contentControl && !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty) && contentControl.Content is string content)
        {
            var original = (string?)contentControl.GetValue(OriginalTextProperty);
            if (original is null)
            {
                original = content;
                contentControl.SetValue(OriginalTextProperty, original);
            }

            contentControl.Content = Translate(original);
        }

        if (!BindingOperations.IsDataBound(node, ToolTipService.ToolTipProperty) && node is FrameworkElement element && element.ToolTip is string toolTip)
        {
            var original = (string?)element.GetValue(OriginalTextProperty);
            if (original is null)
            {
                original = toolTip;
                element.SetValue(OriginalTextProperty, original);
            }

            element.ToolTip = Translate(original);
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            ApplyTo(VisualTreeHelper.GetChild(node, index));
    }
}

public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => LocalizationService.Translate(value?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => WpfBinding.DoNothing;
}
