using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace MastercardHostedCheckout.Models
{
    // 1. الطلب الرئيسي (Initiate Checkout)
    public class InitiateCheckoutRequest
    {
        [JsonPropertyName("apiOperation")]
        public string ApiOperation { get; set; } = "INITIATE_CHECKOUT";

        [JsonPropertyName("checkoutMode")]
        public string CheckoutMode { get; set; } = "WEBSITE";

        [JsonPropertyName("interaction")]
        public Interaction Interaction { get; set; } = new();

        [JsonPropertyName("order")]
        public Order Order { get; set; } = new();

        [JsonPropertyName("transaction")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Transaction? Transaction { get; set; }

        [JsonPropertyName("authentication")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AuthenticationData? Authentication { get; set; }
    }

    // 2. بيانات التحقق (مهم جداً للـ Success Callback)
    public class AuthenticationData
    {
        [JsonPropertyName("transactionId")]
        public string? TransactionId { get; set; }

        // تم إضافة هذا الكائن لأن رد البنك يحتوي على 3ds هرمي
        [JsonPropertyName("3ds")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ThreeDSData? ThreeDS { get; set; }
    }

    public class ThreeDSData
    {
        [JsonPropertyName("transactionId")]
        public string? TransactionId { get; set; }
    }

    // 3. بيانات التفاعل مع واجهة الدفع
    public class Interaction
    {
        [JsonPropertyName("operation")]
        public string Operation { get; set; } = "PURCHASE";

        [JsonPropertyName("merchant")]
        public Merchant Merchant { get; set; } = new();

        [JsonPropertyName("returnUrl")]
        public string? ReturnUrl { get; set; }

        [JsonPropertyName("cancelUrl")]
        public string? CancelUrl { get; set; }
        [JsonPropertyName("redirectMerchantUrl")] // حل إيرور CS0117
        public string? RedirectMerchantUrl { get; set; }
        [JsonPropertyName("retryAttemptCount")] // حل إيرور CS0117
        public int? RetryAttemptCount { get; set; }

        [JsonPropertyName("displayControl")]
        public DisplayControl? DisplayControl { get; set; }

        [JsonPropertyName("action")]
        public InteractionAction? Action { get; set; }
    }

    public class Merchant
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class DisplayControl
    {
        [JsonPropertyName("cardSecurityCode")]
        public string? CardSecurityCode { get; set; } // MANDATORY

        [JsonPropertyName("billingAddress")]
        public string? BillingAddress { get; set; } // HIDE or OPTIONAL

        [JsonPropertyName("customerEmail")] // حل إيرور CS0117
        public string? CustomerEmail { get; set; }
        [JsonPropertyName("shipping")] // حل إيرور CS0117
        public string? Shipping { get; set; }
    }

    public class InteractionAction
    {
        [JsonPropertyName("3DSecure")]
        public string? ThreeDSecure { get; set; } // MANDATORY لضمان الـ OTP
    }

    // 4. بيانات الطلب والعملة
    public class Order
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("amount")]
        public string? Amount { get; set; } // نصيحة: اتركه String لتجنب مشاكل الكسور

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    // 5. بيانات العملية (المستخدمة في PAY)
    public class Transaction
    {
        [JsonPropertyName("amount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Amount { get; set; }

        [JsonPropertyName("currency")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Currency { get; set; }

        [JsonPropertyName("authentication")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AuthenticationData? Authentication { get; set; }

        [JsonPropertyName("reference")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Reference { get; set; }
    }

    // 6. الرد من البوابة
    public class InitiateCheckoutResponse
    {
        [JsonPropertyName("result")]
        public string? Result { get; set; }

        [JsonPropertyName("session")]
        public Session? Session { get; set; }

        [JsonPropertyName("successIndicator")]
        public string? SuccessIndicator { get; set; }
    }

    public class Session
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }
}