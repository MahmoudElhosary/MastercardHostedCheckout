using MastercardHostedCheckout.Models;
using MastercardHostedCheckout.Services;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;

namespace MastercardHostedCheckout
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1. تسجيل الخدمات
            builder.Services.AddControllersWithViews();
            builder.Services.AddRazorPages();
            builder.Services.Configure<MastercardConfig>(builder.Configuration.GetSection("Mastercard"));
            builder.Services.AddHttpClient();
            builder.Services.AddHttpClient<IMastercardService, MastercardService>();

            var app = builder.Build();

            // 2. إعداد الـ Middleware
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            // --- الفلو المعتمد: PURCHASE FLOW ---

            // أولاً: إنشاء الجلسة (طلب صفحة الدفع)
            app.MapPost("/api/mpgs/session", async (IOptions<MastercardConfig> options, IHttpClientFactory factory) =>
            {
                var opt = options.Value;
                var orderId = $"ORD_{Guid.NewGuid():N}".Substring(0, 10).ToUpper();
                var mId = string.IsNullOrEmpty(opt.MerchantIdKwd) ? opt.MerchantId : opt.MerchantIdKwd;

                var url = $"{opt.BaseUrl}/api/rest/version/{opt.ApiVersion}/merchant/{mId}/session";

                var payload = new
                {
                    apiOperation = "CREATE_CHECKOUT_SESSION",
                    interaction = new
                    {
                        operation = "PURCHASE",
                        returnUrl = $"{opt.ReturnUrl}?orderId={orderId}",
                        // 🎯 هذا السطر يمنع ظهور الحقول والرسائل الحمراء تماماً
                        displayControl = new { billingAddress = "HIDE" }
                    },
                    order = new
                    {
                        id = orderId,
                        amount = "2.000",
                        currency = "KWD",
                        // 🎯 يجب أن يكون العنوان هنا لكي تعتبره البوابة "موجوداً" ومكتملاً
                        billing = new
                        {
                            address = new
                            {
                                street = "Mubarak Al-Kabir St",
                                city = "Kuwait City",
                                postcode = "12345",
                                country = "KWT"
                            }
                        }
                    }
                };

                var client = factory.CreateClient();
                var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"merchant.{mId}:{opt.ApiPassword}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

                var response = await client.PostAsJsonAsync(url, payload);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return Results.Problem($"Gateway Error: {json}");

                using var doc = JsonDocument.Parse(json);
                var sessionId = doc.RootElement.GetProperty("session").GetProperty("id").GetString();

                return Results.Json(new { orderId, sessionId });
            });

            // ثانياً: الاستعلام عن النتيجة (بعد عودة العميل من صفحة الـ OTP)
            app.MapGet("/api/mpgs/verify/{orderId}", async (string orderId, IOptions<MastercardConfig> options, IHttpClientFactory factory) =>
            {
                var opt = options.Value;
                var mId = string.IsNullOrEmpty(opt.MerchantIdKwd) ? opt.MerchantId : opt.MerchantIdKwd;

                var client = factory.CreateClient();
                var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"merchant.{mId}:{opt.ApiPassword}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

                // نسأل البوابة عن حالة الطلب النهائي
                var url = $"{opt.BaseUrl}/api/rest/version/{opt.ApiVersion}/merchant/{mId}/order/{orderId}";
                var response = await client.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                var result = doc.RootElement.GetProperty("result").GetString();
                var status = doc.RootElement.GetProperty("status").GetString();

                // إذا كانت النتيجة SUCCESS، فهذا يعني أن الخصم تم بنجاح بعد الـ OTP
                return Results.Json(new
                {
                    IsSuccess = (result == "SUCCESS"),
                    Status = status,
                    FullResponse = doc.RootElement
                });
            });

            app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
            app.Run();
        }
    }
}