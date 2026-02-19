using MastercardHostedCheckout.Models;
using MastercardHostedCheckout.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace MastercardHostedCheckout.Controllers
{
    public class CheckoutController : Controller
    {
        private readonly IMastercardService _mastercardService;
        private readonly MastercardConfig _config;

        public CheckoutController(IMastercardService mastercardService, IOptions<MastercardConfig> config)
        {
            _mastercardService = mastercardService;
            _config = config.Value;
        }

        private string GetMerchantIdForCurrency(string currency)
        {
            return currency?.ToUpper() switch
            {
                "KWD" => string.IsNullOrEmpty(_config.MerchantIdKwd) ? _config.MerchantId : _config.MerchantIdKwd,
                "USD" => string.IsNullOrEmpty(_config.MerchantIdUsd) ? _config.MerchantId : _config.MerchantIdUsd,
                "EUR" => string.IsNullOrEmpty(_config.MerchantIdEur) ? _config.MerchantId : _config.MerchantIdEur,
                _ => _config.MerchantId
            };
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> InitiateCheckout(decimal amount, string currency, bool isPaymentLink = false)
        {
            var orderId = "ORDER_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var merchantId = GetMerchantIdForCurrency(currency);

            var request = new InitiateCheckoutRequest
            {
                Interaction = new Interaction
                {
                    ReturnUrl = Url.Action("Success", "Checkout", new { orderId = orderId, merchantId = merchantId }, Request.Scheme),
                    Merchant = new Merchant
                    {
                        Name = "KIndus Dev Store",
                        // Url = Request.Scheme + "://" + Request.Host // Re-commenting: Still causing error even in v72
                    },
                    RedirectMerchantUrl = Url.Action("Error", "Checkout", null, Request.Scheme),
                    RetryAttemptCount = 3,
                    DisplayControl = new DisplayControl
                    {
                        CardSecurityCode = "MANDATORY",
                        BillingAddress = "OPTIONAL",
                        CustomerEmail = "OPTIONAL",
                        Shipping = "HIDE"
                    },
                    Action = new InteractionAction
                    {
                        ThreeDSecure = "MANDATORY"
                    }
                },
                Order = new Order
                {
                    Id = orderId,
                    Amount = currency?.ToUpper() == "KWD" ? amount.ToString("0.000") : amount.ToString("0.00"),
                    Currency = currency?.ToUpper(),
                    Description = "Order for " + amount.ToString("F2") + " " + currency
                }
            };

            System.Diagnostics.Debug.WriteLine($"[INITIATE] Generated ReturnUrl: {request.Interaction.ReturnUrl}");
            request.Interaction.Operation = "PURCHASE"; // Reverted from AUTHORIZE as merchant is not enabled for it

            var (response, errors) = await _mastercardService.InitiateCheckoutAsync(merchantId, request);

            if (response != null && response.Result == "SUCCESS")
            {
                ViewBag.SessionId = response.Session?.Id;
                ViewBag.MerchantId = merchantId;
                ViewBag.OrderId = orderId;
                ViewBag.Region = "TEST"; // Or extract from BaseUrl
                return View("HostedPage");
            }

            ViewBag.ErrorMessage = errors ?? "Failed to initiate session.";
            return View("Error");
        }

        [HttpPost]
        public async Task<IActionResult> DeletePaymentLink(string currency, string linkId)
        {
            var merchantId = GetMerchantIdForCurrency(currency);
            var success = await _mastercardService.DeletePaymentLinkAsync(merchantId, linkId);
            if (success)
            {
                return Json(new { success = true, message = "Link deleted successfully." });
            }
            return Json(new { success = false, message = "Failed to delete link." });
        }


        [HttpGet]
        public async Task<IActionResult> Success(string? orderId, string? merchantId, string? resultIndicator)
        {
            if (string.IsNullOrEmpty(orderId)) return HandleError("Order ID is missing.");

            // 1. استرجاع بيانات الطلب لمعرفة حالته الحالية
            var (orderResponse, orderErrors) = await _mastercardService.RetrieveOrderAsync(merchantId ?? _config.MerchantId, orderId);

            if (orderResponse == null)
                return HandleError($"Retrieve Order Failed: {orderErrors}");

            // 2. استخراج المبلغ والعملة والحالة
            if (!TryGetOrderData(orderResponse, out decimal amount, out string? currency, out string? orderStatus))
                return HandleError("Could not retrieve Amount or Currency from the order.");

            // 3. التعامل مع الحالات (Logic Flow)

            // أ: إذا تم السحب فعلياً (Captured)
            if (orderStatus == "CAPTURED")
            {
                ViewBag.PaymentStatus = "SUCCESSFUL";
                return View();
            }

            // ب: إذا تم التحقق فقط (Authenticated) - نحتاج لإكمال عملية الدفع PAY
            if (orderStatus == "AUTHENTICATED")
            {
                string? authTransactionId = FindAuthenticationTransactionId(orderResponse);
                if (string.IsNullOrEmpty(authTransactionId))
                    return HandleError("3DS Authentication ID not found.");

                var payTransactionId = "txn-pay-" + Guid.NewGuid().ToString("N").Substring(0, 6);

                // مناداة السيرفس الذي عدلته أنت (النسخة الجديدة)
                var (payResponse, payErrors) = await _mastercardService.CompletePaymentAsync(
                    merchantId ?? _config.MerchantId,
                    orderId,
                    payTransactionId,
                    authTransactionId,
                    amount,
                    currency ?? "");

                if (payResponse != null && IsPaymentApproved(payResponse, out _))
                {
                    ViewBag.PaymentStatus = "SUCCESSFUL";
                    ViewBag.PayTransactionId = payTransactionId;
                    return View();
                }

                return HandleError($"Payment Execution Failed: {payErrors}");
            }

            // ج: حالة الفشل أو عدم الاكتمال
            return HandleError($"Unexpected Order Status: {orderStatus}");
        }

        public IActionResult Error(string message)
        {
            ViewBag.ErrorMessage = message;
            return View();
        }

        private bool TryGetOrderData(JsonDocument response, out decimal amount, out string? currency, out string? status)
        {
            amount = 0;
            currency = null;
            status = response.RootElement.TryGetProperty("status", out JsonElement st) ? st.GetString() : null;

            if (response.RootElement.TryGetProperty("order", out JsonElement orderSection))
            {
                // Robust amount parsing: handle both number and string
                if (orderSection.TryGetProperty("amount", out JsonElement amt))
                {
                    if (amt.ValueKind == JsonValueKind.Number) amount = amt.GetDecimal();
                    else if (amt.ValueKind == JsonValueKind.String && decimal.TryParse(amt.GetString(), out decimal d)) amount = d;
                }

                if (orderSection.TryGetProperty("currency", out JsonElement cur)) currency = cur.GetString();
            }

            // Fallback to root properties
            if (amount == 0 && response.RootElement.TryGetProperty("amount", out JsonElement rootAmt))
            {
                if (rootAmt.ValueKind == JsonValueKind.Number) amount = rootAmt.GetDecimal();
                else if (rootAmt.ValueKind == JsonValueKind.String && decimal.TryParse(rootAmt.GetString(), out decimal d)) amount = d;
            }
            if (string.IsNullOrEmpty(currency) && response.RootElement.TryGetProperty("currency", out JsonElement rootCur)) currency = rootCur.GetString();

            return amount > 0 && !string.IsNullOrEmpty(currency);
        }

        private string? FindAuthenticationTransactionId(JsonDocument orderResponse)
        {
            if (orderResponse.RootElement.TryGetProperty("transaction", out JsonElement transactions))
            {
                foreach (var transaction in transactions.EnumerateArray())
                {
                    // Look for the successful 3DS authentication transaction
                    if (transaction.TryGetProperty("result", out JsonElement result) &&
                        result.GetString() == "SUCCESS")
                    {
                        // Check for authentication.3ds.transactionId
                        if (transaction.TryGetProperty("authentication", out JsonElement auth) &&
                            auth.TryGetProperty("3ds", out JsonElement threeDs))
                        {
                            var id = threeDs.TryGetProperty("transactionId", out JsonElement idEl) ? idEl.GetString() : null;
                            if (!string.IsNullOrEmpty(id)) return id;
                        }
                    }
                }
            }
            return null;
        }

        private bool IsPaymentApproved(JsonDocument payResponse, out string gatewayResult)
        {
            var root = payResponse.RootElement;
            gatewayResult = root.TryGetProperty("result", out JsonElement r) ? (r.GetString() ?? "UNKNOWN") : "UNKNOWN";

            return root.TryGetProperty("response", out JsonElement resp) &&
                   resp.TryGetProperty("gatewayCode", out JsonElement code) &&
                   code.GetString() == "APPROVED";
        }

        private IActionResult HandleError(string message, string? payTransactionId = null)
        {
            System.Diagnostics.Debug.WriteLine($"[SUCCESS CALLBACK] ERROR: {message}");
            ViewBag.ErrorMessage = message;
            ViewBag.PayTransactionId = payTransactionId;
            return View("Error");
        }
    }
}