# Documents API

ASP.NET Core Web API for the PIRT Dashboard's document sign-off workflow. A
document is uploaded once, then goes through three sequential signature
stages - **Opracował** (prepared), **Sprawdził** (checked), **Zatwierdził**
(approved) - each by a different, authorized user. Every stage stamps a row
of a signature table (name + UTC timestamp) onto the PDF, and every
successful signing is logged to MySQL via EF Core together with a hash of
the resulting document.

## Workflow

1. `POST /api/documents` - upload a new document. Rejected if:
   - empty, over the size limit, or not a PDF
   - the file name doesn't follow the `<RepositoryId>#<ProjectName>#<Version>.pdf`
     convention (e.g. `729#VIPD2#v1.00.16.pdf`)
   - a document with that exact name already exists (bump the version instead -
     this is the versioning/no-duplicate-names requirement)

   On success, a blank three-row signature table is stamped onto the last
   page and the document is stored, unsigned.

2. `GET /api/documents/{id}` - current status: which categories are signed,
   by whom, and which category is expected next. Backs the "view with
   signing capability" screen (the frontend renders its own confirmation
   dialog before calling sign).

3. `POST /api/documents/{id}/sign` - signs the document for one category
   (`{"category":"Opracowal"}` / `Sprawdzil` / `Zatwierdzil`). Rejected if:
   - that category is already signed
   - it's signed out of order (categories must be applied Opracował → Sprawdził → Zatwierdził)
   - the caller doesn't hold the role mapped to that category (403)

   The signer's identity is taken from the authenticated caller's claims,
   never from the request body.

4. `GET /api/documents/{id}/file` - downloads the document in its current
   state.

5. `POST /api/documents/verify` - hash-verification tool: upload a PDF, get
   back whether it matches a document/stage this API produced (comparing
   against the hashes recorded in the database).

## Authentication & permission groups

This API does **not** manage its own users. It trusts an external identity
provider and expects a bearer JWT with role/group claims - configure the
provider under `Auth` in `appsettings.json`:

```json
"Auth": {
  "Authority": "https://your-identity-provider/issuer",
  "Audience": "documents-api",
  "RoleClaimType": "...",
  "NameClaimType": "..."
}
```

Each signature category requires a distinct role/group from the provider,
mapped under `SignaturePermissions`:

```json
"SignaturePermissions": {
  "Opracowal": "DocumentSigner.Opracowal",
  "Sprawdzil": "DocumentSigner.Sprawdzil",
  "Zatwierdzil": "DocumentSigner.Zatwierdzil"
}
```

Create these three groups in your identity provider and assign users to
whichever stage(s) they're authorized to sign.

### Local development without a real identity provider

If `Auth:Authority` is empty **and** the environment is `Development`, the
API falls back to a header-based dev auth scheme
(`Auth/DevHeaderAuthenticationHandler.cs`) so it can be exercised without a
running OIDC provider. Send:

- `X-Dev-User: alice` - the signed-in user's name
- `X-Dev-Roles: DocumentSigner.Opracowal,DocumentSigner.Sprawdzil` - comma-separated roles

This path is never used outside Development.

## Running locally

### 1. Start MySQL

```bash
docker compose up -d
```

Starts MySQL 8 on `localhost:3306` (db `documents_api`, user
`documents_api` / `changeme` - matches the default connection string).

### 2. Run the API

```bash
cd src/DocumentsApi.Api
dotnet run
```

Migrations apply automatically on startup. Swagger UI is at `/swagger` in Development.

### 3. Try it (dev header auth)

```bash
# Upload
curl -X POST http://localhost:5203/api/documents \
  -H "X-Dev-User: alice" \
  -F "file=@729#VIPD2#v1.00.16.pdf;type=application/pdf"
# => {"id":1, ...}

# Sign as Opracował
curl -X POST http://localhost:5203/api/documents/1/sign \
  -H "X-Dev-User: alice" -H "X-Dev-Roles: DocumentSigner.Opracowal" \
  -H "Content-Type: application/json" -d '{"category":"Opracowal"}'

# ... Sprawdzil by another user, then Zatwierdzil ...

# Download current state
curl http://localhost:5203/api/documents/1/file -H "X-Dev-User: alice" -o signed.pdf

# Verify a PDF against the database
curl -X POST http://localhost:5203/api/documents/verify \
  -H "X-Dev-User: alice" -F "file=@signed.pdf;type=application/pdf"
```

## What's deliberately not implemented yet

Per the requirements, this is stage 1. **Not** implemented:

- Copying the signed document into a category-specific folder and exposing
  a persistent download portal for the signer - the current
  `IDocumentFileStore`/`LocalDiskDocumentFileStore` is a placeholder
  abstraction so this can be swapped in without touching the controller.

## Project layout

```
src/DocumentsApi.Api/
  Controllers/DocumentsController.cs     # upload / status / sign / download / verify
  Services/PdfSigningService.cs          # renders the 3-row signature table
  Services/DocumentRepository.cs         # Document + DocumentSignature persistence
  Services/LocalDiskDocumentFileStore.cs # original + current PDF bytes on disk
  Services/SigningFailureLogger.cs       # audit log for failed sign attempts
  Domain/SignatureCategory.cs            # Opracowal/Sprawdzil/Zatwierdzil + required order
  Auth/DevHeaderAuthenticationHandler.cs # Development-only auth fallback
  Data/AppDbContext.cs                   # EF Core DbContext (MySQL via Pomelo)
  Data/Migrations/                       # EF Core migrations
  Pdf/EmbeddedFontResolver.cs            # embedded-font PDF font resolver
  Options/                               # PdfSignature, DocumentStorage, Auth, SignaturePermissions
```

## Managing migrations

```bash
cd src/DocumentsApi.Api
dotnet tool install --global dotnet-ef   # first time only
dotnet ef migrations add <Name> -o Data/Migrations
```

## Configuration reference

| Section              | Key               | Meaning                                              | Default |
|-----------------------|-------------------|-------------------------------------------------------|---------|
| `PdfSignature`        | `FontSize`        | Signature table font size (points)                    | `9`     |
|                       | `RowHeight`       | Row height (points)                                    | `18`    |
|                       | `MarginLeft/Right`| Table left/right margins (points)                      | `40`    |
|                       | `MarginBottom`    | Distance from page bottom (points)                     | `30`    |
| `DocumentStorage`     | `MaxFileSizeBytes`| Upload size limit                                      | `25 MB` |
|                       | `BasePath`        | Where original/current PDFs are stored                | `App_Data/documents` |
| `Auth`                | `Authority`       | External identity provider issuer URL                  | *(empty - dev fallback)* |
|                       | `Audience`        | Expected JWT audience                                  | *(empty)* |
| `SignaturePermissions`| `Opracowal` etc.  | Role/group name per signature category                | `DocumentSigner.*` |
