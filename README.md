# Documents API

## Summary

Documents API provides backend services supporting the document sign-off
workflow within the PIRT department.

A document is uploaded once, then goes through three sequential signature
stages - **Opracował** (prepared), **Sprawdził** (checked), **Zatwierdził**
(approved) - each by a different, authorized user. Every stage stamps a row
of a signature table (name + UTC timestamp) onto the PDF.

**This API persists nothing except the signing log itself.** There is no
separate "document" record and no copy of the PDF anywhere server-side - a
document's identity is its file name, and its state is whatever signature
rows are logged against that name. Upload and sign both hand the current PDF
bytes back to the caller; the frontend displays them and is responsible for
resubmitting them at the next signing stage.

### Current Features

- Document upload
  - Validate file type, size and the `<RepositoryId>#<ProjectName>#<Version>`
    naming convention.
  - Stamp a blank three-row signature table onto the uploaded PDF.
- Sequential three-stage e-signing (Opracował → Sprawdził → Zatwierdził)
  - Enforce signing order and reject a category that's already signed.
  - Restrict each category to callers holding the matching role/group.
  - Reject a stale or tampered file via a hash check against the previous
    stage's logged result.
  - Batch signing: sign several documents for one category in a single
    request, best-effort per file - one file failing doesn't stop the rest.
- Signing status lookup for a document by file name.
- Hash-based verification: upload a PDF and check whether it matches a
  signing event this API has logged.

Additional document-related features may be added in future releases.

### How to run the application

1. Clone this repository.
2. Start the MySQL dependency: `docker compose up -d` (provisions MySQL 8 on
   `localhost:3306`, matching the default connection string below). This
   repository does not currently ship a Dockerfile for the API itself -
   `docker-compose.yml` only covers the database.
3. Configure the application by editing the configuration file or setting
   environment variables (see Configuration below).
4. Run the application using the .NET CLI:
   ```bash
   cd src/DocumentsApi.Api
   dotnet run
   ```
   EF Core migrations are applied automatically on startup. Swagger UI is
   available at `/swagger` in Development.

### Configuration

The application listens on Kestrel's default port configuration (override
with `ASPNETCORE_URLS`/`ASPNETCORE_HTTP_PORTS` if needed - no fixed port is
hardcoded).

You can configure application settings using both the `appsettings.json`
file and environment variables. `appsettings.json` values can be overridden
by environment variables.

**Double underscore (\_\_) in environment variables is mandatory.**

| ENV | ENV required | Appsettings.json entry | Description | Default Value |
| --- | --- | --- | --- | --- |
| CONNECTIONSTRINGS\_\_DOCUMENTSDB | Yes | ConnectionStrings:DocumentsDb | MySQL connection string | *(none - the application fails to start without it)* |
| AUTH\_\_AUTHORITY | No | Auth:Authority | External OIDC identity provider issuer URL | *(empty - falls back to header-based dev auth in Development only)* |
| AUTH\_\_AUDIENCE | No | Auth:Audience | Expected JWT `aud` claim | *(empty)* |
| AUTH\_\_REQUIREHTTPSMETADATA | No | Auth:RequireHttpsMetadata | Require HTTPS for JWT discovery metadata | `true` |
| AUTH\_\_ROLECLAIMTYPE | No | Auth:RoleClaimType | Claim type carrying role/group membership | `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` |
| AUTH\_\_NAMECLAIMTYPE | No | Auth:NameClaimType | Claim type carrying the signer's display name | `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` |
| DOCUMENTUPLOAD\_\_MAXFILESIZEBYTES | No | DocumentUpload:MaxFileSizeBytes | Maximum accepted upload size, in bytes | `52428800` (50 MB) |
| DOCUMENTUPLOAD\_\_MAXBATCHSIZE | No | DocumentUpload:MaxBatchSize | Maximum number of files accepted in one batch-sign request | `20` |
| PDFSIGNATURE\_\_FONTSIZE | No | PdfSignature:FontSize | Signature table font size, in points | `9` |
| PDFSIGNATURE\_\_ROWHEIGHT | No | PdfSignature:RowHeight | Signature table row height, in points | `18` |
| PDFSIGNATURE\_\_MARGINLEFT | No | PdfSignature:MarginLeft | Signature table left margin, in points | `40` |
| PDFSIGNATURE\_\_MARGINRIGHT | No | PdfSignature:MarginRight | Signature table right margin, in points | `40` |
| PDFSIGNATURE\_\_MARGINBOTTOM | No | PdfSignature:MarginBottom | Table distance from the page bottom, in points | `30` |
| SIGNATUREPERMISSIONS\_\_OPRACOWAL | No | SignaturePermissions:Opracowal | Role/group required to sign as "Opracował" | `DocumentSigner.Opracowal` |
| SIGNATUREPERMISSIONS\_\_SPRAWDZIL | No | SignaturePermissions:Sprawdzil | Role/group required to sign as "Sprawdził" | `DocumentSigner.Sprawdzil` |
| SIGNATUREPERMISSIONS\_\_ZATWIERDZIL | No | SignaturePermissions:Zatwierdzil | Role/group required to sign as "Zatwierdził" | `DocumentSigner.Zatwierdzil` |

