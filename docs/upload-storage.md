# External upload storage

All backend-managed product and category image uploads use one external root. Application code and bundled frontend assets (bank logos, CSS, JavaScript, etc.) remain in their normal project/publish locations.

Windows default:

```text
C:\AgroTradeData\uploads\products
C:\AgroTradeData\uploads\categories
```

Configuration:

```json
"Storage": {
  "UploadsPath": "C:\\AgroTradeData\\uploads",
  "PublicBaseUrl": ""
}
```

On the production server, prefer environment variables:

```text
Storage__UploadsPath=C:\AgroTradeData\uploads
Storage__PublicBaseUrl=https://api.fixtop.ge
```

PublicBaseUrl is optional; when empty, new image URLs use the request scheme/host/path base. Existing database image URLs do not need rewriting: `/uploads/products/...` and `/uploads/categories/...` are mapped to the external storage by ASP.NET Core's physical file provider. The storage root cannot be inside the application's content root or equal a drive root. On Linux override the Windows appsettings path with an external mounted directory; the code's fallback is `/var/lib/agrotrade/uploads`.

## Existing files and deployment

1. Before replacing or cleaning an existing server publish folder, back up and copy **all** of its `wwwroot/uploads` content into `C:\AgroTradeData\uploads`, preserving relative subfolders and filenames. The local workspace copy does not transfer files that exist only on the server.
2. Grant the backend process identity read/write access to that directory. Under IIS, grant Modify to the actual `IIS AppPool\<pool-name>` identity. No broad Everyone permission is required.
3. Configure the external directory, then deploy and restart the backend. The app initializes the root and its product/category subfolders. It also copies any legacy files still present in the application's `wwwroot/uploads`. Original files remain intact. Existing identical destination files are retained; conflicting contents cause an error rather than silent overwrite.
4. Verify old product/category URLs and upload one new image. The new image must appear in external storage.
5. Back up the external storage together with PostgreSQL. After verifying the migration and retaining a backup, legacy in-project copies can be retired as a separate operation.

Uploaded files are excluded from future backend publish output. They must not be managed as deployment artifacts. No database migration is required for this change.

Local transition: 9 existing upload files were copied to `C:\AgroTradeData\uploads` and verified by hash; originals were preserved. Upload storage checks cover external writes, unchanged URL structure, repeatable legacy copying, content conflicts, and invalid in-project root configuration.

Changed implementation: `UploadStorage.cs`, product/category controllers, `Program.cs`, appsettings files, and the API project publish rules. Existing payment checks also passed after this change.
