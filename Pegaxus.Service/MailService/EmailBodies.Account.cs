namespace Pegaxus.Service.MailService;

public static partial class EmailBodies
{
    /// <summary>One time code for email verification or password reset.</summary>
    public static string OtpBody(bool isVerification, string fullName, string otp, int expiresInMinutes) =>
        Header(
            isVerification ? "Verify your Pegaxus account" : "Reset your Pegaxus password",
            isVerification ? "Your Pegaxus verification code" : "Your Pegaxus password reset code") +
        $"""
              {Title(isVerification ? "Verify your email" : "Reset your password")}
              {Greeting(fullName)}
              {Paragraph(isVerification ? "Use this code to verify your Pegaxus account." : "Use this code to reset your Pegaxus password.")}
              <div class="px-otp" style="margin:0 0 20px;padding:20px 12px 20px 15px;text-align:center;background:#FFF6E5;border:1px solid #F39200;border-radius:16px;font-size:36px;line-height:1.2;font-weight:700;letter-spacing:10px;color:#181818;white-space:nowrap;">{Encode(otp)}</div>
              {Paragraph($"""This code expires in <strong class="px-strong" style="color:#000000;">{expiresInMinutes} minutes</strong>.""")}
              {Paragraph("""<strong class="px-strong" style="color:#000000;">Security notice:</strong> Never share this code with anyone. Pegaxus will never ask you for it.""")}
              {Muted(isVerification ? "If you did not create a Pegaxus account, you can safely ignore this email." : "If you did not request this, you can safely ignore this email.")}
        """ +
        Footer();
}