If `Auth:Authority` is left empty **and** the environment is `Development`,
the API falls back to a header-based dev auth scheme
(`X-Dev-User`/`X-Dev-Roles`) so it can be exercised without a running OIDC
provider. This path is never used outside Development.

### External dependencies

API requires access to the following services:

- MySQL 8.x database, via the Pomelo EF Core provider - the only state this
  API persists (the signing log and a failed-signing-attempt audit log).
- An external OIDC-compliant identity provider (e.g. Keycloak) issuing
  bearer JWTs with role/group claims, for authentication and authorization
  in any non-Development environment.

## Workflow

1. `POST /api/documents` - upload a new document. Rejected if empty, over
   the size limit, not a PDF, or the file name doesn't follow the
   `<RepositoryId>#<ProjectName>#<Version>.pdf` convention (e.g.
   `729#VIPD2#v1.00.16.pdf`) - `Version` must contain exactly one or two
   dots. On success, a blank three-row signature table is stamped onto the
   last page and the PDF bytes are returned in the response body (no
   `Content-Disposition`, so a browser renders it inline). Nothing is
   written to the database at this point. The canonical file name (used in
   every later call) comes back in the `X-File-Name` header.

2. `GET /api/documents/status?fileName=...` - current status: which
   categories are signed, by whom, and which category is expected next.

3. `POST /api/documents/sign` - signs the document for one category.
   `multipart/form-data` with three fields: `file` (the PDF exactly as the
   caller currently holds it), `fileName` (the value from `X-File-Name`),
   and `category` (`Opracowal` / `Sprawdzil` / `Zatwierdzil`). For the
   second and third stage, the uploaded file is hashed and compared against
   the immediately preceding stage's logged hash before anything is
   stamped - a stale or tampered copy is rejected (`409`). Also rejected if
   that category is already signed (`409`), it's signed out of order
   (`409`), or the caller doesn't hold the role mapped to that category
   (`403`). The signer's identity is taken from the authenticated caller's
   claims, never from the request body. On success, the updated PDF is
   returned as an attachment, with `X-Fully-Signed` and (if more stages
   remain) `X-Next-Expected-Category` headers. This is the only endpoint
   that writes to the database.

4. `POST /api/documents/verify` - hash-verification tool: upload a PDF, get
   back whether it matches a signing event this API has logged.

5. `POST /api/documents/sign/batch` - signs several documents in one call.
   `multipart/form-data` with a `Files[i].file` / `Files[i].fileName` /
   `Files[i].category` triple per document (e.g. `Files[0].fileName`,
   `Files[1].fileName`, ...), up to `DocumentUpload:MaxBatchSize` entries
   (`400 BATCH_TOO_LARGE` beyond that, `400 NO_FILES_PROVIDED` for zero).
   Each file goes through exactly the same rules as `sign` above, and files
   are processed **sequentially, in the order submitted** - not in
   parallel - so a duplicate or out-of-order entry within the batch itself
   (e.g. the same file/category listed twice) is caught the same way a
   second real request would be, rather than racing. This is a
   **best-effort** batch, not all-or-nothing: one file failing doesn't stop
   the rest from being attempted. The response is always `200` with a
   `signed-documents.zip` containing one `{fileName}.pdf` entry per
   successfully signed file, plus a `results.json` entry listing every
   file's outcome (a failed file has no corresponding PDF entry, only a row
   here with its `errorCode`/`errorMessage`/`errorData`, same shape as the
   error responses below).

### Error responses

Every non-2xx response is a standard `ProblemDetails` (RFC 7807) body with
two extra fields: a stable `errorCode` for the frontend to map to a
localized message, and (where relevant) an `errorData` object carrying the
raw values needed to build that message.

```json
{
  "status": 409,
  "title": "OUT_OF_ORDER_SIGNATURE",
  "detail": "Signatures must be applied in order. The next expected category for '729#VIPD2#v1.00.16' is 'Sprawdzil'.",
  "errorCode": "OUT_OF_ORDER_SIGNATURE",
  "errorData": { "fileName": "729#VIPD2#v1.00.16", "nextExpectedCategory": "Sprawdzil" }
}
```

