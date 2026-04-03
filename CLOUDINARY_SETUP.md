# Cloudinary Integration Guide

## Overview
Your keyboard website now integrates **Cloudinary** for storing and managing product images. This replaces the local file upload system.

## Setup Steps

### 1. Get Cloudinary Credentials
1. Sign up at [cloudinary.com](https://cloudinary.com)
2. Go to **Dashboard** and copy:
   - **Cloud Name**
   - **API Key**
   - **API Secret**

### 2. Configure Settings
Update both files with your Cloudinary credentials:

**appsettings.json** & **appsettings.Development.json**
```json
{
  "Cloudinary": {
    "CloudName": "your_cloud_name",
    "ApiKey": "your_api_key",
    "ApiSecret": "your_api_secret"
  }
}
```

### 3. Product Upload Workflow
- **Add Product**: In Admin panel, upload image → automatically uploads to Cloudinary
- **Edit Product**: Upload new image or use existing URL
- **Delete Product**: Image remains in Cloudinary (optional manual cleanup from Cloudinary dashboard)

### 4. API Endpoint
- **Upload**: `POST /Admin/UploadProductImage`
  - Accepts: `multipart/form-data` with `image` file
  - Returns: `{ success: true, imageUrl: "https://..." }`

## Features
✅ Automatic image upload to Cloudinary  
✅ Secure HTTPS URLs  
✅ Organized folder: `keyboard_products/`  
✅ Unique filename handling  
✅ Fallback to default image if needed  

## Troubleshooting
- **"No image file provided"**: Check file input is not empty
- **Cloudinary upload failed**: Verify credentials in `appsettings.json`
- **Unauthorized error**: Check API Key and Secret are correct

## Notes
- Images are stored in the `keyboard_products` folder on Cloudinary
- All product images use secure HTTPS URLs
- No local image storage required anymore
