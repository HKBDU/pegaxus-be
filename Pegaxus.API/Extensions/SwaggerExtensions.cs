using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Pegaxus.API.Extensions;

public static class SwaggerExtensions
{
    public static void AddSwaggerServices(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo() { Title = "Pegaxus APIs", Version = "v1" });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Nhập JWT token, không cần nhập tiền tố Bearer."
            });

            options.OperationFilter<AuthorizeOperationFilter>();
            options.OperationFilter<VietnameseOperationFilter>();
        });
    }

    public sealed class VietnameseOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
    {
        public void Apply(
            OpenApiOperation operation,
            Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
        {
            var method = context.ApiDescription.HttpMethod?.ToUpperInvariant() ?? string.Empty;
            var path = context.ApiDescription.RelativePath?.Trim('/').ToLowerInvariant() ?? string.Empty;
            var documentation = GetDocumentation(method, path);

            if (documentation is null)
                return;

            operation.Summary = documentation.Value.Summary;
            operation.Description = documentation.Value.Description;
        }

        private static (string Summary, string Description)? GetDocumentation(string method, string path)
        {
            if (method == "GET" && path == "api/v1/health")
                return ("Kiểm tra trạng thái hệ thống", "Kiểm tra API đang hoạt động hay không.");

            if (path.StartsWith("api/v1/auth/"))
                return AuthDocumentation(method, path);

            if (path == "api/v1/uploads/signature")
                return ("Tạo chữ ký upload", "Tạo chữ ký Cloudinary cho file attachment của tài khoản đang đăng nhập. FE dùng thông tin upload trả về để tải file trực tiếp lên Cloudinary.");

            if (path == "api/v1/uploads/confirm")
                return ("Xác nhận upload", "Xác nhận publicId đã upload thành công và thuộc về tài khoản đang đăng nhập.");

            if (path == "api/v1/accounts/me")
            {
                return method switch
                {
                    "GET" => ("Xem hồ sơ cá nhân", "Lấy hồ sơ của tài khoản đang đăng nhập."),
                    "PATCH" => ("Cập nhật hồ sơ cá nhân", "Cập nhật các thông tin hồ sơ được gửi lên bằng multipart/form-data."),
                    _ => null
                };
            }

            if (path == "api/v1/accounts/me/password")
                return ("Đổi mật khẩu", "Đổi mật khẩu khi tài khoản đã đăng nhập. Mật khẩu hiện tại sai trả về lỗi 400.");

            if (path.StartsWith("api/v1/horses"))
                return HorseDocumentation(method, path);

            if (path.StartsWith("api/v1/employees"))
                return EmployeeDocumentation(method, path);

            if (path.StartsWith("api/v1/bookings"))
                return BookingDocumentation(method, path);

            if (path.EndsWith("/quarantine-proofs"))
                return ("Nộp nhanh bằng chứng kiểm dịch BCP", "Driver hoặc Escort được phân công chuyến nộp một hoặc nhiều ảnh bằng chứng tại BCP bằng multipart/form-data. Hệ thống tạo QuarantineProof chờ TS xác minh và không thay đổi trạng thái ComplianceDossier.");

            if (path.EndsWith("/customs-clearance-proofs"))
                return ("Nộp nhanh bằng chứng thông quan", "Driver được phân công chuyến nộp một hoặc nhiều ảnh bằng chứng hải quan bằng multipart/form-data. Hệ thống tạo CustomsClearanceProof chờ TS xác minh và không thay đổi trạng thái ComplianceDossier.");

            if (path.StartsWith("api/v1/quarantine-proofs/") && path.EndsWith("/decision") == false)
                return ("Xem chi tiết bằng chứng kiểm dịch", "Xem trạng thái, file bằng chứng và danh sách ngựa cần đối chiếu của một QuarantineProof.");

            if (path.StartsWith("api/v1/customs-clearance-proofs/") && !path.EndsWith("/finalize"))
                return ("Xem chi tiết bằng chứng hải quan", "Xem trạng thái, file bằng chứng và thông tin ATA Carnet của một CustomsClearanceProof.");

            if (path.EndsWith("/finalize"))
                return ("Chốt kết quả thông quan", "Transport Specialist xác minh MRN, phân luồng hải quan rồi chốt CLEARED, HELD hoặc REJECTED; cũng có thể từ chối proof để Driver nộp lại.");

            if (path.EndsWith("/finalize"))
                return ("Chốt kết quả kiểm dịch", "Transport Specialist đối chiếu 100% mã microchip rồi chốt PASSED, PASSED_WITH_CONDITIONS hoặc FAILED; cũng có thể từ chối bằng chứng để Driver hoặc Escort nộp lại.");

            if (path.Contains("api/v1/authority-submissions/") && path.EndsWith("/decision"))
                return ("Cập nhật kết quả xét duyệt hồ sơ", "Transport Specialist cập nhật kết quả APPROVED, REJECTED hoặc RETURNED cho lần nộp hồ sơ bằng multipart/form-data. File quyết định là bắt buộc; hồ sơ được cập nhật trạng thái theo kết quả và các lần nộp bắt buộc còn lại.");

            if (path.StartsWith("api/v1/compliance-dossiers"))
                return ComplianceDocumentation(method, path);

            return null;
        }

        private static (string Summary, string Description)? AuthDocumentation(string method, string path)
        {
            return path switch
            {
                "api/v1/auth/google/login" => ("Đăng nhập bằng Google", "Bắt đầu luồng OAuth Google. Trình duyệt được chuyển hướng sang Google để xác thực."),
                "api/v1/auth/google/complete" => ("Hoàn tất đăng nhập Google", "Callback nội bộ sau khi Google xác thực thành công."),
                "api/v1/auth/google/exchange" => ("Đổi mã Google lấy token", "Đổi mã đăng nhập Google thành access token và refresh token."),
                "api/v1/auth/login" => ("Đăng nhập", "Đăng nhập bằng email và mật khẩu."),
                "api/v1/auth/register" => ("Đăng ký tài khoản", "Tạo tài khoản Customer mới và gửi mã xác thực email."),
                "api/v1/auth/forgot-password" => ("Quên mật khẩu", "Gửi mã OTP đặt lại mật khẩu tới email tài khoản."),
                "api/v1/auth/verify-email" => ("Xác thực email", "Xác thực địa chỉ email bằng mã OTP."),
                "api/v1/auth/resend-otp" => ("Gửi lại mã OTP", "Gửi lại mã OTP xác thực email hoặc đặt lại mật khẩu."),
                "api/v1/auth/refresh-token" => ("Làm mới token", "Đổi refresh token lấy access token mới."),
                "api/v1/auth/reset-password" => ("Đặt lại mật khẩu", "Đặt mật khẩu mới sau khi xác thực OTP."),
                "api/v1/auth/logout" => ("Đăng xuất", "Thu hồi refresh token của phiên đăng nhập hiện tại."),
                _ => null
            };
        }

        private static (string Summary, string Description)? HorseDocumentation(string method, string path)
        {
            if (path == "api/v1/horses")
                return method switch
                {
                    "GET" => ("Danh sách ngựa", "Lấy danh sách ngựa thuộc Customer đang đăng nhập, hỗ trợ tìm kiếm và phân trang."),
                    "POST" => ("Thêm ngựa", "Tạo hồ sơ ngựa mới cho Customer đang đăng nhập."),
                    _ => null
                };

            return method switch
            {
                "GET" => ("Xem chi tiết ngựa", "Lấy đầy đủ thông tin của một con ngựa."),
                "PUT" => ("Cập nhật thông tin ngựa", "Thay toàn bộ thông tin ngựa. Không cho sửa nếu ngựa thuộc booking đã được duyệt."),
                "DELETE" => ("Xóa ngựa", "Xóa mềm ngựa khỏi danh sách sở hữu của Customer."),
                _ => null
            };
        }

        private static (string Summary, string Description)? EmployeeDocumentation(string method, string path)
        {
            if (path == "api/v1/employees")
                return method switch
                {
                    "GET" => ("Danh sách nhân viên", "LM xem danh sách tài khoản Employee, hỗ trợ lọc, tìm kiếm, sắp xếp và phân trang."),
                    "POST" => ("Cấp tài khoản nhân viên", "LM tạo tài khoản Employee và hệ thống tự sinh employeeCode."),
                    _ => null
                };

            if (path == "api/v1/employees/export")
                return ("Xuất danh sách nhân viên", "LM xuất danh sách tài khoản Employee theo bộ lọc hiện tại.");

            if (path.EndsWith("/status"))
                return ("Cập nhật trạng thái tài khoản nhân viên", "LM kích hoạt, vô hiệu hóa hoặc xóa mềm tài khoản Employee.");

            return method switch
            {
                "GET" => ("Xem chi tiết nhân viên", "LM xem thông tin chi tiết tài khoản Employee."),
                "PATCH" => ("Cập nhật thông tin làm việc", "LM cập nhật role hoặc trạng thái làm việc của Employee."),
                _ => null
            };
        }

        private static (string Summary, string Description)? BookingDocumentation(string method, string path)
        {
            if (path == "api/v1/bookings")
                return method switch
                {
                    "GET" => ("Danh sách yêu cầu vận chuyển", "Customer xem yêu cầu của mình; LM xem toàn bộ yêu cầu, có lọc, tìm kiếm, sắp xếp và phân trang."),
                    "POST" => ("Tạo yêu cầu vận chuyển", "Customer tạo yêu cầu vận chuyển mới."),
                    _ => null
                };

            if (path.EndsWith("/histories"))
                return ("Lịch sử yêu cầu vận chuyển", "Lấy lịch sử thay đổi trạng thái của yêu cầu, sắp xếp từ cũ đến mới.");
            if (path.EndsWith("/approve"))
                return ("Duyệt yêu cầu vận chuyển", "LM duyệt yêu cầu đang chờ xử lý.");
            if (path.EndsWith("/reject"))
                return ("Từ chối yêu cầu vận chuyển", "LM từ chối yêu cầu và ghi nhận lý do.");
            if (path.EndsWith("/cancel"))
                return ("Hủy yêu cầu vận chuyển", "Customer hủy yêu cầu đang ở trạng thái cho phép hủy.");
            if (path.EndsWith("/status"))
                return ("Chuyển trạng thái chỉnh sửa yêu cầu", "Customer chuyển yêu cầu giữa trạng thái DRAFT và PENDING.");
            if (path.Contains("/horses/"))
                return method switch
                {
                    "PATCH" => ("Cập nhật ghi chú ngựa", "Sửa ghi chú chăm sóc của ngựa trong yêu cầu."),
                    "DELETE" => ("Gỡ ngựa khỏi yêu cầu", "Gỡ ngựa khỏi yêu cầu nhưng không xóa ngựa khỏi danh sách sở hữu."),
                    _ => null
                };
            if (path.EndsWith("/horses"))
                return ("Thêm ngựa vào yêu cầu", "Thêm một con ngựa thuộc Customer vào yêu cầu vận chuyển.");

            return ("Xem chi tiết yêu cầu vận chuyển", "Lấy đầy đủ thông tin của một yêu cầu vận chuyển.");
        }

        private static (string Summary, string Description)? ComplianceDocumentation(string method, string path)
        {
            if (path.EndsWith("/authority-submissions"))
                return method switch
                {
                    "GET" => ("Danh sách lần nộp hồ sơ", "Lấy các lần nộp ComplianceDossier, mới nhất trước, kèm người nộp và file bằng chứng."),
                    "POST" => ("Ghi nhận nộp hồ sơ", "Transport Specialist ghi nhận lần nộp hồ sơ tới cơ quan chức năng bằng multipart/form-data."),
                    _ => null
                };

            return null;
        }
    }

    public sealed class AuthorizeOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
    {
        public void Apply(OpenApiOperation operation,
            Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
        {
            var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
            var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();
            var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();

            if (!requiresAuthorization || allowsAnonymous)
            {
                return;
            }

            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    }] = Array.Empty<string>()
                }
            };
        }
    }

    public static void UseSwaggerAPI(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            // foreach (var version in app.DescribeApiVersions().Select(version => version.GroupName))
            //     options.SwaggerEndpoint($"/swagger/{version}/swagger.json", version);

            options.DisplayRequestDuration();
            options.EnableTryItOutByDefault();
            options.EnableFilter();
            options.DocExpansion(DocExpansion.List);
        });

        app.MapGet("/", () => Results.Redirect("/swagger/index.html"))
            .WithTags(string.Empty);
    }
}