Codes in use: `EMPTY_FILE`, `FILE_TOO_LARGE` (`errorData.maxSizeBytes`),
`UNSUPPORTED_FILE_TYPE`, `INVALID_FILE_NAME`, `FILE_NAME_REQUIRED`,
`FILE_PROCESSING_FAILED`, `ALREADY_SIGNED` (`errorData.fileName`,
`errorData.category`), `OUT_OF_ORDER_SIGNATURE` (`errorData.fileName`,
`errorData.nextExpectedCategory`), `ROLE_NOT_AUTHORIZED`
(`errorData.category`, `errorData.requiredRole`), `STALE_DOCUMENT_STATE`
(`errorData.fileName`), `SIGNING_FAILED`, `NO_FILES_PROVIDED`,
`BATCH_TOO_LARGE` (`errorData.maxBatchSize`). Defined in
`src/DocumentsApi.Api/Errors/ErrorCodes.cs`. The per-file entries in a batch
response's `results.json` use these same codes.

A bare `401` (no token, or an invalid one) doesn't go through this - it's
handled by the authentication middleware itself, before any controller code
runs, so it currently has no body at all.

## Project layout

Two projects: **Api** is the thin HTTP host (controllers, wire DTOs,
composition root); **Core** holds everything else - domain, persistence, PDF
rendering, and the dev auth fallback. Api references Core; Core has no
dependency on Api.

```
src/DocumentsApi.Api/                    # host project (Microsoft.NET.Sdk.Web)
  Controllers/DocumentsController.cs     # upload / status / sign / sign-batch / verify
  Dtos/                                  # request/response wire contracts
  Errors/                                # ErrorCodes + the ProblemDetails-building helper
  Validation/DocumentFileNameValidator.cs # file name convention parsing - unit tested
  Program.cs                             # composition root: DI, auth, EF, migrations
  appsettings*.json

src/DocumentsApi.Core/                   # class library (Microsoft.NET.Sdk + FrameworkReference)
  Domain/SignatureCategory.cs            # Opracowal/Sprawdzil/Zatwierdzil + required order
  Data/AppDbContext.cs                   # EF Core DbContext (MySQL via Pomelo)
  Data/Entities/                         # DocumentSignature (the log), SigningFailure
  Data/Migrations/                       # EF Core migrations
  Services/DocumentSignatureRepository.cs # the signing log - the only thing persisted
  Services/SigningFailureLogger.cs       # audit log for failed sign attempts
  Services/Signing/                      # ISigningWorkflowService, IDocumentProcessingService + their
                                          # command/result types (SignDocumentCommand, SigningOutcome, ...)
  Auth/DevHeaderAuthenticationHandler.cs # Development-only auth fallback
  Pdf/                                   # IPdfSigningService, EmbeddedFontResolver, SignatureRowInfo
  Options/                               # PdfSignature, DocumentUpload, Auth, SignaturePermissions

tests/DocumentsApi.Api.Tests/            # xUnit - references Api directly, no HTTP or real DB needed
```

## Running tests

```bash
dotnet test tests/DocumentsApi.Api.Tests/DocumentsApi.Api.Tests.csproj
```

`DocumentFileNameValidator` is a pure `string -> bool`/struct function with
no ASP.NET Core or EF Core dependencies, so its tests need no mocking, no
`IFormFile`, and no database. `SigningWorkflowService` and
`DocumentProcessingService` are tested against hand-rolled fakes of their
interfaces rather than a mocking library. `DocumentSignatureRepository` is
the one class that genuinely needs EF Core, so its tests run against the EF
Core InMemory provider instead of MySQL - except the duplicate-key race,
which InMemory can't reproduce (it doesn't enforce unique indexes), so that
one test overrides `SaveChangesAsync` to throw the same shape of
`DbUpdateException`/`MySqlException` the real driver would.

## Managing migrations

The `DbContext` lives in Core, but EF tooling needs a runnable startup
project (Api) to load design-time services from. Run commands from the repo
root:

```bash
dotnet tool install --global dotnet-ef   # first time only
dotnet ef migrations add <Name> \
  --project src/DocumentsApi.Core/DocumentsApi.Core.csproj \
  --startup-project src/DocumentsApi.Api/DocumentsApi.Api.csproj \
  -o Data/Migrations
```

## What's deliberately not implemented yet

Per the requirements, this is stage 1. **Not** implemented:

- Persisting the document server-side, copying it into a category-specific
  folder, and exposing a download portal for the signer. Only the signing
  log is stored; reintroducing file storage later (for stage 2) means
  adding a file store service and wiring it into `DocumentsController`
  without changing the DB schema.
