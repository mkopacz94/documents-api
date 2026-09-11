# Documents API

ASP.NET Core Web API for the PIRT Dashboard's document sign-off workflow. A
document is uploaded once, then goes through three sequential signature
stages - **Opracował** (prepared), **Sprawdził** (checked), **Zatwierdził**
(approved) - each by a different, authorized user. Every stage stamps a row
of a signature table (name + UTC timestamp) onto the PDF.

**This API persists nothing except the signing log itself.** There is no
separate "document" record and no copy of the PDF anywhere server-side - a
document's identity is its file name, and its state is whatever
`DocumentSignature` rows are logged against that name. Upload and sign both
hand the current PDF bytes back to the caller; the frontend displays them
and is responsible for resubmitting them at the next signing stage.

## Workflow

1. `POST /api/documents` - upload a new document. Rejected if:
   - empty, over the size limit, or not a PDF
   - the file name doesn't follow the `<RepositoryId>#<ProjectName>#<Version>.pdf`
     convention (e.g. `729#VIPD2#v1.00.16.pdf`)

   On success, a blank three-row signature table is stamped onto the last
   page and the **PDF bytes are returned in the response body** (no
   `Content-Disposition`, so a browser renders it inline) for the frontend
   to display. Nothing is written to the database at this point. The
   canonical file name (identity, used in every later call) comes back in
   the `X-File-Name` header.

2. `GET /api/documents/status?fileName=...` - current status: which
   categories are signed, by whom, and which category is expected next.
   Pure metadata from the database; backs the "view with signing
   capability" screen (the frontend renders its own confirmation dialog
   before calling sign).

3. `POST /api/documents/sign` - signs the document for one category.
   `multipart/form-data` with three fields:
   - `file` - the PDF **exactly as the caller currently holds it** (what
     upload or the previous sign call returned)
   - `fileName` - the value from `X-File-Name` (see note below on why this
     is a separate field rather than the file's own name)
   - `category` - `Opracowal` / `Sprawdzil` / `Zatwierdzil`

   For the second and third stage, the uploaded file is hashed and compared
   against the immediately preceding stage's logged hash before anything is
   stamped - a stale or tampered copy is rejected (409) rather than
   silently signed over. Also rejected if:
   - that category is already signed for this file name (this is also what
     enforces the "no two uploads with the same name" rule: once a name has
     any signature logged, that stage can't be logged again - bump the
     version to start over)
   - it's signed out of order (categories must be applied Opracował → Sprawdził → Zatwierdził)
   - the caller doesn't hold the role mapped to that category (403)

   The signer's identity is taken from the authenticated caller's claims,
   never from the request body. On success, the updated PDF is returned as
   an attachment (`Content-Disposition: attachment`) for the user to
   download, with `X-Fully-Signed` and (if more stages remain)
   `X-Next-Expected-Category` headers. This is the only endpoint that
   writes to the database - one `INSERT` per successful call, nothing else
   to keep in sync.

4. `POST /api/documents/verify` - hash-verification tool: upload a PDF, get
   back whether it matches a signing event this API has logged.

### Why `fileName` is a separate form field

The obvious design would derive identity from the uploaded file's own
multipart file name. That breaks in practice: a browser round-trips the PDF
as `fetch(...).then(r => r.blob())`, and a `Blob` carries no name at all (only
a `File` does) - it's easy for a frontend to forget to re-attach one, or to
have an HTTP client silently substitute a local temp name. So identity is
carried explicitly in its own field instead, populated from the `X-File-Name`
header upload returned.

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
# Upload - the response body IS the PDF (for display); identity comes back in a header
curl -D - -o current.pdf -X POST http://localhost:5203/api/documents \
  -H "X-Dev-User: alice" \
  -F "file=@729#VIPD2#v1.00.16.pdf;type=application/pdf"
# => X-File-Name: 729#VIPD2#v1.00.16   (current.pdf is the file to show the user)

# Sign as Opracował - forward the exact bytes just received, plus the file name
curl -o current.pdf -X POST http://localhost:5203/api/documents/sign \
  -H "X-Dev-User: alice" -H "X-Dev-Roles: DocumentSigner.Opracowal" \
  -F "fileName=729#VIPD2#v1.00.16" -F "category=Opracowal" \
  -F "file=@current.pdf;type=application/pdf"

# Sign as Sprawdził - forward what the previous call just returned
curl -o current.pdf -X POST http://localhost:5203/api/documents/sign \
  -H "X-Dev-User: bob" -H "X-Dev-Roles: DocumentSigner.Sprawdzil" \
  -F "fileName=729#VIPD2#v1.00.16" -F "category=Sprawdzil" \
  -F "file=@current.pdf;type=application/pdf"

# ... and so on for Zatwierdzil - current.pdf ends up fully signed

# Check status at any point
curl -G http://localhost:5203/api/documents/status -H "X-Dev-User: alice" \
  --data-urlencode "fileName=729#VIPD2#v1.00.16"

# Verify a PDF against the database
curl -X POST http://localhost:5203/api/documents/verify \
  -H "X-Dev-User: alice" -F "file=@current.pdf;type=application/pdf"
```

## What's deliberately not implemented yet

Per the requirements, this is stage 1. **Not** implemented:

- Persisting the document server-side, copying it into a category-specific
  folder, and exposing a download portal for the signer. Only the signing
  log is stored; reintroducing file storage later (for stage 2) means
  adding a file store service and wiring it into `DocumentsController`
  without changing the DB schema.

## Project layout

Two projects: **Api** is the thin HTTP host (controllers, wire DTOs,
composition root); **Core** holds everything else - domain, persistence, PDF
rendering, and the dev auth fallback. Api references Core; Core has no
dependency on Api.

```
src/DocumentsApi.Api/                    # host project (Microsoft.NET.Sdk.Web)
  Controllers/DocumentsController.cs     # upload / status / sign / verify
  Dtos/                                  # request/response wire contracts
  Program.cs                             # composition root: DI, auth, EF, migrations
  appsettings*.json

src/DocumentsApi.Core/                   # class library (Microsoft.NET.Sdk + FrameworkReference)
  Domain/SignatureCategory.cs            # Opracowal/Sprawdzil/Zatwierdzil + required order
  Data/AppDbContext.cs                   # EF Core DbContext (MySQL via Pomelo)
  Data/Entities/                         # DocumentSignature (the log), SigningFailure
  Data/Migrations/                       # EF Core migrations
  Services/PdfSigningService.cs          # renders the blank table / fills one row
  Services/DocumentSignatureRepository.cs # the signing log - the only thing persisted
  Services/SigningFailureLogger.cs       # audit log for failed sign attempts
  Auth/DevHeaderAuthenticationHandler.cs # Development-only auth fallback
  Pdf/EmbeddedFontResolver.cs            # embedded-font PDF font resolver
  Options/                               # PdfSignature, DocumentUpload, Auth, SignaturePermissions
```

Core isn't a Web SDK project, but a few of its types (`AuthenticationHandler<T>`,
`IFormFile`) come from ASP.NET Core, so its `.csproj` adds
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` rather than
pulling in the full Web SDK.

### How signing works without a document record

There's no `Documents` table - just `DocumentSignatures` (one row per
completed stage, keyed by file name + category) and `SigningFailures`. A
document's current state is always derived by querying signatures for its
file name; nothing else needs to be kept in sync. Concretely:

- **Order and duplicates**: querying existing signatures for a file name and
  checking which categories are present is enough to know what's next and
  reject an already-used category - which is also what stops a name from
  being reused after it's started.
- **Staleness/tamper check**: each signature row stores the hash of the
  document *after* that stage was applied. Signing category *N* hashes the
  submitted file and compares it to category *N-1*'s stored hash (skipped
  for the first category, which has no predecessor).
- **One write per sign call**: the row inserted for a signature already
  carries everything (category, signer, timestamp, hash) - there's no
  second write (like a separate "current hash" column) that could drift out
  of sync, so no transaction is needed to keep two writes atomic.

`PdfSigningService` has two entry points:
- `RenderSignatureTable` draws the full blank table (all three category
  labels, empty signer/date cells) onto the freshly uploaded PDF. Used once,
  at upload time.
- `FillSignatureRow` draws just one row's signer/date cells, at a position
  computed from the same fixed layout (margins, row height, column widths)
  used to draw the table in the first place. It's given whatever PDF bytes
  the caller currently holds and only touches that one row - it never
  redraws borders or other rows, so there's no original copy to fall back
  to and no risk of duplicating table artwork.

## Managing migrations

The `DbContext` lives in Core, but EF tooling needs a runnable startup
project (Api) to load design-time services from - both projects reference
`Microsoft.EntityFrameworkCore.Design` for this reason. Run commands from
the repo root:

```bash
dotnet tool install --global dotnet-ef   # first time only
dotnet ef migrations add <Name> \
  --project src/DocumentsApi.Core/DocumentsApi.Core.csproj \
  --startup-project src/DocumentsApi.Api/DocumentsApi.Api.csproj \
  -o Data/Migrations
```

## Configuration reference

| Section              | Key               | Meaning                                              | Default |
|-----------------------|-------------------|-------------------------------------------------------|---------|
| `PdfSignature`        | `FontSize`        | Signature table font size (points)                    | `9`     |
|                       | `RowHeight`       | Row height (points)                                    | `18`    |
|                       | `MarginLeft/Right`| Table left/right margins (points)                      | `40`    |
|                       | `MarginBottom`    | Distance from page bottom (points)                     | `30`    |
| `DocumentUpload`      | `MaxFileSizeBytes`| Upload size limit                                      | `50 MB` |
| `Auth`                | `Authority`       | External identity provider issuer URL                  | *(empty - dev fallback)* |
|                       | `Audience`        | Expected JWT audience                                  | *(empty)* |
| `SignaturePermissions`| `Opracowal` etc.  | Role/group name per signature category                | `DocumentSigner.*` |
