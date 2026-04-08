# Custom Stripe Payment UI Setup Guide

## Overview
ระบบชำระเงินแบบ Custom UI ได้เตรียมไว้แล้ว โดยใช้ Stripe.js ซึ่งให้คุณควบคุม UI ได้อย่างเต็มที่แทนการใช้ Stripe's Hosted Checkout

## Files Added/Modified

### 1. Controllers
- **HomeController.cs** - เพิ่ม 2 API endpoints:
  - `CreatePaymentIntent()` - สร้าง PaymentIntent ใหม่
  - `ConfirmPayment()` - ยืนยันการชำระเงิน

### 2. Views
- **Views/Home/Checkout.cshtml** - หน้า checkout ใหม่ด้วย Custom Payment Form

### 3. Models
- **Models/StripeModels.cs** - Request/Response models สำหรับ Stripe APIs:
  - `CreatePaymentIntentRequest`
  - `ConfirmPaymentRequest`
  - `AddToCartRequest`
  - `UpdateCartSelectionRequest`

### 4. Styles
- **wwwroot/css/Home/Checkout.css** - Styling สำหรับ checkout page

### 5. Other
- **Views/Home/cart.cshtml** - ลบ alert notification สำหรับ coupon

## Process Flow

### Payment Flow
1. ผู้ใช้กด "Proceed to Checkout" ในหน้า Cart
2. System เตรียม payment data และ store session data
3. Redirect ไปหน้า Checkout ด้วย Custom Payment Form
4. ผู้ใช้กรอก:
   - ชื่อผู้ถือบัตร
   - Email
   - ข้อมูลบัตรเครดิต (ผ่าน Stripe Elements)
5. JavaScript จัดการการชำระเงิน:
   - สร้าง PaymentIntent
   - Confirm payment ผ่าน Stripe.js
   - Confirm payment ผ่าน backend API
6. หากสำเร็จ: บันทึก Order ลงฐานข้อมูล
7. Redirect กลับไปหน้า Cart

## ความสำคัญ: Stripe Configuration

### ขั้นตอนที่จำเป็น:
1. ไปที่ Stripe Dashboard
2. Copy Publishable Key และ Secret Key
3. Update `appsettings.json`:

```json
"Stripe": {
    "PublishableKey": "pk_test_YOUR_KEY_HERE",
    "SecretKey": "sk_test_YOUR_KEY_HERE"
}
```

หรือ ใช้ `appsettings.Development.json` สำหรับ development

## Payment Methods Supported
- Credit Card (Visa, Mastercard, Amex)
- อื่นๆตามที่ Stripe Elements รองรับ

## Security Features
- ✅ All payment data handled by Stripe (ไม่ store card data ในเซิร์ฟเวอร์)
- ✅ PCI DSS compliant ผ่าน Stripe.js
- ✅ HTTPS required
- ✅ CSRF protection จาก ASP.NET Core

## Testing

### Test Card Numbers (Stripe):
- **Success**: 4242 4242 4242 4242
- **Decline**: 4000 0000 0000 0002
- **Requires Auth**: 4000 0025 0000 3155

**Expiry**: Any future date (e.g., 12/25)
**CVC**: Any 3 digits

## Customization

### เปลี่ยน Styling
- ทั้งหมดอยู่ใน `wwwroot/css/Home/Checkout.css`
- ปรับสีจาก CSS variables ที่ top of file

### เปลี่ยน Stripe Elements Style
ดู `Checkout.cshtml` line ที่มี `elements.create('card', {...})`

### เปลี่ยน Form Fields
- Open `Checkout.cshtml`
- เพิ่ม/ลบ fields ตามต้องการ
- Update JavaScript function `initializeCheckout()` if needed

## Troubleshooting

### "Payment failed" Error
1. ตรวจสอบ Stripe Keys ใน `appsettings.json`
2. ตรวจสอบว่า public key ถูกต้อง
3. ตรวจสอบ console log ตรวจหา errors

### "Address not found" Error
1. ตรวจสอบว่าเลือก shipping address ก่อนชำระเงิน

### CORS Issues
ไม่น่าเกิด เพราะ Stripe.js ทำงานใน browser client-side

## API Endpoints

### POST /Home/CreatePaymentIntent
สร้าง PaymentIntent ใหม่
```
Request Body: { amount: 0 }
Response: {
    success: true,
    clientSecret: "pi_...",
    totalAmount: 100.50,
    subtotal: 100.50,
    discount: 0
}
```

### POST /Home/ConfirmPayment
ยืนยันการชำระเงินที่สำเร็จ
```
Request Body: { paymentIntentId: "pi_..." }
Response: {
    success: true,
    orderId: 123,
    message: "ชำระเงินเรียบร้อยแล้ว"
}
```

## Next Steps

1. เพิ่ม Stripe Keys ใน `appsettings.json`
2. Test ด้วย test card numbers
3. Setup Webhooks (optional) สำหรับ production
4. Customize styling ตามต้องการ
5. Test กับ real transactions ในโหมด Test Mode

## Notes
- ทั้งระบบ handle coupon discount และ shipping address
- Order ถูกบันทึก only เมื่อ payment succeeded
- Selected cart items ถูกลบหลัง successful payment
