namespace KeyboardWebsiteProject.Models;

public class CreatePaymentIntentRequest
{
    public long Amount { get; set; }
    public string Currency { get; set; } = "thb";
}

public class ConfirmPaymentRequest
{
    public string PaymentIntentId { get; set; } = string.Empty;
}
